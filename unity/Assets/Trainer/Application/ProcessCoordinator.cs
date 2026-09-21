using System;
using System.Collections.Generic;
using System.Linq;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    /// Sole owner of production session, attempt, activation and event ordering.
    public sealed class ProcessCoordinator : IDisposable
    {
        private readonly IMonotonicClock _clock;
        private readonly IIdentifierSource _ids;
        private readonly IInputSnapshotSource _input;
        private readonly IRegistrationStateSource _registration;
        private readonly IActivationSafetyPort _safety;
        private readonly IProcessRecorder _recorder;
        private readonly IProcessSnapshotSink _snapshotSink;
        private readonly IHapticLifecyclePort _haptics;
        private readonly List<ProcessCommand> _commands = new();
        private readonly List<ProcessFault> _faults = new();

        private long _commandSequence, _eventSequence, _snapshotSequence;
        private string _sessionId, _nozzleId;
        private AttemptConfiguration _attempt;
        private SessionLifecycle _lifecycle = SessionLifecycle.Idle;
        private ActivationState _activation = ActivationState.Disarmed;
        private ClampState _clamp = ClampState.Disconnected;
        private bool _emergencyStopLatched, _triggerReleaseRequired;
        private long _registrationGeneration, _inputGeneration;
        private double _lastInputTimestamp = double.NaN;
        private InhibitReason _reasons = InhibitReason.SessionNotRunning;
        private bool _disposed;

        public ProcessSnapshot Current { get; private set; }
        public event Action<ProcessEvent> EventEmitted;
        public event Action<ProcessSnapshot> SnapshotChanged;

        public ProcessCoordinator(IMonotonicClock clock, IIdentifierSource ids,
            IInputSnapshotSource input, IRegistrationStateSource registration,
            IActivationSafetyPort safety, IProcessRecorder recorder,
            IProcessSnapshotSink snapshotSink, IHapticLifecyclePort haptics)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
            _safety = safety ?? throw new ArgumentNullException(nameof(safety));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _snapshotSink = snapshotSink ?? throw new ArgumentNullException(nameof(snapshotSink));
            _haptics = haptics ?? throw new ArgumentNullException(nameof(haptics));
            Publish(_clock.Seconds, default, default);
        }

        public void StartSession()
        {
            if (_lifecycle != SessionLifecycle.Idle && _lifecycle != SessionLifecycle.Completed &&
                _lifecycle != SessionLifecycle.Aborted) throw new InvalidOperationException("Session is already active.");
            _sessionId = _ids.NewId(); _attempt = null; _nozzleId = null;
            _clamp = ClampState.Disconnected; _activation = ActivationState.Disarmed;
            _faults.Clear(); _emergencyStopLatched = false; _triggerReleaseRequired = false;
            Transition(SessionLifecycle.Initializing, ProcessEventType.SessionStarted, "session-created");
            Transition(SessionLifecycle.Registering, ProcessEventType.LifecycleChanged, "awaiting-registration");
        }

        public AttemptConfiguration PrepareAttempt(ContentSnapshot content, string seamId,
            ProcessMode mode, string sourceAttemptId = null)
        {
            if (_lifecycle != SessionLifecycle.Selecting && _lifecycle != SessionLifecycle.Reviewing)
                throw new InvalidOperationException("Attempts can only be prepared while Selecting or Reviewing.");
            if (_activation == ActivationState.Armed || _activation == ActivationState.Active)
                throw new InvalidOperationException("Disarm before changing process, seam or profile.");
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (!content.Entry.Workpiece.Seams.Any(x => x.Id == seamId))
                throw new ArgumentException("Unknown seam ID.", nameof(seamId));
            ProcessProfile profile = content.Entry.Profiles.SingleOrDefault(x => x.Settings.Mode == mode)
                ?? throw new ArgumentException("Selected content does not contain the requested process mode.", nameof(mode));
            _attempt = new AttemptConfiguration(_ids.NewId(), seamId, mode, profile, content, sourceAttemptId);
            _activation = ActivationState.Disarmed; _lastInputTimestamp = double.NaN;
            _recorder.BeginAttempt(_attempt);
            Transition(SessionLifecycle.Ready, ProcessEventType.AttemptPrepared,
                $"mode={mode};seam={seamId};source={sourceAttemptId ?? "none"}");
            return _attempt;
        }

        public long Submit(ProcessCommandType type, double timestampSeconds, string value = null)
        {
            long sequence = ++_commandSequence;
            _commands.Add(new ProcessCommand(type, timestampSeconds, sequence, value));
            return sequence;
        }

        public void Tick()
        {
            double now = _clock.Seconds;
            InputSnapshot input = _input.Capture(now);
            RegistrationSnapshot registration = _registration.Capture(now);
            _inputGeneration = input.Generation;

            if (input.EmergencyStopPressed)
                _commands.Add(new ProcessCommand(ProcessCommandType.EmergencyStop, now, ++_commandSequence));

            foreach (ProcessCommand command in _commands.Where(x => x.TimestampSeconds <= now)
                .OrderBy(x => x.TimestampSeconds).ThenBy(x => x.Priority).ThenBy(x => x.SubmissionSequence).ToArray())
            {
                Apply(command, input, registration);
                _commands.Remove(command);
            }

            if (_lifecycle == SessionLifecycle.Registering && registration.Available && registration.Valid)
            {
                _registrationGeneration = registration.Generation;
                Emit(ProcessEventType.RegistrationAccepted, "registration-valid", now);
                Transition(SessionLifecycle.Selecting, ProcessEventType.LifecycleChanged, "select-content");
            }

            if (_lifecycle == SessionLifecycle.Running || _lifecycle == SessionLifecycle.Ready ||
                _lifecycle == SessionLifecycle.Suspended)
                Evaluate(now, input, registration);

            Publish(now, input, registration);
        }

        private void Evaluate(double now, InputSnapshot input, RegistrationSnapshot registration)
        {
            InhibitReason reasons = InhibitReason.None;
            if (!input.Available) reasons |= InhibitReason.MissingInput;
            if (!registration.Available) reasons |= InhibitReason.MissingRegistration;
            else if (!registration.Valid) reasons |= InhibitReason.RegistrationInvalid;
            else if (_registrationGeneration != 0 && registration.Generation != _registrationGeneration)
                reasons |= InhibitReason.RegistrationChanged;
            if (!input.HeadValid) reasons |= InhibitReason.HeadTrackingInvalid;
            if (!input.ToolValid) reasons |= InhibitReason.ToolTrackingInvalid;
            if (string.IsNullOrWhiteSpace(_nozzleId)) reasons |= InhibitReason.NozzleUnknown;
            else if (_attempt != null && !string.Equals(_nozzleId, _attempt.Profile.RequiredNozzleId, StringComparison.Ordinal))
                reasons |= InhibitReason.NozzleMismatch;
            if (_clamp != ClampState.Connected) reasons |= InhibitReason.ClampDisconnected;
            if (_recorder.Health != RecorderHealth.Ready) reasons |= InhibitReason.RecorderUnavailable;
            if (_emergencyStopLatched) reasons |= InhibitReason.EmergencyStopLatched;
            if (_triggerReleaseRequired) reasons |= InhibitReason.TriggerReleaseRequired;

            if (!double.IsNaN(_lastInputTimestamp) && input.SourceTimestampSeconds - _lastInputTimestamp >
                (_attempt?.Profile.MaximumSampleGapSeconds ?? 0.1))
                reasons |= InhibitReason.MaximumSampleGapExceeded;
            if (input.Available && (double.IsNaN(_lastInputTimestamp) || input.SourceTimestampSeconds >= _lastInputTimestamp))
                _lastInputTimestamp = input.SourceTimestampSeconds;

            SafetyDecision safety = _attempt == null ? SafetyDecision.Missing :
                _safety.Evaluate(new EvaluationRequest(now, input, registration, _attempt));
            if (!safety.Available) reasons |= InhibitReason.SafetyProviderUnavailable;
            else if (!safety.Allowed) reasons |= InhibitReason.SafetyRejected | safety.Reasons;

            if (_triggerReleaseRequired && !input.TriggerPressed) _triggerReleaseRequired = false;
            if (_lifecycle == SessionLifecycle.Running &&
                (reasons & (InhibitReason.RegistrationInvalid | InhibitReason.RegistrationChanged |
                 InhibitReason.MissingRegistration | InhibitReason.ToolTrackingInvalid |
                 InhibitReason.HeadTrackingInvalid | InhibitReason.MaximumSampleGapExceeded)) != 0)
            {
                _activation = ActivationState.Inhibited;
                Transition(SessionLifecycle.Suspended, ProcessEventType.Suspended, reasons.ToString());
            }

            ActivationState previous = _activation;
            if (_emergencyStopLatched) _activation = ActivationState.EmergencyStopped;
            else if (_activation == ActivationState.Armed || _activation == ActivationState.Active ||
                     _activation == ActivationState.Inhibited)
            {
                if (reasons != InhibitReason.None) _activation = ActivationState.Inhibited;
                else _activation = input.TriggerPressed ? ActivationState.Active : ActivationState.Armed;
            }
            _reasons = reasons;
            if (previous != _activation) Emit(ProcessEventType.ActivationChanged, $"{previous}->{_activation};{reasons}", now);
        }

        private void Apply(ProcessCommand command, InputSnapshot input, RegistrationSnapshot registration)
        {
            switch (command.Type)
            {
                case ProcessCommandType.EmergencyStop:
                    if (_emergencyStopLatched) break;
                    _emergencyStopLatched = true; _triggerReleaseRequired = true;
                    _activation = ActivationState.EmergencyStopped; _haptics.StopImmediately();
                    Emit(ProcessEventType.EmergencyStopLatched, "emergency-stop", command.TimestampSeconds);
                    if (_lifecycle == SessionLifecycle.Running) Transition(SessionLifecycle.Suspended, ProcessEventType.Suspended, "emergency-stop");
                    break;
                case ProcessCommandType.AbortSession: Abort(command.TimestampSeconds); break;
                case ProcessCommandType.FinishSession:
                    if (_lifecycle != SessionLifecycle.Reviewing)
                        throw new InvalidOperationException("Finish session requires Reviewing state.");
                    _attempt = null; _nozzleId = null; _clamp = ClampState.Disconnected;
                    _activation = ActivationState.Disarmed; _haptics.StopImmediately();
                    Transition(SessionLifecycle.Completed, ProcessEventType.SessionCompleted, "session-completed");
                    break;
                case ProcessCommandType.FinishAttempt: Finish(command.TimestampSeconds, false); break;
                case ProcessCommandType.Pause:
                    if (_lifecycle == SessionLifecycle.Running) { _activation = ActivationState.Disarmed; Transition(SessionLifecycle.Suspended, ProcessEventType.Suspended, "operator-pause"); }
                    break;
                case ProcessCommandType.ResetEmergencyStop:
                    if (_emergencyStopLatched && !input.TriggerPressed)
                    { _emergencyStopLatched = false; _activation = ActivationState.Disarmed; _triggerReleaseRequired = false; Emit(ProcessEventType.EmergencyStopReset, "explicit-reset", command.TimestampSeconds); }
                    break;
                case ProcessCommandType.Disarm:
                    if (_activation == ActivationState.Disarmed) break;
                    _activation = ActivationState.Disarmed; _triggerReleaseRequired = input.TriggerPressed;
                    Emit(ProcessEventType.Disarmed, "explicit-disarm", command.TimestampSeconds); break;
                case ProcessCommandType.DisconnectClamp:
                    if (_clamp == ClampState.Disconnected) break;
                    RequireMutablePreparation(); _clamp = ClampState.Disconnected; Emit(ProcessEventType.ClampChanged, "disconnected", command.TimestampSeconds); break;
                case ProcessCommandType.ConnectClamp:
                    if (_clamp == ClampState.Connected) break;
                    RequireMutablePreparation(); _clamp = ClampState.Connected; Emit(ProcessEventType.ClampChanged, "connected", command.TimestampSeconds); break;
                case ProcessCommandType.SelectNozzle:
                    if (string.Equals(_nozzleId, command.Value, StringComparison.Ordinal)) break;
                    RequireMutablePreparation(); _nozzleId = command.Value; Emit(ProcessEventType.NozzleChanged, _nozzleId, command.TimestampSeconds); break;
                case ProcessCommandType.Arm:
                    if (_lifecycle == SessionLifecycle.Running &&
                        (_activation == ActivationState.Armed || _activation == ActivationState.Active || _activation == ActivationState.Inhibited)) break;
                    if (_lifecycle != SessionLifecycle.Ready && _lifecycle != SessionLifecycle.Suspended)
                        throw new InvalidOperationException("Arm requires Ready or Suspended state.");
                    if (_emergencyStopLatched)
                    { _triggerReleaseRequired = true; _activation = ActivationState.EmergencyStopped; break; }
                    if (input.TriggerPressed)
                    { _triggerReleaseRequired = true; _activation = ActivationState.Inhibited; break; }
                    _activation = ActivationState.Armed;
                    if (_lifecycle == SessionLifecycle.Ready) Transition(SessionLifecycle.Running, ProcessEventType.Armed, "armed");
                    else Transition(SessionLifecycle.Running, ProcessEventType.Resumed, "explicit-rearm");
                    break;
                case ProcessCommandType.Resume:
                    if (_lifecycle != SessionLifecycle.Suspended) throw new InvalidOperationException("Resume requires Suspended state.");
                    _activation = ActivationState.Disarmed; Transition(SessionLifecycle.Ready, ProcessEventType.LifecycleChanged, "ready-to-rearm"); break;
            }
        }

        private void RequireMutablePreparation()
        {
            if (_lifecycle == SessionLifecycle.Running || _activation == ActivationState.Armed || _activation == ActivationState.Active)
                throw new InvalidOperationException("Disarm and leave Running before changing preparation state.");
        }

        private void Finish(double time, bool interrupted)
        {
            if (_attempt == null || (_lifecycle != SessionLifecycle.Running && _lifecycle != SessionLifecycle.Suspended)) return;
            _activation = ActivationState.Disarmed; _haptics.StopImmediately();
            Transition(SessionLifecycle.Saving, ProcessEventType.SavingStarted, interrupted ? "interrupted" : "completed");
            if (!_recorder.FinishAttempt(_attempt.AttemptId, interrupted, out string error))
            { AddFault("recording-save-failed", FaultSeverity.Inhibiting, error); Transition(SessionLifecycle.Faulted, ProcessEventType.SavingFailed, error); }
            else Transition(SessionLifecycle.Reviewing, ProcessEventType.ReviewReady, _attempt.AttemptId);
        }

        private void Abort(double time)
        {
            if (_attempt != null && (_lifecycle == SessionLifecycle.Running || _lifecycle == SessionLifecycle.Suspended)) Finish(time, true);
            _activation = ActivationState.Disarmed; _clamp = ClampState.Disconnected; _haptics.StopImmediately();
            Transition(SessionLifecycle.Aborted, ProcessEventType.SessionAborted, "operator-abort");
        }

        private void AddFault(string code, FaultSeverity severity, string detail)
        { _faults.Add(new ProcessFault(code, severity, detail)); Emit(ProcessEventType.FaultRaised, $"{code}:{detail}", _clock.Seconds); }

        private void Transition(SessionLifecycle next, ProcessEventType type, string payload)
        { _lifecycle = next; Emit(type, payload, _clock.Seconds); }

        private void Emit(ProcessEventType type, string payload, double timestamp)
        {
            var e = new ProcessEvent(++_eventSequence, timestamp, _sessionId, _attempt?.AttemptId,
                _inputGeneration, _registrationGeneration, _attempt?.Content.EvaluationHashSha256, type, payload);
            EventEmitted?.Invoke(e);
            if (_recorder.Health == RecorderHealth.Ready) _recorder.Append(e);
        }

        private void Publish(double now, InputSnapshot input, RegistrationSnapshot registration)
        {
            Current = new ProcessSnapshot(++_snapshotSequence, now, _sessionId, _attempt?.AttemptId,
                _lifecycle, _activation, _attempt?.Mode, _attempt?.SeamId, _nozzleId,
                _clamp, _registrationGeneration, _inputGeneration, _reasons, _faults.ToArray());
            _snapshotSink.Publish(Current); SnapshotChanged?.Invoke(Current);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activation = ActivationState.Disarmed; _clamp = ClampState.Disconnected;
            _haptics.StopImmediately(); _registration.Teardown(); _recorder.Teardown();
            if (_lifecycle != SessionLifecycle.Completed && _lifecycle != SessionLifecycle.Aborted)
                _lifecycle = SessionLifecycle.Aborted;
        }
    }
}
