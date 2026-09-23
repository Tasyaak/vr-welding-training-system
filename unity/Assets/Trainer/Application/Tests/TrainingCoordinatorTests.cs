using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class TrainingCoordinatorTests
    {
        private Clock clock;
        private Input input;
        private Registration registration;
        private Safety safety;
        private Recorder recorder;
        private Sink sink;
        private Haptics haptics;
        private TrainingCoordinator coordinator;

        [SetUp]
        public void Setup()
        {
            clock = new Clock();
            input = new Input();
            registration = new Registration();
            safety = new Safety();
            recorder = new Recorder();
            sink = new Sink();
            haptics = new Haptics();

            coordinator = new TrainingCoordinator(
                clock,
                new Ids(),
                input,
                registration,
                safety,
                recorder,
                sink,
                haptics);
        }

        [TearDown]
        public void Down()
        {
            coordinator.Dispose();
        }

        [Test]
        public void RegistrationIsRequiredBeforeSelection()
        {
            coordinator.StartSession();
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Registering));

            registration.Value = ValidRegistration();
            input.Value = Valid(false, true);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Selecting));
        }

        [Test]
        public void MissingSafetyNeverActivates()
        {
            Ready();
            safety.Value = default;

            PrepareAndArm();

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.Inhibited));

            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.SafetyUnknown),
                Is.True);
        }

        [Test]
        public void UnavailableInputNeverActivates()
        {
            Ready();
            Prepare();

            input.Value = new TrainingInput(
                clock.Seconds,
                2,
                3,
                available: false,
                head: true,
                tool: true,
                trigger: false,
                released: true,
                estop: false,
                invalid: false);

            coordinator.Tick();
            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Ready));

            Assert.That(
                coordinator.Current.Permission,
                Is.False);

            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.InputUnavailable),
                Is.True);
        }

        [Test]
        public void SystemInvalidNeverActivates()
        {
            Ready();
            Prepare();

            input.Value = new TrainingInput(
                clock.Seconds,
                2,
                3,
                available: true,
                head: true,
                tool: true,
                trigger: false,
                released: true,
                estop: false,
                invalid: true);

            coordinator.Tick();
            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();

            Assert.That(coordinator.Current.Permission, Is.False);
            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.SystemInvalid),
                Is.True);
        }


        [Test]
        public void UnavailableRecorderNeverActivates()
        {
            Ready();
            Prepare();

            recorder.Available = false;
            coordinator.Tick();
            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();

            Assert.That(coordinator.Current.Permission, Is.False);
            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.RecorderUnavailable),
                Is.True);
        }

        [TestCase(ProcessMode.Fusion, TrainingCommandType.SelectCleaningNozzle)]
        [TestCase(ProcessMode.Wobble, TrainingCommandType.SelectCleaningNozzle)]
        [TestCase(ProcessMode.Pulsed, TrainingCommandType.SelectCleaningNozzle)]
        [TestCase(ProcessMode.PreWeldCleaning, TrainingCommandType.SelectWeldingNozzle)]
        [TestCase(ProcessMode.PostWeldCleaning, TrainingCommandType.SelectWeldingNozzle)]
        public void WrongNozzleForEveryModePreventsArming(
            ProcessMode mode,
            TrainingCommandType wrongNozzleCommand)
        {
            Ready();

            coordinator.PrepareAttempt(Config(mode));
            coordinator.Submit(wrongNozzleCommand, clock.Seconds);
            coordinator.Submit(TrainingCommandType.ConnectClamp, clock.Seconds);
            coordinator.Tick();

            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();

            Assert.That(coordinator.Current.Permission, Is.False);
            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.NozzleMismatch),
                Is.True);
        }

        [Test]
        public void UnsupportedProcessModeIsRejectedByProfile()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ProcessProfile(
                    "unsupported",
                    1,
                    (ProcessMode)999,
                    "hash",
                    0.1));
        }

        [Test]
        public void EmergencyStopWinsSameTimestamp()
        {
            Ready();
            Prepare();

            List<ProcessEvent> events = new();
            coordinator.EventEmitted += events.Add;

            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Submit(TrainingCommandType.EmergencyStop, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.ResetRequired));

            Assert.That(haptics.Count, Is.GreaterThan(0));
            Assert.That(events.Any(e => e.Type == ProcessEventType.EmergencyStop), Is.True);
            Assert.That(events.Any(e => e.Type == ProcessEventType.Armed), Is.False);
        }

        [Test]
        public void EmergencyStopDominatesSimultaneousFinishAsInterrupted()
        {
            Ready();
            PrepareAndArm();

            coordinator.Submit(TrainingCommandType.FinishAttempt, clock.Seconds);
            coordinator.Submit(TrainingCommandType.EmergencyStop, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Reviewing));

            Assert.That(recorder.FinishedInterrupted, Has.Count.EqualTo(1));
            Assert.That(recorder.FinishedInterrupted[0], Is.True);
        }

        [Test]
        public void IdenticalSameTimestampScenarioProducesIdenticalEventOrder()
        {
            string[] first = RunSameTimestampScenario();
            string[] second = RunSameTimestampScenario();

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void DuplicateArmIsSafeAndEmitsArmedOnce()
        {
            Ready();
            Prepare();

            List<ProcessEvent> events = new();
            coordinator.EventEmitted += events.Add;

            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);

            Assert.DoesNotThrow(() => coordinator.Tick());

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Running));

            Assert.That(
                events.Count(e => e.Type == ProcessEventType.Armed),
                Is.EqualTo(1));
        }

        [Test]
        public void RegistrationLossSuspendsRunningAttempt()
        {
            Ready();
            PrepareAndArm();

            registration.Value = new RegistrationInput(
                true,
                false,
                7,
                3,
                "fixture");

            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Suspended));

            Assert.That(coordinator.Current.Permission, Is.False);
        }

        [Test]
        public void OriginMismatchPreventsArming()
        {
            Ready();
            Prepare();

            registration.Value = new RegistrationInput(
                true,
                true,
                7,
                4,
                "fixture");

            coordinator.Tick();
            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();

            Assert.That(coordinator.Current.Permission, Is.False);
            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.OriginMismatch),
                Is.True);
        }

        [Test]
        public void RegistrationReplacementSuspendsAndDisconnectsClamp()
        {
            Ready();
            PrepareAndArm();

            registration.Value = new RegistrationInput(
                true,
                true,
                8,
                3,
                "fixture");

            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Suspended));

            Assert.That(
                coordinator.Current.Clamp,
                Is.EqualTo(ClampState.Disconnected));

            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.RegistrationChanged),
                Is.True);
        }

        [Test]
        public void FixtureReplacementSuspendsAndDisconnectsClamp()
        {
            Ready();
            PrepareAndArm();

            registration.Value = new RegistrationInput(
                true,
                true,
                7,
                3,
                "other-fixture");

            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Suspended));

            Assert.That(
                coordinator.Current.Clamp,
                Is.EqualTo(ClampState.Disconnected));

            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.FixtureMismatch),
                Is.True);
        }

        [Test]
        public void RegistrationReplacementDuringReviewDisconnectsClamp()
        {
            Ready();
            PrepareAndArm();

            coordinator.Submit(TrainingCommandType.FinishAttempt, clock.Seconds);
            coordinator.Tick();

            Assert.That(coordinator.Current.Clamp, Is.EqualTo(ClampState.Connected));

            registration.Value = new RegistrationInput(
                true,
                true,
                8,
                3,
                "fixture");

            coordinator.Tick();

            Assert.That(
                coordinator.Current.Clamp,
                Is.EqualTo(ClampState.Disconnected));
        }

        [Test]
        public void ChangedRegistrationCannotBeSilentlyUsedForRetry()
        {
            Ready();
            PrepareAndArm();

            string first = coordinator.Current.AttemptId;

            coordinator.Submit(TrainingCommandType.FinishAttempt, clock.Seconds);
            coordinator.Tick();

            registration.Value = new RegistrationInput(
                true,
                true,
                8,
                3,
                "fixture");

            Assert.Throws<InvalidOperationException>(
                () => coordinator.PrepareAttempt(Config(ProcessMode.Fusion)));

            Assert.That(recorder.Finished, Does.Contain(first));
        }

        [Test]
        public void AttemptConfigurationCannotChangeWhileRunning()
        {
            Ready();
            PrepareAndArm();

            Assert.Throws<InvalidOperationException>(
                () => coordinator.PrepareAttempt(Config(ProcessMode.Wobble)));
        }

        [Test]
        public void RetryHasDistinctIdAndPreservesPreviousAttempt()
        {
            Ready();
            PrepareAndArm();

            string first = coordinator.Current.AttemptId;

            coordinator.Submit(
                TrainingCommandType.FinishAttempt,
                clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Reviewing));

            AttemptConfiguration retry =
                coordinator.PrepareAttempt(
                    Config(ProcessMode.Fusion));

            Assert.That(
                retry.AttemptId,
                Is.Not.EqualTo(first));

            coordinator.Tick();

            Assert.That(
                coordinator.Current.AttemptId,
                Is.EqualTo(retry.AttemptId));

            Assert.That(
                recorder.Finished,
                Does.Contain(first));
        }

        [Test]
        public void RetryRequiresExplicitClampReconnect()
        {
            Ready();
            PrepareAndArm();

            coordinator.Submit(TrainingCommandType.FinishAttempt, clock.Seconds);
            coordinator.Tick();

            coordinator.PrepareAttempt(Config(ProcessMode.Fusion));
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Clamp,
                Is.EqualTo(ClampState.Disconnected));

            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.ClampDisconnected),
                Is.True);
        }

        [Test]
        public void RecorderBecomingUnavailableBeforeFinishFaultsExplicitly()
        {
            Ready();
            PrepareAndArm();

            recorder.Available = false;
            coordinator.Submit(TrainingCommandType.FinishAttempt, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Faulted));
        }

        [Test]
        public void RecorderFailureIsExplicit()
        {
            Ready();
            PrepareAndArm();

            recorder.FinishOk = false;
            coordinator.Submit(TrainingCommandType.FinishAttempt, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Faulted));
        }

        [Test]
        public void AbortRecorderFailureRemainsFaulted()
        {
            Ready();
            PrepareAndArm();

            recorder.FinishOk = false;
            coordinator.Submit(TrainingCommandType.Abort, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Faulted));
        }

        [Test]
        public void FinishSessionDisconnectsClamp()
        {
            Ready();
            PrepareAndArm();

            coordinator.Submit(TrainingCommandType.FinishAttempt, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Reviewing));

            coordinator.Submit(TrainingCommandType.FinishSession, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Completed));

            Assert.That(
                coordinator.Current.Clamp,
                Is.EqualTo(ClampState.Disconnected));
        }

        [Test]
        public void EmittedEventsCarryCurrentInputGeneration()
        {
            Ready();
            Prepare();

            input.Value = Valid(false, true, inputGeneration: 42);
            coordinator.Tick();

            List<ProcessEvent> events = new();
            coordinator.EventEmitted += events.Add;

            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();

            ProcessEvent armed = events.Single(e => e.Type == ProcessEventType.Armed);

            Assert.That(armed.InputGeneration, Is.EqualTo(42));
            Assert.That(armed.RegistrationGeneration, Is.EqualTo(7));
        }

        [Test]
        public void EmergencyResetRequiresReleaseAndHealthyPrerequisites()
        {
            Ready();
            PrepareAndArm();

            coordinator.Submit(TrainingCommandType.EmergencyStop, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.ResetRequired));

            // Still held: reset must not clear the latch.
            input.Value = Valid(true, false);
            coordinator.Submit(TrainingCommandType.ResetEmergencyStop, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.ResetRequired));

            // Release is observed, but unsafe prerequisites still prevent reset.
            input.Value = Valid(false, true);
            safety.Value = new SafetyInput(
                true,
                false,
                false,
                BlockReason.SafetyRejected);
            coordinator.Tick();
            coordinator.Submit(TrainingCommandType.ResetEmergencyStop, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.ResetRequired));

            // Restore prerequisites; reset succeeds but remains suspended until Resume.
            safety.Value = new SafetyInput(true, true, false, BlockReason.None);
            coordinator.Submit(TrainingCommandType.ResetEmergencyStop, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Suspended));

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.Inhibited));

            coordinator.Submit(TrainingCommandType.Resume, clock.Seconds);
            coordinator.Tick();
            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Running));

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.Armed));
        }

        [Test]
        public void StaleInputSuspendsRunningAttempt()
        {
            Ready();
            PrepareAndArm();

            clock.Seconds = 2.0;
            input.Value = new TrainingInput(
                1.0,
                2,
                3,
                true,
                true,
                true,
                false,
                false,
                false,
                false);

            coordinator.Tick();

            Assert.That(
                coordinator.Current.Session,
                Is.EqualTo(SessionState.Suspended));

            Assert.That(
                coordinator.Current.Reasons.HasFlag(BlockReason.StaleInput),
                Is.True);
        }

        [Test]
        public void DisposeStopsOutputsAndPortsAndPublishesInhibitedSnapshot()
        {
            Ready();
            PrepareAndArm();

            input.Value = Valid(true);
            coordinator.Tick();

            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.Active));

            coordinator.Dispose();

            Assert.That(haptics.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(registration.TornDown, Is.True);
            Assert.That(recorder.TornDown, Is.True);
            Assert.That(coordinator.Current.Permission, Is.False);
            Assert.That(
                coordinator.Current.Activation,
                Is.EqualTo(ActivationState.Inhibited));
            Assert.That(
                coordinator.Current.Clamp,
                Is.EqualTo(ClampState.Disconnected));
            Assert.That(sink.Last.Permission, Is.False);
        }

        private void Ready()
        {
            coordinator.StartSession();
            registration.Value = ValidRegistration();
            input.Value = Valid(false, true);
            coordinator.Tick();
        }

        private void Prepare()
        {
            coordinator.PrepareAttempt(Config(ProcessMode.Fusion));
            coordinator.Submit(TrainingCommandType.SelectWeldingNozzle, clock.Seconds);
            coordinator.Submit(TrainingCommandType.ConnectClamp, clock.Seconds);
            coordinator.Tick();
        }

        private void PrepareAndArm()
        {
            Prepare();
            input.Value = Valid(false, true);
            coordinator.Tick();
            coordinator.Submit(TrainingCommandType.Arm, clock.Seconds);
            coordinator.Tick();
        }

        private TrainingInput Valid(
            bool trigger,
            bool released = false,
            long inputGeneration = 1)
        {
            return new TrainingInput(
                clock.Seconds,
                inputGeneration,
                3,
                true,
                true,
                true,
                trigger,
                released,
                false,
                false);
        }

        private static RegistrationInput ValidRegistration()
        {
            return new RegistrationInput(
                true,
                true,
                7,
                3,
                "fixture");
        }

        private static ProcessConfiguration Config(ProcessMode mode)
        {
            return new ProcessConfiguration(
                "fixture",
                "seam",
                new ProcessProfile(
                    mode.ToString(),
                    1,
                    mode,
                    "hash-" + mode,
                    0.1));
        }

        private static string[] RunSameTimestampScenario()
        {
            Clock localClock = new();
            Input localInput = new();
            Registration localRegistration = new();
            Safety localSafety = new();
            Recorder localRecorder = new();
            Sink localSink = new();
            Haptics localHaptics = new();

            using TrainingCoordinator localCoordinator = new(
                localClock,
                new Ids(),
                localInput,
                localRegistration,
                localSafety,
                localRecorder,
                localSink,
                localHaptics);

            List<ProcessEvent> events = new();
            localCoordinator.EventEmitted += events.Add;

            localCoordinator.StartSession();
            localRegistration.Value = ValidRegistration();
            localInput.Value = new TrainingInput(
                localClock.Seconds,
                1,
                3,
                true,
                true,
                true,
                false,
                true,
                false,
                false);
            localCoordinator.Tick();

            localCoordinator.PrepareAttempt(Config(ProcessMode.Fusion));
            localCoordinator.Submit(TrainingCommandType.SelectWeldingNozzle, localClock.Seconds);
            localCoordinator.Submit(TrainingCommandType.ConnectClamp, localClock.Seconds);
            localCoordinator.Tick();

            localCoordinator.Submit(TrainingCommandType.Arm, localClock.Seconds);
            localCoordinator.Submit(TrainingCommandType.EmergencyStop, localClock.Seconds);
            localCoordinator.Tick();

            return events
                .Select(e => $"{e.Sequence}|{e.Type}|{e.Payload}|{e.InputGeneration}|{e.RegistrationGeneration}")
                .ToArray();
        }

        private sealed class Clock : IMonotonicClock
        {
            public double Seconds { get; set; } = 1;
        }

        private sealed class Ids : IIdSource
        {
            private int next;
            public string NewId() => (++next).ToString();
        }

        private sealed class Input : ITrainingInputPort
        {
            public TrainingInput Value;
            public TrainingInput Capture(double now) => Value;
        }

        private sealed class Registration : IRegistrationPort
        {
            public RegistrationInput Value;
            public bool TornDown;

            public RegistrationInput Capture(double now) => Value;
            public void Teardown() => TornDown = true;
        }

        private sealed class Safety : ISafetyPort
        {
            public SafetyInput Value =
                new SafetyInput(true, true, false, BlockReason.None);

            public SafetyInput Evaluate(
                TrainingInput trainingInput,
                RegistrationInput registrationInput,
                AttemptConfiguration attempt)
            {
                return Value;
            }
        }

        private sealed class Sink : ISnapshotSink
        {
            public ProcessSnapshot Last;
            public void Publish(ProcessSnapshot snapshot) => Last = snapshot;
        }

        private sealed class Haptics : IHapticStop
        {
            public int Count;
            public void StopImmediately() => Count++;
        }

        private sealed class Recorder : IRecorderPort
        {
            public bool Available { get; set; } = true;
            public bool FinishOk = true;
            public bool TornDown;
            public readonly List<string> Finished = new();
            public readonly List<bool> FinishedInterrupted = new();
            public readonly List<ProcessEvent> Events = new();

            public void Append(ProcessEvent processEvent)
            {
                Events.Add(processEvent);
            }

            public void Begin(AttemptConfiguration attempt)
            {
            }

            public bool Finish(
                string attemptId,
                bool interrupted,
                out string error)
            {
                Finished.Add(attemptId);
                FinishedInterrupted.Add(interrupted);
                error = FinishOk ? null : "disk";
                return FinishOk;
            }

            public void Teardown()
            {
                TornDown = true;
            }
        }
    }
}
