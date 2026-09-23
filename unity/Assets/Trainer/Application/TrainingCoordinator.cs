using System;
using System.Collections.Generic;
using System.Linq;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum TrainingCommandType
    {
        EmergencyStop,
        Abort,
        FinishSession,
        FinishAttempt,
        Pause,
        ResetEmergencyStop,
        Disarm,
        DisconnectClamp,
        ConnectClamp,
        SelectWeldingNozzle,
        SelectCleaningNozzle,
        Arm,
        Resume
    }

    public readonly struct TrainingCommand
    {
        public readonly TrainingCommandType Type;
        public readonly double Time;
        public readonly long Sequence;

        public TrainingCommand(
            TrainingCommandType type,
            double time,
            long sequence)
        {
            Type = type;
            Time = time;
            Sequence = sequence;
        }

        public int Priority => Type switch
        {
            TrainingCommandType.EmergencyStop => 0,
            TrainingCommandType.Abort => 1,
            TrainingCommandType.FinishSession => 2,
            TrainingCommandType.FinishAttempt => 3,
            TrainingCommandType.Pause => 4,
            TrainingCommandType.ResetEmergencyStop => 5,
            TrainingCommandType.Disarm => 6,
            TrainingCommandType.DisconnectClamp => 7,
            TrainingCommandType.ConnectClamp => 8,
            TrainingCommandType.SelectWeldingNozzle => 9,
            TrainingCommandType.SelectCleaningNozzle => 9,
            TrainingCommandType.Arm => 10,
            TrainingCommandType.Resume => 11,
            _ => 99
        };
    }

    public sealed class TrainingCoordinator : IDisposable
    {
        private readonly IMonotonicClock _clock;
        private readonly IIdSource _ids;
        private readonly ITrainingInputPort _input;
        private readonly IRegistrationPort _registration;
        private readonly ISafetyPort _safety;
        private readonly IRecorderPort _recorder;
        private readonly ISnapshotSink _sink;
        private readonly IHapticStop _haptics;

        private readonly List<TrainingCommand> _commands = new();

        private long _commandSequence;
        private long _eventSequence;
        private long _snapshotSequence;
        private long _inputGeneration;
        private long _registrationGeneration;

        private string _sessionId;
        private AttemptConfiguration _attempt;
        private SessionState _state = SessionState.Idle;
        private ActivationState _activation = ActivationState.Inhibited;
        private NozzleState _nozzle = NozzleState.Unknown;
        private ClampState _clamp = ClampState.Disconnected;
        private BlockReason _reasons = BlockReason.NoSession;
        private TrainingInput _lastInput;

        private bool _hasAcceptedRegistration;
        private bool _emergencyStopLatched;
        private bool _triggerReleaseRequired;
        private bool _disposed;

        public ProcessSnapshot Current { get; private set; }
        public event Action<ProcessEvent> EventEmitted;

        public TrainingCoordinator(
            IMonotonicClock clock,
            IIdSource ids,
            ITrainingInputPort input,
            IRegistrationPort registration,
            ISafetyPort safety,
            IRecorderPort recorder,
            ISnapshotSink sink,
            IHapticStop haptics)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
            _safety = safety ?? throw new ArgumentNullException(nameof(safety));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _haptics = haptics ?? throw new ArgumentNullException(nameof(haptics));

            Publish(default);
        }

        public void StartSession()
        {
            ThrowIfDisposed();

            if (_state != SessionState.Idle &&
                _state != SessionState.Completed &&
                _state != SessionState.Aborted)
            {
                throw new InvalidOperationException("Session already active.");
            }

            double now = RequireFiniteTime(_clock.Seconds);

            _commands.Clear();
            _sessionId = _ids.NewId();
            _attempt = null;
            _inputGeneration = 0;
            _registrationGeneration = 0;
            _hasAcceptedRegistration = false;
            _lastInput = default;
            _nozzle = NozzleState.Unknown;
            _clamp = ClampState.Disconnected;
            _emergencyStopLatched = false;
            _triggerReleaseRequired = true;
            _activation = ActivationState.Inhibited;
            _reasons = BlockReason.NoSession;

            Transition(
                SessionState.Initializing,
                ProcessEventType.SessionStarted,
                "created",
                now);

            Transition(
                SessionState.Registering,
                ProcessEventType.StateChanged,
                "await-registration",
                now);
        }

        public AttemptConfiguration PrepareAttempt(ProcessConfiguration process)
        {
            ThrowIfDisposed();

            if (process == null)
                throw new ArgumentNullException(nameof(process));

            if (_state != SessionState.Selecting &&
                _state != SessionState.Reviewing)
            {
                throw new InvalidOperationException(
                    "Prepare is only valid from Selecting or Reviewing.");
            }

            if (_activation == ActivationState.Armed ||
                _activation == ActivationState.Active)
            {
                throw new InvalidOperationException("Disarm before preparing an attempt.");
            }

            double now = RequireFiniteTime(_clock.Seconds);
            RegistrationInput registration = _registration.Capture(now);

            if (!registration.Available || !registration.Valid)
                throw new InvalidOperationException("Registration is unavailable or invalid.");

            if (!_hasAcceptedRegistration ||
                registration.RegistrationGeneration != _registrationGeneration)
            {
                ApplyRegistrationReplacementSideEffects(registration, now);
                throw new InvalidOperationException(
                    "Registration changed. Finish the session and perform fresh registration.");
            }

            if (!string.Equals(
                    registration.FixtureId,
                    process.FixtureId,
                    StringComparison.Ordinal))
            {
                ApplyRegistrationReplacementSideEffects(registration, now);
                throw new InvalidOperationException("Registration/config fixture mismatch.");
            }

            AttemptConfiguration nextAttempt = new(
                _ids.NewId(),
                process,
                registration.RegistrationGeneration);

            _recorder.Begin(nextAttempt);
            _attempt = nextAttempt;
            _activation = ActivationState.ReadyDisarmed;
            _triggerReleaseRequired = true;

            // A new attempt always requires an explicit clamp connection.
            // This avoids implicitly carrying a preparation state across attempts.
            if (_clamp == ClampState.Connected)
            {
                _clamp = ClampState.Disconnected;
                Emit(
                    ProcessEventType.ClampChanged,
                    "disconnected-new-attempt",
                    now);
            }

            Transition(
                SessionState.Ready,
                ProcessEventType.AttemptPrepared,
                process.Profile.Hash,
                now);

            return _attempt;
        }

        public long Submit(TrainingCommandType type, double time)
        {
            ThrowIfDisposed();

            if (!double.IsFinite(time))
                throw new ArgumentOutOfRangeException(nameof(time));

            long sequence = ++_commandSequence;
            _commands.Add(new TrainingCommand(type, time, sequence));
            return sequence;
        }

        public void Tick()
        {
            if (_disposed)
                return;

            double now = _clock.Seconds;
            if (!double.IsFinite(now))
            {
                _activation = ActivationState.Inhibited;
                _reasons |= BlockReason.SystemInvalid;
                _triggerReleaseRequired = true;
                _haptics.StopImmediately();
                Publish(_lastInput);
                return;
            }

            TrainingInput input = _input.Capture(now);
            RegistrationInput registration = _registration.Capture(now);

            _lastInput = input;
            _inputGeneration = input.InputGeneration;

            if (input.EmergencyStopPressed)
            {
                _commands.Add(
                    new TrainingCommand(
                        TrainingCommandType.EmergencyStop,
                        now,
                        ++_commandSequence));
            }

            TrainingCommand[] dueCommands = _commands
                .Where(command => command.Time <= now)
                .OrderBy(command => command.Time)
                .ThenBy(command => command.Priority)
                .ThenBy(command => command.Sequence)
                .ToArray();

            foreach (TrainingCommand command in dueCommands)
            {
                Apply(command, input, registration, now);
                _commands.Remove(command);
            }

            if (_state == SessionState.Registering)
            {
                if (registration.Available &&
                    registration.Valid &&
                    !string.IsNullOrWhiteSpace(registration.FixtureId))
                {
                    _registrationGeneration = registration.RegistrationGeneration;
                    _hasAcceptedRegistration = true;
                    _reasons = BlockReason.None;
                    Emit(
                        ProcessEventType.RegistrationAccepted,
                        registration.FixtureId,
                        now);

                    Transition(
                        SessionState.Selecting,
                        ProcessEventType.StateChanged,
                        "select",
                        now);
                }
                else
                {
                    _reasons = BlockReason.RegistrationInvalid;
                }
            }

            if (_state == SessionState.Ready ||
                _state == SessionState.Running ||
                _state == SessionState.Suspended)
            {
                Evaluate(input, registration, now);
            }
            else if (_state == SessionState.Reviewing)
            {
                ApplyRegistrationReplacementSideEffects(registration, now);
            }

            Publish(input);
        }

        private void Evaluate(
            TrainingInput input,
            RegistrationInput registration,
            double now)
        {
            if (_triggerReleaseRequired &&
                input.TriggerReleased &&
                !input.TriggerPressed)
            {
                _triggerReleaseRequired = false;
            }

            ApplyRegistrationReplacementSideEffects(registration, now);

            BlockReason reasons = ComputeBlockReasons(
                input,
                registration,
                now,
                includeEmergencyStop: true,
                includeTriggerRelease: true);

            if (_state == SessionState.Running)
            {
                if (reasons != BlockReason.None)
                {
                    _activation = _emergencyStopLatched
                        ? ActivationState.ResetRequired
                        : ActivationState.Inhibited;

                    _triggerReleaseRequired = true;
                    _haptics.StopImmediately();

                    Transition(
                        SessionState.Suspended,
                        ProcessEventType.Suspended,
                        reasons.ToString(),
                        now);
                }
                else
                {
                    _activation = input.TriggerPressed
                        ? ActivationState.Active
                        : ActivationState.Armed;
                }
            }
            else if (_state == SessionState.Ready)
            {
                if (_emergencyStopLatched)
                    _activation = ActivationState.ResetRequired;
                else
                    _activation = reasons == BlockReason.None
                        ? ActivationState.ReadyDisarmed
                        : ActivationState.Inhibited;
            }
            else if (_state == SessionState.Suspended)
            {
                _activation = _emergencyStopLatched
                    ? ActivationState.ResetRequired
                    : ActivationState.Inhibited;
            }

            _reasons = reasons;
        }

        private BlockReason ComputeBlockReasons(
            TrainingInput input,
            RegistrationInput registration,
            double now,
            bool includeEmergencyStop,
            bool includeTriggerRelease)
        {
            BlockReason reasons = BlockReason.None;

            if (!input.Available)
                reasons |= BlockReason.InputUnavailable;

            if (input.SystemInvalid)
                reasons |= BlockReason.SystemInvalid;

            if (!registration.Available || !registration.Valid)
                reasons |= BlockReason.RegistrationInvalid;

            if (_attempt != null && registration.Available && registration.Valid)
            {
                if (registration.RegistrationGeneration != _attempt.RegistrationGeneration)
                    reasons |= BlockReason.RegistrationChanged;

                if (!string.Equals(
                        registration.FixtureId,
                        _attempt.Process.FixtureId,
                        StringComparison.Ordinal))
                {
                    reasons |= BlockReason.FixtureMismatch;
                }
            }

            if (input.Available &&
                registration.Available &&
                registration.Valid &&
                registration.OriginGeneration != input.OriginGeneration)
            {
                reasons |= BlockReason.OriginMismatch;
            }

            if (!input.HeadValid)
                reasons |= BlockReason.HeadInvalid;

            if (!input.ToolValid)
                reasons |= BlockReason.ToolInvalid;

            if (_attempt != null)
            {
                bool invalidTimestamp =
                    !double.IsFinite(input.CapturedSeconds) ||
                    input.CapturedSeconds > now;

                bool stale =
                    !invalidTimestamp &&
                    now - input.CapturedSeconds >
                    _attempt.Process.Profile.MaximumSampleGapSeconds;

                if (invalidTimestamp || stale)
                    reasons |= BlockReason.StaleInput;

                if (_nozzle == NozzleState.Unknown)
                {
                    reasons |= BlockReason.NozzleUnknown;
                }
                else
                {
                    bool weldingMode =
                        _attempt.Process.Profile.Mode <= ProcessMode.Pulsed;

                    if ((weldingMode && _nozzle != NozzleState.Welding) ||
                        (!weldingMode && _nozzle != NozzleState.Cleaning))
                    {
                        reasons |= BlockReason.NozzleMismatch;
                    }
                }
            }

            if (_clamp != ClampState.Connected)
                reasons |= BlockReason.ClampDisconnected;

            if (!_recorder.Available)
                reasons |= BlockReason.RecorderUnavailable;

            if (includeEmergencyStop && _emergencyStopLatched)
                reasons |= BlockReason.EmergencyStop;

            if (includeTriggerRelease && _triggerReleaseRequired)
                reasons |= BlockReason.TriggerReleaseRequired;

            if (_attempt == null ||
                !input.Available ||
                !registration.Available ||
                !registration.Valid)
            {
                reasons |= BlockReason.SafetyUnknown;
            }
            else
            {
                SafetyInput safety = _safety.Evaluate(input, registration, _attempt);

                if (!safety.Available)
                {
                    reasons |= BlockReason.SafetyUnknown;
                }
                else if (!safety.Allowed)
                {
                    reasons |= BlockReason.SafetyRejected | safety.Reasons;
                }

                if (safety.FaultLatched)
                    reasons |= BlockReason.FaultLatched;
            }

            return reasons;
        }

        private void Apply(
            TrainingCommand command,
            TrainingInput input,
            RegistrationInput registration,
            double now)
        {
            switch (command.Type)
            {
                case TrainingCommandType.EmergencyStop:
                    ApplyEmergencyStop(command.Time);
                    break;

                case TrainingCommandType.ResetEmergencyStop:
                    ApplyEmergencyReset(input, registration, now, command.Time);
                    break;

                case TrainingCommandType.Abort:
                    ApplyAbort(now);
                    break;

                case TrainingCommandType.FinishAttempt:
                    ApplyFinishAttempt(now);
                    break;

                case TrainingCommandType.FinishSession:
                    ApplyFinishSession(now);
                    break;

                case TrainingCommandType.Pause:
                    ApplyPause(command.Time);
                    break;

                case TrainingCommandType.Resume:
                    ApplyResume(command.Time);
                    break;

                case TrainingCommandType.Disarm:
                    ApplyDisarm(command.Time);
                    break;

                case TrainingCommandType.DisconnectClamp:
                    ApplyDisconnectClamp(command.Time);
                    break;

                case TrainingCommandType.ConnectClamp:
                    ApplyConnectClamp(command.Time);
                    break;

                case TrainingCommandType.SelectWeldingNozzle:
                    ApplyNozzleSelection(NozzleState.Welding, command.Time);
                    break;

                case TrainingCommandType.SelectCleaningNozzle:
                    ApplyNozzleSelection(NozzleState.Cleaning, command.Time);
                    break;

                case TrainingCommandType.Arm:
                    ApplyArm(input, registration, now, command.Time);
                    break;
            }
        }

        private void ApplyEmergencyStop(double time)
        {
            if (_emergencyStopLatched)
                return;

            _emergencyStopLatched = true;
            _triggerReleaseRequired = true;
            _activation = ActivationState.ResetRequired;
            _haptics.StopImmediately();

            Emit(ProcessEventType.EmergencyStop, "latched", time);

            if (_state == SessionState.Running)
            {
                Transition(
                    SessionState.Suspended,
                    ProcessEventType.Suspended,
                    "estop",
                    time);
            }
        }

        private void ApplyEmergencyReset(
            TrainingInput input,
            RegistrationInput registration,
            double now,
            double commandTime)
        {
            if (!_emergencyStopLatched ||
                input.TriggerPressed ||
                _triggerReleaseRequired)
            {
                return;
            }

            ApplyRegistrationReplacementSideEffects(registration, now);

            BlockReason prerequisites = ComputeBlockReasons(
                input,
                registration,
                now,
                includeEmergencyStop: false,
                includeTriggerRelease: false);

            if (prerequisites != BlockReason.None)
                return;

            _emergencyStopLatched = false;
            _activation = ActivationState.ReadyDisarmed;
            Emit(ProcessEventType.EmergencyReset, "reset", commandTime);
        }

        private void ApplyAbort(double now)
        {
            if (_state == SessionState.Faulted ||
                _state == SessionState.Completed ||
                _state == SessionState.Aborted ||
                _state == SessionState.Idle)
            {
                return;
            }

            if (_attempt != null &&
                (_state == SessionState.Ready ||
                 _state == SessionState.Running ||
                 _state == SessionState.Suspended))
            {
                FinalizeAttempt(
                    interrupted: true,
                    successState: SessionState.Aborted,
                    successEvent: ProcessEventType.SessionAborted,
                    successPayload: "abort",
                    now: now,
                    disconnectClampOnSuccess: true);
                return;
            }

            _activation = ActivationState.Inhibited;
            _triggerReleaseRequired = true;
            _haptics.StopImmediately();
            DisconnectClamp("disconnected-session-end", now);

            Transition(
                SessionState.Aborted,
                ProcessEventType.SessionAborted,
                "abort",
                now);
        }

        private void ApplyFinishAttempt(double now)
        {
            if (_attempt == null ||
                (_state != SessionState.Ready &&
                 _state != SessionState.Running &&
                 _state != SessionState.Suspended))
            {
                return;
            }

            // If E-stop already won this timestamp, or the attempt is already
            // suspended, finalization must preserve that it was interrupted.
            bool interrupted =
                _emergencyStopLatched ||
                _state == SessionState.Suspended;

            FinalizeAttempt(
                interrupted: interrupted,
                successState: SessionState.Reviewing,
                successEvent: ProcessEventType.ReviewReady,
                successPayload: _attempt.AttemptId,
                now: now,
                disconnectClampOnSuccess: false);
        }

        private void ApplyFinishSession(double now)
        {
            if (_state != SessionState.Reviewing)
                return;

            _activation = ActivationState.Inhibited;
            _triggerReleaseRequired = true;
            DisconnectClamp("disconnected-session-end", now);

            Transition(
                SessionState.Completed,
                ProcessEventType.SessionCompleted,
                "complete",
                now);
        }

        private void ApplyPause(double time)
        {
            if (_state != SessionState.Running)
                return;

            _activation = ActivationState.Inhibited;
            _triggerReleaseRequired = true;
            _haptics.StopImmediately();

            Transition(
                SessionState.Suspended,
                ProcessEventType.Suspended,
                "pause",
                time);
        }

        private void ApplyResume(double time)
        {
            if (_state != SessionState.Suspended || _emergencyStopLatched)
                return;

            _activation = ActivationState.ReadyDisarmed;

            Transition(
                SessionState.Ready,
                ProcessEventType.Resumed,
                "ready",
                time);
        }

        private void ApplyDisarm(double time)
        {
            if (_state != SessionState.Running)
                return;

            _activation = ActivationState.ReadyDisarmed;
            _triggerReleaseRequired = true;
            _haptics.StopImmediately();

            Transition(
                SessionState.Ready,
                ProcessEventType.Disarmed,
                "disarm",
                time);
        }

        private void ApplyDisconnectClamp(double time)
        {
            if (_clamp == ClampState.Disconnected)
                return;

            _clamp = ClampState.Disconnected;
            Emit(ProcessEventType.ClampChanged, "disconnected", time);

            if (_state == SessionState.Running)
            {
                _activation = ActivationState.Inhibited;
                _triggerReleaseRequired = true;
                _haptics.StopImmediately();

                Transition(
                    SessionState.Suspended,
                    ProcessEventType.Suspended,
                    "clamp",
                    time);
            }
        }

        private void ApplyConnectClamp(double time)
        {
            if (!PreparationIsMutable() || _clamp == ClampState.Connected)
                return;

            _clamp = ClampState.Connected;
            Emit(ProcessEventType.ClampChanged, "connected", time);
        }

        private void ApplyNozzleSelection(NozzleState nozzle, double time)
        {
            if (!PreparationIsMutable() || _nozzle == nozzle)
                return;

            _nozzle = nozzle;
            Emit(
                ProcessEventType.NozzleChanged,
                nozzle == NozzleState.Welding ? "welding" : "cleaning",
                time);
        }

        private void ApplyArm(
            TrainingInput input,
            RegistrationInput registration,
            double now,
            double commandTime)
        {
            // Duplicate or stale Arm commands are safe no-ops.
            if (_state != SessionState.Ready || _attempt == null)
                return;

            if (input.TriggerPressed || _triggerReleaseRequired)
            {
                _triggerReleaseRequired = true;
                _activation = ActivationState.Inhibited;
                return;
            }

            ApplyRegistrationReplacementSideEffects(registration, now);

            BlockReason reasons = ComputeBlockReasons(
                input,
                registration,
                now,
                includeEmergencyStop: true,
                includeTriggerRelease: true);

            _reasons = reasons;

            if (reasons != BlockReason.None)
            {
                _activation = _emergencyStopLatched
                    ? ActivationState.ResetRequired
                    : ActivationState.Inhibited;
                return;
            }

            _activation = ActivationState.Armed;

            Transition(
                SessionState.Running,
                ProcessEventType.Armed,
                "armed",
                commandTime);
        }

        private bool PreparationIsMutable()
        {
            if (_activation == ActivationState.Armed ||
                _activation == ActivationState.Active)
            {
                return false;
            }

            return _state == SessionState.Selecting ||
                   _state == SessionState.Ready ||
                   _state == SessionState.Suspended ||
                   _state == SessionState.Reviewing;
        }

        private void ApplyRegistrationReplacementSideEffects(
            RegistrationInput registration,
            double now)
        {
            if (_attempt == null ||
                !registration.Available ||
                !registration.Valid)
            {
                return;
            }

            bool generationChanged =
                registration.RegistrationGeneration != _attempt.RegistrationGeneration;

            bool fixtureChanged =
                !string.Equals(
                    registration.FixtureId,
                    _attempt.Process.FixtureId,
                    StringComparison.Ordinal);

            if ((generationChanged || fixtureChanged) &&
                _clamp == ClampState.Connected)
            {
                _clamp = ClampState.Disconnected;
                Emit(
                    ProcessEventType.ClampChanged,
                    $"disconnected-registration-change:{registration.RegistrationGeneration}",
                    now);
            }
        }

        private bool FinalizeAttempt(
            bool interrupted,
            SessionState successState,
            ProcessEventType successEvent,
            string successPayload,
            double now,
            bool disconnectClampOnSuccess)
        {
            if (_attempt == null)
                return true;

            _activation = ActivationState.Inhibited;
            _triggerReleaseRequired = true;
            _haptics.StopImmediately();

            Transition(
                SessionState.Saving,
                ProcessEventType.SavingStarted,
                interrupted ? "interrupted" : "complete",
                now);

            if (!_recorder.Available)
            {
                Transition(
                    SessionState.Faulted,
                    ProcessEventType.SaveFailed,
                    "recorder-unavailable",
                    now);
                return false;
            }

            if (!_recorder.Finish(
                    _attempt.AttemptId,
                    interrupted,
                    out string error))
            {
                Transition(
                    SessionState.Faulted,
                    ProcessEventType.SaveFailed,
                    error,
                    now);
                return false;
            }

            if (disconnectClampOnSuccess)
                DisconnectClamp("disconnected-session-end", now);

            Transition(
                successState,
                successEvent,
                successPayload,
                now);

            return true;
        }

        private void DisconnectClamp(string payload, double time)
        {
            if (_clamp == ClampState.Disconnected)
                return;

            _clamp = ClampState.Disconnected;
            Emit(ProcessEventType.ClampChanged, payload, time);
        }

        private void Transition(
            SessionState state,
            ProcessEventType type,
            string payload,
            double time)
        {
            _state = state;
            Emit(type, payload, time);
        }

        private void Emit(
            ProcessEventType type,
            string payload,
            double time)
        {
            ProcessEvent processEvent = new(
                ++_eventSequence,
                time,
                _sessionId,
                _attempt?.AttemptId,
                _inputGeneration,
                _registrationGeneration,
                _attempt?.Process.Profile.Hash,
                type,
                payload);

            EventEmitted?.Invoke(processEvent);

            if (_recorder.Available)
                _recorder.Append(processEvent);
        }

        private void Publish(TrainingInput input)
        {
            Current = new ProcessSnapshot(
                ++_snapshotSequence,
                _sessionId,
                _attempt?.AttemptId,
                _state,
                _activation,
                _nozzle,
                _clamp,
                _reasons,
                input.TriggerPressed);

            _sink.Publish(Current);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _activation = ActivationState.Inhibited;
            _triggerReleaseRequired = true;
            _clamp = ClampState.Disconnected;
            _haptics.StopImmediately();

            // Publish the fail-closed state before tearing down downstream ports.
            Publish(_lastInput);

            _registration.Teardown();
            _recorder.Teardown();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TrainingCoordinator));
        }

        private static double RequireFiniteTime(double time)
        {
            if (!double.IsFinite(time))
                throw new InvalidOperationException("Monotonic clock returned a non-finite time.");

            return time;
        }
    }
}
