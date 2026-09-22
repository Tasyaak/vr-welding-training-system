using System;

namespace WeldingTrainer.Domain
{
    public enum ProcessMode
    {
        Fusion,
        Wobble,
        Pulsed,
        PreWeldCleaning,
        PostWeldCleaning
    }

    public enum SessionState
    {
        Idle,
        Initializing,
        Registering,
        Selecting,
        Ready,
        Running,
        Suspended,
        Saving,
        Reviewing,
        Completed,
        Aborted,
        Faulted
    }

    public enum ActivationState
    {
        Inhibited,
        ResetRequired,
        ReadyDisarmed,
        Armed,
        Active
    }

    public enum NozzleState
    {
        Unknown,
        Welding,
        Cleaning
    }

    public enum ClampState
    {
        Disconnected,
        Connected
    }

    [Flags]
    public enum BlockReason : ulong
    {
        None = 0,
        NoSession = 1,
        RegistrationInvalid = 2,
        OriginMismatch = 4,
        HeadInvalid = 8,
        ToolInvalid = 16,
        StaleInput = 32,
        NozzleUnknown = 64,
        NozzleMismatch = 128,
        ClampDisconnected = 256,
        SafetyUnknown = 512,
        SafetyRejected = 1024,
        RecorderUnavailable = 2048,
        TriggerReleaseRequired = 4096,
        EmergencyStop = 8192,
        FaultLatched = 16384,

        // Added without renumbering the existing schema-v1 flags.
        InputUnavailable = 32768,
        SystemInvalid = 65536,
        RegistrationChanged = 131072,
        FixtureMismatch = 262144,
        AssemblyUnconfirmed = 1UL << 19,
        BindingMismatch = 1UL << 20,
        ContactInvalid = 1UL << 21,
        OutsideFiniteSurface = 1UL << 22,
        WrongApproach = 1UL << 23,
        AmbiguousSurface = 1UL << 24,
        ReflectionUnknown = 1UL << 25,
        ReflectionUnsafe = 1UL << 26,
        ProcessPrerequisiteMissing = 1UL << 27,
        MenuOpen = 1UL << 28
    }

    public readonly struct Vec3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Finite =>
            double.IsFinite(X) &&
            double.IsFinite(Y) &&
            double.IsFinite(Z);

        public double LengthSquared =>
            X * X + Y * Y + Z * Z;

        public double Length =>
            Math.Sqrt(LengthSquared);

        public Vec3 Normalized
        {
            get
            {
                double length = Length;
                return Finite && double.IsFinite(length) && length > 1e-12
                    ? this / length
                    : default;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) =>
            new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator *(Vec3 value, double scalar) =>
            new(value.X * scalar, value.Y * scalar, value.Z * scalar);

        public static Vec3 operator *(double scalar, Vec3 value) =>
            value * scalar;

        public static Vec3 operator /(Vec3 value, double scalar) =>
            new(value.X / scalar, value.Y / scalar, value.Z / scalar);

        public static double Dot(Vec3 a, Vec3 b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
    }

    public readonly struct Quat
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;
        public readonly double W;

        public Quat(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public bool Finite =>
            double.IsFinite(X) &&
            double.IsFinite(Y) &&
            double.IsFinite(Z) &&
            double.IsFinite(W);
    }

    public readonly struct RigidPose
    {
        public readonly string DestinationFrame;
        public readonly string SourceFrame;
        public readonly Vec3 PositionMetres;
        public readonly Quat Rotation;

        public RigidPose(
            string destination,
            string source,
            Vec3 positionMetres,
            Quat rotation)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("Destination frame is required.", nameof(destination));
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Source frame is required.", nameof(source));
            if (!positionMetres.Finite || !rotation.Finite)
                throw new ArgumentException("Rigid pose must contain only finite values.");

            DestinationFrame = destination;
            SourceFrame = source;
            PositionMetres = positionMetres;
            Rotation = rotation;
        }
    }

    public sealed class ProcessProfile
    {
        public string Id { get; }
        public int Version { get; }
        public ProcessMode Mode { get; }
        public string Hash { get; }
        public double MaximumSampleGapSeconds { get; }

        public ProcessProfile(
            string id,
            int version,
            ProcessMode mode,
            string hash,
            double maximumSampleGapSeconds)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Profile ID is required.", nameof(id));
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version));
            if (string.IsNullOrWhiteSpace(hash))
                throw new ArgumentException("Profile hash is required.", nameof(hash));
            if (!Enum.IsDefined(typeof(ProcessMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (!double.IsFinite(maximumSampleGapSeconds) || maximumSampleGapSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(maximumSampleGapSeconds));

            Id = id;
            Version = version;
            Mode = mode;
            Hash = hash;
            MaximumSampleGapSeconds = maximumSampleGapSeconds;
        }
    }

    public sealed class ProcessConfiguration
    {
        public const int SchemaVersion = 1;

        public string FixtureId { get; }
        public string SeamId { get; }
        public ProcessProfile Profile { get; }
        public string SourceAttemptId { get; }

        public ProcessConfiguration(
            string fixture,
            string seam,
            ProcessProfile profile,
            string sourceAttempt = null)
        {
            if (string.IsNullOrWhiteSpace(fixture))
                throw new ArgumentException("Fixture ID is required.", nameof(fixture));
            if (string.IsNullOrWhiteSpace(seam))
                throw new ArgumentException("Seam ID is required.", nameof(seam));

            FixtureId = fixture;
            SeamId = seam;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            SourceAttemptId = sourceAttempt;
        }
    }

    public sealed class AttemptConfiguration
    {
        public string AttemptId { get; }
        public ProcessConfiguration Process { get; }
        public long RegistrationGeneration { get; }

        public AttemptConfiguration(
            string id,
            ProcessConfiguration process,
            long registrationGeneration)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Attempt ID is required.", nameof(id));

            AttemptId = id;
            Process = process ?? throw new ArgumentNullException(nameof(process));
            RegistrationGeneration = registrationGeneration;
        }
    }

    public sealed class ProcessSnapshot
    {
        public long Sequence { get; }
        public string SessionId { get; }
        public string AttemptId { get; }
        public SessionState Session { get; }
        public ActivationState Activation { get; }
        public NozzleState Nozzle { get; }
        public ClampState Clamp { get; }
        public BlockReason Reasons { get; }
        public bool Requested { get; }
        public BlockReason PrimaryReason => ActivationDecision.Primary(Reasons);

        public bool Permission =>
            Activation == ActivationState.Armed ||
            Activation == ActivationState.Active;

        public ProcessSnapshot(
            long sequence,
            string session,
            string attempt,
            SessionState state,
            ActivationState activation,
            NozzleState nozzle,
            ClampState clamp,
            BlockReason reasons,
            bool requested)
        {
            Sequence = sequence;
            SessionId = session;
            AttemptId = attempt;
            Session = state;
            Activation = activation;
            Nozzle = nozzle;
            Clamp = clamp;
            Reasons = reasons;
            Requested = requested;
        }
    }

    public enum ProcessEventType
    {
        SessionStarted,
        StateChanged,
        RegistrationAccepted,
        AttemptPrepared,
        NozzleChanged,
        ClampChanged,
        Armed,
        Disarmed,
        Suspended,
        Resumed,
        SavingStarted,
        ReviewReady,
        SaveFailed,
        EmergencyStop,
        EmergencyReset,
        SessionCompleted,
        SessionAborted
    }

    public sealed class ProcessEvent
    {
        public const int SchemaVersion = 1;

        public long Sequence { get; }
        public double MonotonicSeconds { get; }
        public string SessionId { get; }
        public string AttemptId { get; }
        public long InputGeneration { get; }
        public long RegistrationGeneration { get; }
        public string ProfileHash { get; }
        public ProcessEventType Type { get; }
        public string Payload { get; }

        public ProcessEvent(
            long sequence,
            double time,
            string session,
            string attempt,
            long inputGeneration,
            long registrationGeneration,
            string profileHash,
            ProcessEventType type,
            string payload)
        {
            if (!double.IsFinite(time))
                throw new ArgumentOutOfRangeException(nameof(time));

            Sequence = sequence;
            MonotonicSeconds = time;
            SessionId = session;
            AttemptId = attempt;
            InputGeneration = inputGeneration;
            RegistrationGeneration = registrationGeneration;
            ProfileHash = profileHash;
            Type = type;
            Payload = payload ?? string.Empty;
        }
    }
}
