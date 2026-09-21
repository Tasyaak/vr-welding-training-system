using System;
using System.Collections.Generic;

namespace WeldingTrainer.Domain
{
    public enum SessionLifecycle
    {
        Idle, Initializing, Registering, Selecting, Ready, Running, Suspended,
        Reviewing, Saving, Completed, Aborted, Faulted
    }

    public enum ActivationState { Disarmed, Armed, Active, Inhibited, EmergencyStopped }
    public enum ClampState { Disconnected, Connected }
    public enum FaultSeverity { Warning, Inhibiting, Latched }

    [Flags]
    public enum InhibitReason : ulong
    {
        None = 0,
        MissingInput = 1UL << 0,
        MissingRegistration = 1UL << 1,
        RegistrationInvalid = 1UL << 2,
        RegistrationChanged = 1UL << 3,
        HeadTrackingInvalid = 1UL << 4,
        ToolTrackingInvalid = 1UL << 5,
        NozzleUnknown = 1UL << 6,
        NozzleMismatch = 1UL << 7,
        ClampDisconnected = 1UL << 8,
        SafetyProviderUnavailable = 1UL << 9,
        SafetyRejected = 1UL << 10,
        RecorderUnavailable = 1UL << 11,
        EmergencyStopLatched = 1UL << 12,
        TriggerReleaseRequired = 1UL << 13,
        SessionNotRunning = 1UL << 14,
        MaximumSampleGapExceeded = 1UL << 15
    }

    public sealed class ProcessFault
    {
        public string Code { get; }
        public FaultSeverity Severity { get; }
        public string Detail { get; }
        public ProcessFault(string code, FaultSeverity severity, string detail)
        { Code = code; Severity = severity; Detail = detail; }
    }

    public sealed class AttemptConfiguration
    {
        public string AttemptId { get; }
        public string SeamId { get; }
        public ProcessMode Mode { get; }
        public ProcessProfile Profile { get; }
        public ContentSnapshot Content { get; }
        public string SourceAttemptId { get; }
        public AttemptConfiguration(string attemptId, string seamId, ProcessMode mode,
            ProcessProfile profile, ContentSnapshot content, string sourceAttemptId)
        { AttemptId = attemptId; SeamId = seamId; Mode = mode; Profile = profile;
          Content = content; SourceAttemptId = sourceAttemptId; }
    }

    public sealed class ProcessSnapshot
    {
        private readonly ProcessFault[] _faults;
        public long Sequence { get; }
        public double MonotonicSeconds { get; }
        public string SessionId { get; }
        public string AttemptId { get; }
        public SessionLifecycle Lifecycle { get; }
        public ActivationState Activation { get; }
        public ProcessMode? Mode { get; }
        public string SeamId { get; }
        public string NozzleId { get; }
        public ClampState Clamp { get; }
        public long RegistrationGeneration { get; }
        public long InputGeneration { get; }
        public InhibitReason InhibitReasons { get; }
        public IReadOnlyList<ProcessFault> Faults => Array.AsReadOnly(_faults);

        public ProcessSnapshot(long sequence, double time, string sessionId, string attemptId,
            SessionLifecycle lifecycle, ActivationState activation, ProcessMode? mode,
            string seamId, string nozzleId, ClampState clamp, long registrationGeneration,
            long inputGeneration, InhibitReason reasons, ProcessFault[] faults)
        { Sequence = sequence; MonotonicSeconds = time; SessionId = sessionId;
          AttemptId = attemptId; Lifecycle = lifecycle; Activation = activation; Mode = mode;
          SeamId = seamId; NozzleId = nozzleId; Clamp = clamp;
          RegistrationGeneration = registrationGeneration; InputGeneration = inputGeneration;
          InhibitReasons = reasons; _faults = faults == null ? Array.Empty<ProcessFault>() : (ProcessFault[])faults.Clone(); }
    }
}
