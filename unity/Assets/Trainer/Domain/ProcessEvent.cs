using System;

namespace WeldingTrainer.Domain
{
    public enum ProcessEventType
    {
        SessionStarted, LifecycleChanged, RegistrationAccepted, AttemptPrepared,
        ClampChanged, NozzleChanged, Armed, Disarmed, ActivationChanged,
        Suspended, Resumed, AttemptFinished, SavingStarted, SavingFailed,
        ReviewReady, EmergencyStopLatched, EmergencyStopReset, FaultRaised,
        SessionCompleted, SessionAborted
    }

    public sealed class ProcessEvent
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; }
        public long Sequence { get; }
        public double MonotonicSeconds { get; }
        public string SessionId { get; }
        public string AttemptId { get; }
        public long InputGeneration { get; }
        public long RegistrationGeneration { get; }
        public string ProfileHash { get; }
        public ProcessEventType Type { get; }
        public string Payload { get; }

        public ProcessEvent(long sequence, double time, string sessionId, string attemptId,
            long inputGeneration, long registrationGeneration, string profileHash,
            ProcessEventType type, string payload)
        { SchemaVersion = CurrentSchemaVersion; Sequence = sequence; MonotonicSeconds = time;
          SessionId = sessionId; AttemptId = attemptId; InputGeneration = inputGeneration;
          RegistrationGeneration = registrationGeneration; ProfileHash = profileHash;
          Type = type; Payload = payload ?? string.Empty; }
    }
}
