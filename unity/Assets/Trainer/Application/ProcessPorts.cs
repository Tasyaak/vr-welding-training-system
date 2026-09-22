using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum InputContext { Training, Calibration, Menu, Suspended }

    [System.Flags]
    public enum InputCommandEdges
    {
        None = 0, EmergencyStop = 1 << 0, Confirm = 1 << 1,
        MenuToggle = 1 << 2, TriggerPressed = 1 << 3, TriggerReleased = 1 << 4
    }

    public readonly struct TrackedPoseSnapshot
    {
        public readonly RigidPose Pose;
        public readonly bool DeviceValid, PositionValid, RotationValid;
        public bool IsValid => DeviceValid && PositionValid && RotationValid;
        public TrackedPoseSnapshot(RigidPose pose, bool deviceValid, bool positionValid, bool rotationValid)
        { Pose = pose; DeviceValid = deviceValid; PositionValid = positionValid; RotationValid = rotationValid; }
        public static TrackedPoseSnapshot Invalid => new(default, false, false, false);
    }

    public interface IMonotonicClock { double Seconds { get; } }
    public interface IIdentifierSource { string NewId(); }

    public readonly struct InputSnapshot
    {
        public readonly bool Available, HeadValid, ToolValid, TriggerPressed, EmergencyStopPressed;
        public readonly long Generation;
        public readonly double SourceTimestampSeconds;
        public readonly float TriggerValue;
        public readonly InputContext Context;
        public readonly InputCommandEdges CommandEdges;
        public readonly TrackedPoseSnapshot Head, Controller, Tool, Tip;
        public InputSnapshot(bool available, bool headValid, bool toolValid, bool trigger,
            bool emergencyStop, long generation, double sourceTimestamp)
        { Available = available; HeadValid = headValid; ToolValid = toolValid;
          TriggerPressed = trigger; EmergencyStopPressed = emergencyStop;
          Generation = generation; SourceTimestampSeconds = sourceTimestamp;
          TriggerValue = trigger ? 1f : 0f; Context = InputContext.Training;
          CommandEdges = emergencyStop ? InputCommandEdges.EmergencyStop : InputCommandEdges.None;
          Head = Controller = Tool = Tip = TrackedPoseSnapshot.Invalid; }

        public InputSnapshot(bool available, TrackedPoseSnapshot head, TrackedPoseSnapshot controller,
            TrackedPoseSnapshot tool, TrackedPoseSnapshot tip, float triggerValue, bool triggerPressed,
            InputCommandEdges edges, InputContext context, long generation, double sourceTimestamp)
        { Available = available; Head = head; Controller = controller; Tool = tool; Tip = tip;
          HeadValid = head.IsValid; ToolValid = tool.IsValid && tip.IsValid;
          TriggerValue = triggerValue; TriggerPressed = triggerPressed;
          EmergencyStopPressed = (edges & InputCommandEdges.EmergencyStop) != 0;
          CommandEdges = edges; Context = context; Generation = generation;
          SourceTimestampSeconds = sourceTimestamp; }
    }

    public readonly struct RegistrationSnapshot
    {
        public readonly bool Available, Valid;
        public readonly long Generation;
        public readonly RegistrationWorkflowState State;
        public readonly RigidPose WorldFromFixture, WorldFromWorkpiece;
        public readonly string InvalidReason;
        public RegistrationSnapshot(bool available, bool valid, long generation)
        { Available = available; Valid = valid; Generation = generation; State = valid ? RegistrationWorkflowState.Registered : RegistrationWorkflowState.Unregistered;
          WorldFromFixture = WorldFromWorkpiece = default; InvalidReason = valid ? null : "unavailable"; }
        public RegistrationSnapshot(bool available, bool valid, long generation, RegistrationWorkflowState state,
            RigidPose fixture, RigidPose workpiece, string reason)
        { Available=available;Valid=valid;Generation=generation;State=state;WorldFromFixture=fixture;
          WorldFromWorkpiece=workpiece;InvalidReason=reason; }
    }

    public readonly struct SafetyDecision
    {
        public readonly bool Available, Allowed;
        public readonly InhibitReason Reasons;
        public SafetyDecision(bool available, bool allowed, InhibitReason reasons)
        { Available = available; Allowed = allowed; Reasons = reasons; }
        public static SafetyDecision Missing => new(false, false, InhibitReason.SafetyProviderUnavailable);
    }

    public readonly struct EvaluationRequest
    {
        public readonly double TimestampSeconds;
        public readonly InputSnapshot Input;
        public readonly RegistrationSnapshot Registration;
        public readonly AttemptConfiguration Attempt;
        public EvaluationRequest(double time, InputSnapshot input, RegistrationSnapshot registration,
            AttemptConfiguration attempt)
        { TimestampSeconds = time; Input = input; Registration = registration; Attempt = attempt; }
    }

    public interface IInputSnapshotSource { InputSnapshot Capture(double monotonicSeconds); }
    public interface IInputContextControl { InputContext Context { get; } void SetContext(InputContext context); }
    public interface IRegistrationStateSource { RegistrationSnapshot Capture(double monotonicSeconds); void Teardown(); }
    public interface IRegistrationInputConsumer { void UpdateInput(InputSnapshot input, double monotonicSeconds); }
    public interface ICalibrationWorkflow
    {
        RegistrationWorkflowState State { get; }
        string CurrentPointId { get; }
        RegistrationCandidate Candidate { get; }
        void AcceptPreview();
        void Recapture(string pointId);
        void ReportFixtureMoved();
    }
    public readonly struct AnchorCreationResult
    {
        public readonly bool Success, Localized; public readonly string Error;
        public AnchorCreationResult(bool success, bool localized, string error)
        { Success=success;Localized=localized;Error=error; }
    }
    public interface ISessionAnchorPort
    {
        void BeginCreate(RigidPose worldFromFixture, long generation, System.Action<long, AnchorCreationResult> completed);
        bool IsLocalized { get; }
        void DestroyAnchor();
    }
    public interface IActivationSafetyPort { SafetyDecision Evaluate(EvaluationRequest request); }
    public interface IProcessSnapshotSink { void Publish(ProcessSnapshot snapshot); }
    public interface IHapticLifecyclePort { void StopImmediately(); }

    public enum RecorderHealth { Unavailable, Ready, Faulted }
    public interface IProcessRecorder
    {
        RecorderHealth Health { get; }
        void Append(ProcessEvent processEvent);
        void BeginAttempt(AttemptConfiguration configuration);
        bool FinishAttempt(string attemptId, bool interrupted, out string error);
        void Teardown();
    }
}
