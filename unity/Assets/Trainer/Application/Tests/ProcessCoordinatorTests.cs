using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class ProcessCoordinatorTests
    {
        private FakeClock _clock;
        private FakeInput _input;
        private FakeRegistration _registration;
        private FakeSafety _safety;
        private FakeRecorder _recorder;
        private FakeSink _sink;
        private FakeHaptics _haptics;
        private ProcessCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock(); _input = new FakeInput(); _registration = new FakeRegistration();
            _safety = new FakeSafety(); _recorder = new FakeRecorder(); _sink = new FakeSink(); _haptics = new FakeHaptics();
            _coordinator = new ProcessCoordinator(_clock, new SequentialIds(), _input, _registration,
                _safety, _recorder, _sink, _haptics);
        }

        [TearDown] public void TearDown() => _coordinator.Dispose();

        [Test]
        public void RegistrationMustBeValidBeforeSelecting()
        {
            _coordinator.StartSession(); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Registering));
            _registration.Snapshot = new RegistrationSnapshot(true, true, 7); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Selecting));
            Assert.That(_coordinator.Current.RegistrationGeneration, Is.EqualTo(7));
        }

        [Test]
        public void MissingSafetyProviderNeverActivates()
        {
            PrepareReady(); _safety.Decision = SafetyDecision.Missing;
            SubmitNow(ProcessCommandType.SelectNozzle, "welding-nozzle");
            SubmitNow(ProcessCommandType.ConnectClamp); SubmitNow(ProcessCommandType.Arm); _coordinator.Tick();
            _input.Snapshot = new InputSnapshot(true, true, true, true, false, 2, _clock.Seconds);
            _coordinator.Tick();
            Assert.That(_coordinator.Current.Activation, Is.EqualTo(ActivationState.Inhibited));
            Assert.That(_coordinator.Current.InhibitReasons.HasFlag(InhibitReason.SafetyProviderUnavailable), Is.True);
        }

        [Test]
        public void EmergencyStopDominatesArmAtSameTimestamp()
        {
            PrepareReady(); SubmitNow(ProcessCommandType.SelectNozzle, "welding-nozzle"); SubmitNow(ProcessCommandType.ConnectClamp);
            SubmitNow(ProcessCommandType.Arm); SubmitNow(ProcessCommandType.EmergencyStop); _coordinator.Tick();
            Assert.That(_coordinator.Current.Activation, Is.EqualTo(ActivationState.EmergencyStopped));
            Assert.That(_haptics.StopCount, Is.GreaterThan(0));
        }

        [Test]
        public void RegistrationGenerationChangeIsCheckedEveryRunningTick()
        {
            PrepareRunning();
            _registration.Snapshot = new RegistrationSnapshot(true, true, 8); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Suspended));
            Assert.That(_coordinator.Current.InhibitReasons.HasFlag(InhibitReason.RegistrationChanged), Is.True);
        }

        [Test]
        public void ProcessChangeWhileRunningIsRejected()
        {
            PrepareRunning();
            Assert.Throws<InvalidOperationException>(() => _coordinator.PrepareAttempt(
                TestContent.Create(), "seam-a", ProcessMode.Wobble));
        }

        [Test]
        public void RetryCreatesDistinctAttemptAndPreservesRecorderHistory()
        {
            PrepareRunning(); string first = _coordinator.Current.AttemptId;
            SubmitNow(ProcessCommandType.FinishAttempt); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Reviewing));
            _coordinator.PrepareAttempt(TestContent.Create(), "seam-a", ProcessMode.Fusion);
            Assert.That(_coordinator.Current.AttemptId, Is.Not.EqualTo(first));
            Assert.That(_recorder.StartedAttemptIds, Does.Contain(first));
            Assert.That(_recorder.FinishedAttemptIds, Does.Contain(first));
        }

        [Test]
        public void SessionCompletesOnlyFromReview()
        {
            PrepareRunning(); SubmitNow(ProcessCommandType.FinishAttempt); _coordinator.Tick();
            SubmitNow(ProcessCommandType.FinishSession); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Completed));
            Assert.That(_coordinator.Current.Clamp, Is.EqualTo(ClampState.Disconnected));
            Assert.That(_coordinator.Current.AttemptId, Is.Null);
        }

        [Test]
        public void RecorderFailureIsExplicitFault()
        {
            PrepareRunning(); _recorder.FinishSucceeds = false;
            SubmitNow(ProcessCommandType.FinishAttempt); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Faulted));
            Assert.That(_coordinator.Current.Faults.Any(x => x.Code == "recording-save-failed"), Is.True);
        }

        [Test]
        public void TriggerMustBeReleasedBeforeResetAndRearm()
        {
            PrepareRunning(); _input.Snapshot = new InputSnapshot(true, true, true, true, false, 2, _clock.Seconds);
            SubmitNow(ProcessCommandType.EmergencyStop); _coordinator.Tick();
            SubmitNow(ProcessCommandType.ResetEmergencyStop); _coordinator.Tick();
            Assert.That(_coordinator.Current.Activation, Is.EqualTo(ActivationState.EmergencyStopped));
            _input.Snapshot = new InputSnapshot(true, true, true, false, false, 3, _clock.Seconds);
            SubmitNow(ProcessCommandType.ResetEmergencyStop); _coordinator.Tick();
            Assert.That(_coordinator.Current.Activation, Is.EqualTo(ActivationState.Disarmed));
        }

        [Test]
        public void EventSequencesAreStrictlyIncreasing()
        {
            var events = new List<ProcessEvent>(); _coordinator.EventEmitted += events.Add;
            PrepareRunning(); SubmitNow(ProcessCommandType.Pause); _coordinator.Tick();
            Assert.That(events.Select(x => x.Sequence), Is.Ordered.And.Unique);
        }

        [Test]
        public void DisposalStopsHapticsAndTearsDownPorts()
        {
            _coordinator.StartSession(); _coordinator.Dispose();
            Assert.That(_haptics.StopCount, Is.EqualTo(1));
            Assert.That(_registration.TornDown, Is.True);
            Assert.That(_recorder.TornDown, Is.True);
        }

        private void PrepareReady()
        {
            _coordinator.StartSession(); _registration.Snapshot = new RegistrationSnapshot(true, true, 7); _coordinator.Tick();
            _coordinator.PrepareAttempt(TestContent.Create(), "seam-a", ProcessMode.Fusion);
        }

        private void PrepareRunning()
        {
            PrepareReady(); SubmitNow(ProcessCommandType.SelectNozzle, "welding-nozzle");
            SubmitNow(ProcessCommandType.ConnectClamp); SubmitNow(ProcessCommandType.Arm); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Running));
        }

        private void SubmitNow(ProcessCommandType type, string value = null) => _coordinator.Submit(type, _clock.Seconds, value);

        private sealed class FakeClock : IMonotonicClock { public double Seconds { get; set; } = 1; }
        private sealed class SequentialIds : IIdentifierSource { private int _value; public string NewId() => (++_value).ToString(); }
        private sealed class FakeInput : IInputSnapshotSource
        { public InputSnapshot Snapshot = new(true, true, true, false, false, 1, 1); public InputSnapshot Capture(double t) => Snapshot; }
        private sealed class FakeRegistration : IRegistrationStateSource
        { public RegistrationSnapshot Snapshot; public bool TornDown; public RegistrationSnapshot Capture(double t) => Snapshot; public void Teardown() => TornDown = true; }
        private sealed class FakeSafety : IActivationSafetyPort
        { public SafetyDecision Decision = new(true, true, InhibitReason.None); public SafetyDecision Evaluate(EvaluationRequest r) => Decision; }
        private sealed class FakeSink : IProcessSnapshotSink { public ProcessSnapshot Last; public void Publish(ProcessSnapshot s) => Last = s; }
        private sealed class FakeHaptics : IHapticLifecyclePort { public int StopCount; public void StopImmediately() => StopCount++; }
        private sealed class FakeRecorder : IProcessRecorder
        {
            public RecorderHealth Health { get; set; } = RecorderHealth.Ready;
            public bool FinishSucceeds = true, TornDown;
            public readonly List<string> StartedAttemptIds = new(), FinishedAttemptIds = new();
            public void Append(ProcessEvent e) { }
            public void BeginAttempt(AttemptConfiguration c) => StartedAttemptIds.Add(c.AttemptId);
            public bool FinishAttempt(string id, bool interrupted, out string error)
            { FinishedAttemptIds.Add(id); error = FinishSucceeds ? null : "disk-fault"; return FinishSucceeds; }
            public void Teardown() => TornDown = true;
        }

        private static class TestContent
        {
            public static ContentSnapshot Create()
            {
                var points = new[] { new ReferencePointDefinition("a", new(-1,0,-1)), new ReferencePointDefinition("b", new(1,0,-1)), new ReferencePointDefinition("c", new(1,0,1)), new ReferencePointDefinition("d", new(-1,0,1)) };
                var fixture = new FixtureDefinition("f", 1, points, new(2,0.1,2), RigidPose.Identity, new(2,0.001,0.005,0.1,0.1));
                var surface = new SurfacePatch("s", new[] { new Vector3d(-1,0,-1), new Vector3d(1,0,-1), new Vector3d(1,0,1), new Vector3d(-1,0,1) }, new(0,1,0), 1);
                DirectedSeam Seam(string id, double z) => new(id, "s", new[] { new Vector3d(-0.5,0,z), new Vector3d(0.5,0,z) }, new[] { 0d, 1d }, 0.0001);
                var workpiece = new WorkpieceDefinition("w", 1, new[] { surface }, new[] { Seam("seam-a", -0.1), Seam("seam-b", 0.1) }, new[] { new TargetRegion("pre", "s", null, RegionPurpose.PreClean, new[] { new Vector3d(-.5,0,-.2), new Vector3d(.5,0,-.2), new Vector3d(.5,0,0), new Vector3d(-.5,0,0) }), new TargetRegion("post", "s", "seam-a", RegionPurpose.PostClean, new[] { new Vector3d(-.5,0,-.2), new Vector3d(.5,0,-.2), new Vector3d(.5,0,0), new Vector3d(-.5,0,0) }) });
                var tool = new ToolDefinition("t", 1, GripPoseConvention.RightHandTouchPlusGrip, RigidPose.Identity, RigidPose.Identity, new(0,0,-1), new(0,0,1));
                ProcessProfile P(string id, ProcessSettings settings, string nozzle) => new(id, 1, settings, nozzle, .01,.01,new(.01,.2),new(0,.01),new(-1,1),new(-1,1),.002,.004,.1,.2,.05,100);
                var profiles = new[] { P("fusion",new FusionSettings(),"welding-nozzle"), P("wobble",new WobbleSettings(.005,4),"welding-nozzle"), P("pulse",new PulsedSettings(.05,.05),"welding-nozzle"), P("pre",new CleaningSettings(ProcessMode.PreWeldCleaning),"cleaning-nozzle"), P("post",new CleaningSettings(ProcessMode.PostWeldCleaning),"cleaning-nozzle") };
                return ContentSnapshot.Freeze(new ContentCatalogEntry("catalog", fixture, workpiece, tool, profiles));
            }
        }
    }
}
