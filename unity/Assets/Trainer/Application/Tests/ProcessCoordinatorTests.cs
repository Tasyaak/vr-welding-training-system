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
        public void MenuEdgeSuspendsRunningAttempt()
        {
            PrepareRunning();
            var invalid = TrackedPoseSnapshot.Invalid;
            _input.Snapshot = new InputSnapshot(true, invalid, invalid, invalid, invalid, 0, false,
                InputCommandEdges.MenuToggle, InputContext.Menu, 2, _clock.Seconds);
            _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle, Is.EqualTo(SessionLifecycle.Suspended));
            Assert.That(_coordinator.Current.Activation, Is.EqualTo(ActivationState.Disarmed));
        }

        [Test]
        public void DisconnectingClampWhileRunningSuspendsImmediately()
        {
            PrepareRunning(); SubmitNow(ProcessCommandType.DisconnectClamp); _coordinator.Tick();
            Assert.That(_coordinator.Current.Lifecycle,Is.EqualTo(SessionLifecycle.Suspended));
            Assert.That(_coordinator.Current.Clamp,Is.EqualTo(ClampState.Disconnected));
            Assert.That(_coordinator.Current.Activation,Is.EqualTo(ActivationState.Inhibited));
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

        [Test]
        public void CalibrationOriginChangeInvalidatesAndDestroysAnchor()
        {
            var anchor = new FakeAnchor();
            var calibration = new FixtureCalibrationCoordinator(anchor);
            calibration.Begin(TestContent.Create(), 10, 1);
            calibration.Tick(new InputSnapshot(false, false, false, false, false, 11, 2), 2);
            Assert.That(calibration.State, Is.EqualTo(RegistrationWorkflowState.Lost));
            Assert.That(anchor.DestroyCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(calibration.Capture(2).Valid, Is.False);
        }

        [Test]
        public void UnifiedSafetyRequiresFiniteContactAndKnownSafeRisk()
        {
            ContentSnapshot content=TestContent.Create(); ProcessProfile profile=content.Entry.Profiles.Single(x=>x.Settings.Mode==ProcessMode.Fusion);
            var attempt=new AttemptConfiguration("a","seam-a",ProcessMode.Fusion,profile,content,null);
            var validPose=new TrackedPoseSnapshot(new RigidPose(new Vector3d(0,.001,0),Quaterniond.Identity),true,true,true);
            var input=new InputSnapshot(true,validPose,validPose,validPose,validPose,0,false,InputCommandEdges.None,InputContext.Training,1,1);
            var registration=new RegistrationSnapshot(true,true,1,RegistrationWorkflowState.Registered,RigidPose.Identity,RigidPose.Identity,null);
            var safety=new UnifiedActivationSafety(new SafeRisk(),new NoPrerequisites());
            Assert.That(safety.Evaluate(new EvaluationRequest(1,input,registration,attempt)).Allowed,Is.True);
            var outside=new TrackedPoseSnapshot(new RigidPose(new Vector3d(2,.001,0),Quaterniond.Identity),true,true,true);
            input=new InputSnapshot(true,outside,outside,outside,outside,0,false,InputCommandEdges.None,InputContext.Training,1,2);
            SafetyDecision blocked=safety.Evaluate(new EvaluationRequest(2,input,registration,attempt));
            Assert.That(blocked.Allowed,Is.False);
            Assert.That(blocked.Reasons.HasFlag(InhibitReason.OutsideFiniteSurface),Is.True);
        }

        [Test]
        public void UnknownReflectionIsFailClosed()
        {
            ContentSnapshot content=TestContent.Create();ProcessProfile profile=content.Entry.Profiles.First();
            var attempt=new AttemptConfiguration("a","seam-a",profile.Settings.Mode,profile,content,null);
            var pose=new TrackedPoseSnapshot(new RigidPose(new Vector3d(0,.001,0),Quaterniond.Identity),true,true,true);
            var input=new InputSnapshot(true,pose,pose,pose,pose,0,false,InputCommandEdges.None,InputContext.Training,1,1);
            var registration=new RegistrationSnapshot(true,true,1,RegistrationWorkflowState.Registered,RigidPose.Identity,RigidPose.Identity,null);
            SafetyDecision decision=new UnifiedActivationSafety(null,new NoPrerequisites()).Evaluate(new EvaluationRequest(1,input,registration,attempt));
            Assert.That(decision.Reasons.HasFlag(InhibitReason.ReflectionUnknown),Is.True);
            Assert.That(decision.Allowed,Is.False);
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
        private sealed class FakeAnchor : ISessionAnchorPort
        {
            public int DestroyCount; public bool IsLocalized { get; set; }
            public void BeginCreate(RigidPose pose, long generation, Action<long, AnchorCreationResult> completed) { }
            public void DestroyAnchor() => DestroyCount++;
        }
        private sealed class SafeRisk : IProspectiveRiskPort { public RiskDisposition Evaluate(EvaluationRequest r,ContactEvidence c)=>RiskDisposition.Safe; }
        private sealed class NoPrerequisites : IProcessPrerequisitePort { public InhibitReason Evaluate(EvaluationRequest r,ContactEvidence c)=>InhibitReason.None; }

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

    public sealed class InputContextGateTests
    {
        [Test]
        public void EmergencyStopEdgeSurvivesUntilConsumed()
        {
            var gate = new InputContextGate(.55f, .45f);
            gate.Queue(InputCommandEdges.EmergencyStop);
            Assert.That(gate.ConsumeEdges(), Is.EqualTo(InputCommandEdges.EmergencyStop));
            Assert.That(gate.ConsumeEdges(), Is.EqualTo(InputCommandEdges.None));
        }

        [Test]
        public void ContextChangeRequiresReleaseBeforeTriggerCanRestart()
        {
            var gate = new InputContextGate(.55f, .45f);
            gate.SetContext(InputContext.Training); gate.AdvanceTrigger(1f);
            Assert.That(gate.ProcessPressed, Is.False);
            gate.AdvanceTrigger(0f); gate.AdvanceTrigger(1f);
            Assert.That(gate.ProcessPressed, Is.True);
            gate.SetContext(InputContext.Menu); gate.SetContext(InputContext.Training);
            gate.AdvanceTrigger(1f);
            Assert.That(gate.ProcessPressed, Is.False);
        }

        [Test]
        public void TriggerUsesHysteresis()
        {
            var gate = new InputContextGate(.6f, .4f);
            gate.SetContext(InputContext.Training); gate.AdvanceTrigger(0f); gate.AdvanceTrigger(.61f);
            Assert.That(gate.ProcessPressed, Is.True);
            gate.AdvanceTrigger(.5f); Assert.That(gate.ProcessPressed, Is.True);
            gate.AdvanceTrigger(.39f); Assert.That(gate.ProcessPressed, Is.False);
        }
    }

    public sealed class RigidPoseMathTests
    {
        [Test]
        public void ComposeAppliesControllerRotationBeforeTranslation()
        {
            double half = Math.Sqrt(.5);
            var worldFromController = new RigidPose(new Vector3d(1, 2, 3), new Quaterniond(0, half, 0, half));
            var controllerFromTip = new RigidPose(new Vector3d(0, 0, 1), Quaterniond.Identity);
            RigidPose result = RigidPoseMath.Compose(worldFromController, controllerFromTip);
            Assert.That(result.PositionMetres.X, Is.EqualTo(2).Within(1e-9));
            Assert.That(result.PositionMetres.Y, Is.EqualTo(2).Within(1e-9));
            Assert.That(result.PositionMetres.Z, Is.EqualTo(3).Within(1e-9));
        }
    }

    public sealed class FourPointRigidSolverTests
    {
        [Test]
        public void PlanarRectangleRecoversKnownRigidTransform()
        {
            FixtureDefinition fixture=Fixture(Rectangle());
            double half=Math.Sqrt(.5);var expected=new RigidPose(new Vector3d(.3,1.1,-.2),new Quaterniond(0,half,0,half));
            var captures=fixture.ReferencePoints.Select(p=>Capture(p.Id,
                RigidPoseMath.Rotate(expected.Rotation,p.FixturePositionMetres)+expected.PositionMetres)).ToArray();
            RegistrationCandidate result=FourPointRigidSolver.Solve(fixture,captures);
            Assert.That(result.Quality.Accepted,Is.True);
            Assert.That(result.Quality.ResidualMetres,Has.All.LessThan(1e-7));
            Assert.That(Vector3d.Distance(result.WorldFromFixture.PositionMetres,expected.PositionMetres),Is.LessThan(1e-7));
        }

        [Test]
        public void CollinearLayoutIsRejected()
        {
            Vector3d[] points={new(0,0,0),new(.1,0,0),new(.2,0,0),new(.3,0,0)};
            FixtureDefinition fixture=Fixture(points);
            RegistrationCandidate result=FourPointRigidSolver.Solve(fixture,points.Select((p,i)=>Capture("p"+(i+1),p)).ToArray());
            Assert.That(result.Quality.Reason,Is.EqualTo(RegistrationValidityReason.DegenerateLayout));
        }

        [Test]
        public void ScaleMismatchIsRejected()
        {
            FixtureDefinition fixture=Fixture(Rectangle());Vector3d[] measured=Rectangle().Select(x=>x*1.2).ToArray();
            RegistrationCandidate result=FourPointRigidSolver.Solve(fixture,measured.Select((p,i)=>Capture("p"+(i+1),p)).ToArray());
            Assert.That(result.Quality.Reason,Is.EqualTo(RegistrationValidityReason.DimensionMismatch));
        }

        [Test]
        public void CaptureRejectsOneLargeOutlierButKeepsRequiredSamples()
        {
            Vector3d[] samples={new(0,0,0),new(.0001,0,0),new(0,.0001,0),new(1,1,1)};
            CalibrationPointCapture capture=CalibrationCaptureMath.Build("p1",samples,3,.003,4);
            Assert.That(capture.RetainedCount,Is.EqualTo(3));
            Assert.That(capture.SpreadMetres,Is.LessThan(.003));
        }

        private static Vector3d[] Rectangle()=>new[]{new Vector3d(-.2,0,-.1),new Vector3d(.2,0,-.1),new Vector3d(.2,0,.1),new Vector3d(-.2,0,.1)};
        private static CalibrationPointCapture Capture(string id,Vector3d point)=>new(id,point,5,5,.0001,1);
        private static FixtureDefinition Fixture(Vector3d[] points)=>new("fixture",1,
            points.Select((p,i)=>new ReferencePointDefinition("p"+(i+1),p)),new Vector3d(.4,.02,.2),RigidPose.Identity,
            new CalibrationPolicy(3,.003,.005,.05,.002));
    }

    public sealed class FiniteSurfaceContactTests
    {
        private static readonly SurfacePatch Patch=new("plate",new[]{new Vector3d(-1,0,-1),new Vector3d(1,0,-1),new Vector3d(1,0,1),new Vector3d(-1,0,1)},new Vector3d(0,1,0),1);
        private static WorkpieceDefinition Workpiece()=>new("w",1,new[]{Patch},Array.Empty<DirectedSeam>(),Array.Empty<TargetRegion>());

        [Test]
        public void InsideFrontFaceEntersAndUsesExitHysteresis()
        {
            var evaluator=new FiniteSurfaceContactEvaluator();
            Assert.That(evaluator.Evaluate(Workpiece(),new Vector3d(0,.001,0),true,.002,.004).State,Is.EqualTo(ContactState.Contact));
            Assert.That(evaluator.Evaluate(Workpiece(),new Vector3d(0,.003,0),true,.002,.004).State,Is.EqualTo(ContactState.Contact));
            Assert.That(evaluator.Evaluate(Workpiece(),new Vector3d(0,.005,0),true,.002,.004).State,Is.EqualTo(ContactState.Separated));
        }

        [Test]
        public void InfinitePlaneNearnessDoesNotPermitOutsidePatch()
        {
            ContactEvidence evidence=new FiniteSurfaceContactEvaluator().Evaluate(Workpiece(),new Vector3d(2,.001,0),true,.002,.004);
            Assert.That(evidence.State,Is.Not.EqualTo(ContactState.Contact));
            Assert.That(evidence.InvalidReason,Is.EqualTo(ContactInvalidReason.OutsideBounds));
        }

        [Test]
        public void BacksideApproachIsRejected()
        {
            ContactEvidence evidence=new FiniteSurfaceContactEvaluator().Evaluate(Workpiece(),new Vector3d(0,-.001,0),true,.002,.004);
            Assert.That(evidence.State,Is.Not.EqualTo(ContactState.Contact));
            Assert.That(evidence.InvalidReason,Is.EqualTo(ContactInvalidReason.WrongApproach));
        }

        [Test]
        public void RayMustHitFinitePatch()
        {
            Assert.That(FiniteSurfaceContactEvaluator.TryRayImpact(Patch,new Vector3d(0,1,0),new Vector3d(0,-1,0),out _),Is.True);
            Assert.That(FiniteSurfaceContactEvaluator.TryRayImpact(Patch,new Vector3d(2,1,0),new Vector3d(0,-1,0),out _),Is.False);
        }

        [TestCase(ProcessMode.Fusion,"welding-nozzle",true)]
        [TestCase(ProcessMode.Wobble,"welding-nozzle",true)]
        [TestCase(ProcessMode.Pulsed,"welding-nozzle",true)]
        [TestCase(ProcessMode.PreWeldCleaning,"cleaning-nozzle",true)]
        [TestCase(ProcessMode.PostWeldCleaning,"cleaning-nozzle",true)]
        [TestCase(ProcessMode.Fusion,"cleaning-nozzle",false)]
        [TestCase(ProcessMode.PreWeldCleaning,"welding-nozzle",false)]
        public void NozzleCompatibilityTableIsExplicit(ProcessMode mode,string nozzle,bool expected)
            =>Assert.That(NozzleCompatibility.IsCompatible(mode,nozzle),Is.EqualTo(expected));
    }
}
