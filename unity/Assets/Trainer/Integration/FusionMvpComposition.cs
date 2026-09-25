using System;
using System.Linq;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Domain;
using WeldingTrainer.Platform.Meta.Tool;
using WeldingTrainer.Platform.Meta.Tool.Unity;
using WeldingTrainer.Presentation;
using WeldingTrainer.Registration;
using DVec = WeldingTrainer.Domain.Vec3;
using SVec = WeldingTrainer.Content.Spatial.Vec3;
using SPose = WeldingTrainer.Content.Spatial.RigidPose;

namespace WeldingTrainer.Integration
{
    [DefaultExecutionOrder(500)]
    public sealed class FusionMvpComposition : MonoBehaviour, WeldingTrainer.Application.IMonotonicClock, IIdSource, ITrainingInputPort, IRegistrationPort, ISafetyPort, ISnapshotSink, IHapticStop, IRightHapticFeedbackSink
    {
        public QuestRegistrationBridge registration;
        public RightControllerSource right;
        public FusionTrainingView view;
        [Range(0, 100)]
        public float assistance = 100;
        public double Seconds => Time.realtimeSinceStartupAsDouble;

        public string NewId() => Guid.NewGuid().ToString("N");
        public ProcessSnapshot State => coordinator?.Current;
        public FusionResult Result => result;
        public FusionAttemptSummary Summary { get; private set; }

        TrainingCoordinator coordinator;
        FusionLocalRecording recorder;
        SemanticFeedbackEngine feedback = new(new AssistanceProfile(.25, 1, 3));
        FeedbackDeliveryCoordinator delivery;
        AssemblyBindingSnapshot binding;
        SeamSnapshot authored;
        DirectedSpline seam;
        FinitePatch[] patches;
        readonly FiniteContactEvaluator contactEvaluator = new();
        WeldPathEvaluator evaluator;
        FusionProcessKernel fusion;
        FusionAttemptStatistics statistics;
        FusionProfile profile;
        AttemptConfiguration attempt;
        PathMetrics path;
        FusionResult result;
        ToolHeadSnapshot tool;
        TrainingInput input;
        RegistrationInput registrationInput;
        RegistrationSnapshot spatial;
        SPose worldFromWorkpiece;
        DVec tip, incident, head, normal;
        bool spatialValid, poseValid, originBound, lifecycleInvalid, finishNextTick, resultShown, restartRegistration;
        long boundToolOrigin, boundRegistrationOrigin, boundRegistrationGeneration;
        double hapticUntil, preparedAt;
        string error;
#if UNITY_EDITOR
        [HideInInspector] public bool EditorRehearsal;
        [NonSerialized] public bool EditorTrigger;
        [NonSerialized] public double EditorArc;
        public void EditorSubmit() => SubmitAction();
        public void EditorFinish() => coordinator.Submit(TrainingCommandType.FinishAttempt,Seconds);
#endif
        void Start()
        {
            try
            {
                view.Initialize();
                view.Head = registration.rig.centerEyeAnchor;
                recorder = new FusionLocalRecording(UnityEngine.Application.persistentDataPath);
                recorder.Summarize = FinalizeSummary;
                coordinator = new TrainingCoordinator(this, this, this, this, this, recorder, this, this);
                delivery = new FeedbackDeliveryCoordinator(view, view, this);
                coordinator.StartSession();
#if UNITY_EDITOR
                if(EditorRehearsal)
                {
                    right.enabled=false; registration.enabled=false; view.Rehearsal=true;
                    binding=registration.catalog.Freeze().Bindings.First();
                    worldFromWorkpiece=ToolMath.Pose("World","Workpiece",new SVec(0,1, -.65),new WeldingTrainer.Content.Spatial.Quat(0,0,0,1));
                    SetupGeometry();
                }
                else
#endif
                registration.BeginRegistration();
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogException(e, this);
            }
        }

        void Update()
        {
            if (Seconds >= hapticUntil)
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
            if (coordinator == null || error != null)
            {
                view.Status = "Setup unavailable: " + error;
                return;
            }

            try
            {
                Step();
            }
            catch (Exception e)
            {
                error = e.Message;
                StopImmediately();
                fusion?.Stop(Seconds);
                view.Clear();
                coordinator.Submit(TrainingCommandType.Abort, Seconds);
                coordinator.Tick();
                Debug.LogException(e, this);
            }
        }

        void Step()
        {
            double now = Seconds;
            CaptureFrame(now);
            // Registration.Refresh() timestamps its snapshot while reading. Use a
            // subsequent monotonic time for the rest of this frame; otherwise a
            // newly Registered snapshot appears to be captured in the future.
            now = Seconds;
            if (finishNextTick)
            {
                finishNextTick = false;
                coordinator.Submit(TrainingCommandType.FinishAttempt, now);
            }

            coordinator.Tick();
            if (restartRegistration && State.Session == SessionState.Completed)
            {
                restartRegistration=false;originBound=false;binding=null;attempt=null;statistics=null;
                coordinator.StartSession();registration.BeginRegistration();right.SetContext(ToolInputContext.Registration);
            }
            if (State.Session == SessionState.Selecting)
                Prepare();
            bool attemptOpen = attempt != null && (State.Session == SessionState.Ready || State.Session == SessionState.Running || State.Session == SessionState.Suspended);
            if (attemptOpen)
            {
                path = evaluator.Evaluate(new PathSample(State.SessionId, attempt.AttemptId, binding.WorkpieceGeometry.ContentHash, now, tip, incident, normal, input.Available, poseValid && spatialValid, registrationInput.RegistrationGeneration, input.OriginGeneration));
                var activation = new ActivationDecision(State.Activation, State.Reasons, State.Requested);
                result = fusion.Update(new FusionInput(now, path, activation, registrationInput.RegistrationGeneration, attempt.RegistrationGeneration));
                statistics.Add(now, path, result, input.TriggerPressed, profile.MaximumIntervalSeconds);
                var q = result.QualityFlags;
                var semantic = feedback.Evaluate(new FeedbackInput(input.InputGeneration, registrationInput.RegistrationGeneration, now, result.Coverage.AttemptedFraction, poseValid, spatialValid, State.Reasons, q.HasFlag(FusionQualityFlags.Position), path.SpeedClass == SpeedClass.TooSlow, path.SpeedClass == SpeedClass.SpeedCorrect, path.SpeedClass == SpeedClass.TooFast, q.HasFlag(FusionQualityFlags.TravelAngle), q.HasFlag(FusionQualityFlags.WorkAngle), q.HasFlag(FusionQualityFlags.Reverse), false, result.Complete), assistance);
                delivery.Update(semantic, now, State.Session == SessionState.Running, poseValid && spatialValid);
                recorder.Sample(new FusionLocalRecording.Record { kind = "sample", time = now, sessionId = State.SessionId, attemptId = attempt.AttemptId, contentHash = binding.WorkpieceGeometry.ContentHash, binding = binding.Id, profile = profile.Hash, seam = seam.Id, calibration = tool?.Calibration?.Revision, sequence = tool?.Sequence ?? 0, registrationGeneration = registrationInput.RegistrationGeneration, inputGeneration = input.InputGeneration, originGeneration = input.OriginGeneration, valid = path.Valid, speedValid = path.SpeedValid, travelValid = path.TravelAngleValid, workValid = path.WorkAngleValid, requested = input.TriggerPressed, active = result.OutputOn, acceptable = result.Acceptable, triggerAnalog = tool?.TriggerAnalog ?? 0, assistance = assistance, arcMetres = path.Projection.ArcLengthMetres, speedMps = path.FilteredSpeedMps, positionMetres = path.TotalErrorMetres, travelRadians = path.TravelAngleRadians, workRadians = path.WorkAngleRadians, tipWorkpiece = Raw(tip), headWorkpiece = Raw(head), incidentWorkpiece = Raw(incident), state = State.Activation.ToString(), reason = State.Reasons.ToString(), feedback = semantic.PrimaryCue.ToString(), deltaValid = result.Delta.Valid, startArcMetres = result.Delta.StartArcMetres, endArcMetres = result.Delta.EndArcMetres, activationEpoch = result.Delta.ActivationEpoch });
                if (result.Complete || now - preparedAt > 600)
                    finishNextTick = true;
            }

            if (spatialValid)
                UnitySpatialPose.Write(view.Workpiece, worldFromWorkpiece);
            view.Workpiece.gameObject.SetActive(spatialValid || spatial?.State == RegistrationState.Preview);
            if (!spatialValid && spatial?.WorldFromWorkpiece != null)
                UnitySpatialPose.Write(view.Workpiece, spatial.WorldFromWorkpiece.Value);
            view.Render(path, result, spatialValid && attemptOpen);
            if ((State.Session == SessionState.Reviewing || State.Session == SessionState.Aborted || State.Session == SessionState.Faulted) && !resultShown && statistics != null)
            {
                delivery.AttemptEnded();
                Summary ??= FinalizeSummary(true);
                view.ShowResults(Summary, recorder.SavedPath != null && recorder.Available);
                resultShown = true;
            }

            view.Status = StatusText();
            view.ActionHint = Hint();
        }

        void CaptureFrame(double now)
        {
#if UNITY_EDITOR
            if(EditorRehearsal)
            {
                spatialValid=poseValid=true;
                tip=seam.PositionAtArc(EditorArc)+normal*.001; incident=normal*-1;head=new DVec(0,.5,.6);
                input=new TrainingInput(now,1,1,true,true,true,EditorTrigger,!EditorTrigger,false,false);
                registrationInput=new RegistrationInput(true,true,1,1,binding.FixtureId);
                view.SetTool(view.Workpiece.TransformPoint(FusionBeadView.ToUnity(tip)),view.Workpiece.TransformPoint(FusionBeadView.ToUnity(tip-incident*.07)),true);
                return;
            }
#endif
            tool = right.Capture();
            spatial = registration.Refresh();
            double capturedAt = Seconds;
            spatialValid = spatial != null && spatial.IsUsableAt(capturedAt, spatial.OriginGeneration) && spatial.WorldFromWorkpiece.HasValue;
            if (spatial?.Binding != null && binding == null)
            {
                binding = spatial.Binding;
                SetupGeometry();
            }

            // A's two providers have independent generation counters. Bind their pair
            // once at registration; never equate counters or silently rebind an attempt.
            if (spatialValid && !originBound)
            {
                boundToolOrigin = tool.OriginGeneration;
                boundRegistrationOrigin = spatial.OriginGeneration;
                boundRegistrationGeneration = spatial.Generation;
                originBound = true;
            }

            if (originBound && (boundToolOrigin != tool.OriginGeneration || boundRegistrationOrigin != spatial?.OriginGeneration || boundRegistrationGeneration != spatial?.Generation))
            {
                spatialValid = false;
                registration.InvalidateTracking();
            }

            if (spatialValid)
                worldFromWorkpiece = spatial.WorldFromWorkpiece.Value;
            poseValid = tool.IsUsableAt(capturedAt, tool.OriginGeneration) && !lifecycleInvalid;
            if (poseValid && spatialValid)
            {
                var inverse = worldFromWorkpiece.Inverse();
                tip = Domain(inverse.TransformPoint(tool.Tip.Pose.Value.Position));
                incident = Domain(inverse.TransformDirection(tool.Tool.Pose.Value.TransformDirection(new SVec(0, 0, -1))));
                head = Domain(inverse.TransformPoint(tool.Head.Pose.Value.Position));
            }

            registrationInput = new RegistrationInput(spatial != null, spatialValid, spatial?.Generation ?? 0, originBound ? boundToolOrigin : -1, spatial?.Binding?.FixtureId);
            bool estop = tool.Commands.Any(c => c.Type == ToolCommandType.EStopPressed);
            bool system = tool.Commands.Any(c => c.Type == ToolCommandType.SystemInvalid) || lifecycleInvalid;
            input = new TrainingInput(tool.CapturedSeconds, tool.InputContextGeneration, tool.OriginGeneration, tool.DeviceValid, tool.Head.Pose.HasValue, tool.Tip.Pose.HasValue, tool.TriggerHeld, tool.TriggerAnalog.HasValue && tool.TriggerAnalog.Value <= tool.TriggerReleaseThreshold && !tool.TriggerHeld, estop, system);
            if (tool.Commands.Any(c => c.Type == ToolCommandType.TriggerReleased || c.Type == ToolCommandType.SystemInvalid || c.Type == ToolCommandType.EStopPressed)) fusion?.Stop(now);
            foreach (var command in tool.Commands)
            {
                if (command.Type == ToolCommandType.Submit && !estop)
                    SubmitAction();
                if (command.Type == ToolCommandType.MenuRequested)
                    coordinator.Submit(TrainingCommandType.FinishAttempt, now);
            }

            view.SetTool(tool.Tip.Pose.HasValue ? UnitySpatialPose.ToUnity(tool.Tip.Pose.Value.Position) : Vector3.zero, tool.Tool.Pose.HasValue ? UnitySpatialPose.ToUnity(tool.Tool.Pose.Value.Position) : Vector3.zero, poseValid);
        }

        void SetupGeometry()
        {
            authored = binding.WorkpieceGeometry.Seams.Single(s => s.Id == "PART-001-T-JOINT");
            seam = new DirectedSpline(authored.Id, authored.Points.Select(Domain));
            var primary = binding.WorkpieceGeometry.Surfaces.Single(s => s.Id == authored.SurfaceIds[0]);
            var adjacent = binding.WorkpieceGeometry.Surfaces.Single(s => s.Id == authored.AdjacentSurfaceIds[0]);
            normal = (Domain(primary.Normal) + Domain(adjacent.Normal)).Normalized;
            patches = binding.WorkpieceGeometry.Surfaces.Where(s => s.SourceFace == primary.SourceFace).Select(s => new FinitePatch(s.Id, new[] { Domain(s.A), Domain(s.B), Domain(s.C) }, Domain(s.Normal), 1)).ToArray();
            view.Configure(seam, normal);
        }

        void Prepare()
        {
            profile = new FusionProfile("fusion-mvp", 1, "fusion-mvp-v1:4mm:15-35mms:20deg:100ms", seam.Id, .004, .004, .015, .035, 20 * Math.PI / 180, 20 * Math.PI / 180, .003, .002, .1, .01, .002, .006);
            attempt = coordinator.PrepareAttempt(new ProcessConfiguration(binding.FixtureId, seam.Id, new ProcessProfile(profile.Id, profile.Version, ProcessMode.Fusion, profile.Hash, .1)));
            evaluator = new WeldPathEvaluator(seam, new PathEvaluationPolicy(.015, .035, .12, .1, .01, .00005, .000001, .03), new PathEvaluationContext(State.SessionId, attempt.AttemptId, binding.WorkpieceGeometry.ContentHash, attempt.RegistrationGeneration, input.OriginGeneration));
            fusion = new FusionProcessKernel(attempt.AttemptId, seam, profile);
            statistics = new FusionAttemptStatistics();
            preparedAt = Seconds;
            resultShown = false;
            Summary = null;
            path = null;
            result = null;
            feedback.Reset();
            view.Configure(seam, normal);
#if UNITY_EDITOR
            if(!EditorRehearsal)
#endif
            right.SetContext(ToolInputContext.Training);
            recorder.Sample(new FusionLocalRecording.Record { kind = "registration", time = Seconds, binding = binding.Id, contentHash = binding.ContentHash, registrationGeneration = attempt.RegistrationGeneration, originGeneration = input.OriginGeneration, reason = spatial == null ? "EDITOR SYNTHETIC REGISTRATION" : $"{spatial.State}; anchor={spatial.Anchor}; confirmed={spatial.AssemblyConfirmed}; observations={spatial.ObservationCount}; scatterMetres={spatial.TranslationScatterMetres}; runtime={registration.RuntimeTuple}" });
        }

        void SubmitAction()
        {
            if (coordinator == null)
                return;
            if (State.Session == SessionState.Registering)
            {
                if (spatial?.State == RegistrationState.Preview)
                    registration.ConfirmAssembly(true, true, true);
                else
                {
                    originBound = false;
                    registration.BeginRegistration();
                }

                return;
            }

            if (State.Session == SessionState.Reviewing)
            {
                if (!spatialValid)
                {
                    restartRegistration=true;
                    coordinator.Submit(TrainingCommandType.FinishSession,Seconds);
                    return;
                }
                Prepare();
                return;
            }

            if (State.Session == SessionState.Suspended)
            {
                coordinator.Submit((State.Reasons & BlockReason.EmergencyStop) != 0 ? TrainingCommandType.ResetEmergencyStop : TrainingCommandType.Resume, Seconds);
                return;
            }

            if (State.Session == SessionState.Ready)
            {
                if (State.Nozzle != NozzleState.Welding)
                    coordinator.Submit(TrainingCommandType.SelectWeldingNozzle, Seconds);
                else if (State.Clamp != ClampState.Connected)
                    coordinator.Submit(TrainingCommandType.ConnectClamp, Seconds);
                else
                    coordinator.Submit(TrainingCommandType.Arm, Seconds);
            }
        }

        public SafetyInput Evaluate(TrainingInput captured, RegistrationInput registered, AttemptConfiguration frozen)
        {
            ContactEvidence contact = default;
            if (poseValid && spatialValid && patches != null)
            {
                // Triangles on the selected authored primary face form one finite support.
                // Shared triangle edges are not competing physical surfaces.
                foreach (var patch in patches)
                {
                    var candidate = contactEvaluator.Evaluate(new[] { patch }, tip, true, new ContactPolicy(.006, .006));
                    if (candidate.State == ContactState.Contact)
                    {
                        contact = candidate;
                        break;
                    }
                }
            }

            RiskState risk = RiskState.Unknown;
            if (contact.State == ContactState.Contact && poseValid)
            {
                double dot = DVec.Dot(incident, contact.Normal);
                if (dot < -.05)
                {
                    var reflected = (incident - contact.Normal * (2 * dot)).Normalized;
                    var toHead = head - contact.ClosestPoint;
                    double along = DVec.Dot(toHead, reflected);
                    double lateral = (toHead - reflected * along).Length;
                    risk = along > 0 && lateral < .12 + along * Math.Tan(15 * Math.PI / 180) ? RiskState.High : RiskState.Low;
                }
            }

            return new SafetyInput(true, false, spatialValid, registered.FixtureId == frozen.Process.FixtureId, false, contact, risk, BlockReason.None);
        }

        FusionAttemptSummary FinalizeSummary(bool interrupted)
        {
            var s = statistics.Finish(interrupted);
            s.sessionId = State.SessionId;
            s.attemptId = attempt.AttemptId;
            s.seamId = seam.Id;
            s.contentHash = binding.WorkpieceGeometry.ContentHash;
            s.profileHash = profile.Hash;
            Summary = s;
            return s;
        }

        string StatusText()
        {
            if (error != null)
                return error;
            if (State.Session == SessionState.Registering)
                return spatial == null ? "Starting QR registration" : $"QR {spatial.State}: {spatial.Reason}";
            if (State.Reasons != BlockReason.None)
                return $"{State.Session}  /  {State.PrimaryReason}";
            if (result?.BlockReasons == FusionBlockReason.StartGate)
                return "Move to green START before welding";
            return $"{State.Session}  /  {State.Activation}";
        }

        string Hint()
        {
            if (State.Session == SessionState.Registering)
                return spatial?.State == RegistrationState.Preview ? "A: confirm correct, secured part and overlay" : "Point headset at fixture QR  /  A: retry";
            if (State.Session == SessionState.Reviewing)
                return spatialValid ? "A: new attempt  /  local JSON results saved" : "A: register again before the next attempt";
            if (State.Session == SessionState.Suspended)
                return "Release trigger  /  A: acknowledge, then resume";
            if (State.Session == SessionState.Ready)
                return State.Nozzle != NozzleState.Welding ? "A: select welding nozzle" : State.Clamp != ClampState.Connected ? "A: connect virtual clamp" : "Tip to START, release trigger  /  A: arm";
            return "Trigger: weld  /  stick click: finish  /  B: STOP";
        }

        public TrainingInput Capture(double now) => input;
        RegistrationInput IRegistrationPort.Capture(double now) => registrationInput;
        public void Teardown()
        {
            if (registration && registration.enabled)
                registration.CancelRegistration();
        }

        public void Publish(ProcessSnapshot snapshot)
        {
        }

        public void Play(FeedbackCue cue, double strength, double durationSeconds)
        {
            hapticUntil = Seconds + Math.Min(durationSeconds, .15);
            OVRInput.SetControllerVibration(.5f, (float)strength * .3f, OVRInput.Controller.RTouch);
        }

        public void Stop() => StopImmediately();
        public void StopImmediately()
        {
            hapticUntil = 0;
            OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                Interrupt();
            else
                lifecycleInvalid = false;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
                Interrupt();
            else
                lifecycleInvalid = false;
        }

        void Interrupt()
        {
            lifecycleInvalid = true;
            StopImmediately();
            delivery?.AppPaused();
            view?.Clear();
            coordinator?.Submit(TrainingCommandType.Pause, Seconds);
        }

        void OnDisable()
        {
            StopImmediately();
            delivery?.Suspend();
            coordinator?.Dispose();
        }

        static DVec Domain(SVec v) => new(v.x, v.y, v.z);
        static Vector3 Raw(DVec v) => new((float)v.X, (float)v.Y, (float)v.Z);
    }
}
