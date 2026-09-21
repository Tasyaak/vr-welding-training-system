using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public interface IMonotonicClock { double Seconds { get; } }
    public interface IIdentifierSource { string NewId(); }

    public readonly struct InputSnapshot
    {
        public readonly bool Available, HeadValid, ToolValid, TriggerPressed, EmergencyStopPressed;
        public readonly long Generation;
        public readonly double SourceTimestampSeconds;
        public InputSnapshot(bool available, bool headValid, bool toolValid, bool trigger,
            bool emergencyStop, long generation, double sourceTimestamp)
        { Available = available; HeadValid = headValid; ToolValid = toolValid;
          TriggerPressed = trigger; EmergencyStopPressed = emergencyStop;
          Generation = generation; SourceTimestampSeconds = sourceTimestamp; }
    }

    public readonly struct RegistrationSnapshot
    {
        public readonly bool Available, Valid;
        public readonly long Generation;
        public RegistrationSnapshot(bool available, bool valid, long generation)
        { Available = available; Valid = valid; Generation = generation; }
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
    public interface IRegistrationStateSource { RegistrationSnapshot Capture(double monotonicSeconds); void Teardown(); }
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
