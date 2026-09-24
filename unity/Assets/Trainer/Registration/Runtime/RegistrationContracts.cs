using System;
using System.Collections.Generic;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration
{
    public enum RegistrationState
    {
        Unregistered,
        Acquiring,
        Preview,
        Anchoring,
        Registered,
        Lost,
        Disposed
    }

    public enum RegistrationReason
    {
        None,
        Unsupported,
        PermissionRequired,
        PermissionDenied,
        ConfigurationPending,
        ConfigurationInvalid,
        ConventionUnqualified,
        AwaitingQr,
        MalformedPayload,
        UnknownPart,
        AmbiguousQr,
        InvalidPose,
        StaleObservation,
        UnstablePose,
        InvalidView,
        DimensionsUnknown,
        PrintCandidateNotSelected,
        DimensionsMismatch,
        MarkerPlaneUnqualified,
        AwaitingConfirmation,
        ConfirmationIncomplete,
        Timeout,
        Cancelled,
        AnchorFailed,
        AnchorLost,
        TrackingLost,
        OriginChanged,
        QrConflict,
        AssemblyChanged,
        WrongPart,
        MarkerMoved,
        SessionEnded,
        ClockInvalid,
        PlatformFailure,
        AwaitingTrackedQr
    }

    public enum AnchorStatus
    {
        Creating,
        Localizing,
        Localized,
        Failed,
        Lost,
        Disposed
    }

    public enum PermissionState
    {
        Unknown,
        Pending,
        Granted,
        Denied
    }

    public interface IMonotonicClock
    {
        double Now { get; }
    }

    public interface IRegistrationEvidenceSink
    {
        void Record(RegistrationSnapshot snapshot);
    }

    public interface IQrTracker : IDisposable
    {
        void RequestScanning();
        void StopScanning();
        // Each read contains ALL currently visible QR identities, including invalid/pose-less ones.
        // Sequence advances only on an SDK update, never on repeated polling.
        TrackerFrame Read();
    }

    public interface ISessionAnchor : IDisposable
    {
        AnchorFrame Read();
    }

    public interface ISessionAnchorFactory
    {
        // Owns an unsaved operation immediately, including pending creation/localization.
        ISessionAnchor Create(RigidPose worldFromAnchor, long registrationGeneration, long originGeneration);
    }

    public sealed class PlatformStatus
    {
        public bool Supported { get; }
        public PermissionState Permission { get; }
        public bool ConfigurationValid { get; }
        public bool Requested { get; }
        public bool Applied { get; }
        public bool ConventionQualified { get; }
        public bool TrackingValid { get; }
        public long OriginGeneration { get; }
        public double ReceivedAt { get; }
        public string RuntimeTuple { get; }
        public string ConventionEvidence { get; }

        public PlatformStatus(bool supported, PermissionState permission, bool configurationValid, bool requested, bool applied, bool conventionQualified, bool trackingValid, long originGeneration, double receivedAt, string runtimeTuple, string conventionEvidence)
        {
            Supported = supported;
            Permission = permission;
            ConfigurationValid = configurationValid;
            Requested = requested;
            Applied = applied;
            ConventionQualified = conventionQualified;
            TrackingValid = trackingValid;
            OriginGeneration = originGeneration;
            ReceivedAt = receivedAt;
            RuntimeTuple = runtimeTuple;
            ConventionEvidence = conventionEvidence;
        }
    }

    public sealed class QrObservation
    {
        private readonly byte[] payload;
        public byte[] CopyPayload() => payload == null ? null : (byte[])payload.Clone();
        public string TrackableId { get; }
        public long Sequence { get; }
        public long OriginGeneration { get; }
        public double ReceivedAt { get; }
        public double? SourceCapturedAt { get; }
        public RigidPose? WorldFromMarker { get; }
        public double WidthMetres { get; }
        public double HeightMetres { get; }
        public string DimensionConvention { get; }
        public Vec3? ObserverPosition { get; }

        public QrObservation(byte[] payload, string trackableId, long sequence, long originGeneration, double receivedAt, RigidPose? worldFromMarker, double widthMetres, double heightMetres, string dimensionConvention, Vec3? observerPosition, double? sourceCapturedAt = null)
        {
            this.payload = payload == null ? null : (byte[])payload.Clone();
            TrackableId = trackableId;
            Sequence = sequence;
            OriginGeneration = originGeneration;
            ReceivedAt = receivedAt;
            WorldFromMarker = worldFromMarker;
            WidthMetres = widthMetres;
            HeightMetres = heightMetres;
            DimensionConvention = dimensionConvention;
            ObserverPosition = observerPosition;
            SourceCapturedAt = sourceCapturedAt;
        }
    }

    // SDK object lifetime and current tracking are independent of the last captured pose.
    // LastObservation is diagnostic history, never promoted to fresh evidence by this status.
    public sealed class QrTrackableStatus
    {
        public string TrackableId { get; }
        public bool IsTracked { get; }
        public bool AwaitingTrackedUpdate { get; }
        public QrObservation LastObservation { get; }

        public QrTrackableStatus(string id, bool tracked, bool awaitingUpdate, QrObservation lastObservation)
        {
            TrackableId = id;
            IsTracked = tracked;
            AwaitingTrackedUpdate = awaitingUpdate;
            LastObservation = lastObservation;
        }
    }

    public sealed class TrackerFrame
    {
        public PlatformStatus Platform { get; }
        public IReadOnlyList<QrObservation> Observations { get; }
        public IReadOnlyList<QrTrackableStatus> Trackables { get; }

        public TrackerFrame(PlatformStatus platform, IEnumerable<QrObservation> observations, IEnumerable<QrTrackableStatus> trackables = null)
        {
            Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            Observations = new List<QrObservation>(observations ?? throw new ArgumentNullException(nameof(observations))).AsReadOnly();
            Trackables = new List<QrTrackableStatus>(trackables ?? Array.Empty<QrTrackableStatus>()).AsReadOnly();
        }
    }

    public sealed class AnchorFrame
    {
        public AnchorStatus Status { get; }
        public RigidPose? WorldFromAnchor { get; }
        public long RegistrationGeneration { get; }
        public long OriginGeneration { get; }
        public double ReceivedAt { get; }

        public AnchorFrame(AnchorStatus status, RigidPose? pose, long generation, long origin, double receivedAt)
        {
            Status = status;
            WorldFromAnchor = pose;
            RegistrationGeneration = generation;
            OriginGeneration = origin;
            ReceivedAt = receivedAt;
        }
    }

    public sealed class RegistrationSnapshot
    {
        public int SchemaVersion => 1;
        public string SessionId { get; }
        public RegistrationState State { get; }
        public RegistrationReason Reason { get; }
        public long Generation { get; }
        public long OriginGeneration { get; }
        public double CapturedAt { get; }
        public double? LastQrReceivedAt { get; }
        public double? QrSourceCapturedAt { get; }
        public double? ReprojectionConfidence => null;
        public double? AbsoluteAccuracyMetres => null;
        // Immutable #46 object includes all IDs/revisions, catalog & geometry hashes and source identity.
        public AssemblyBindingSnapshot Binding { get; }
        public RigidPose? WorldFromFixture { get; }
        public RigidPose? WorldFromWorkpiece { get; }
        public bool IsValid => State == RegistrationState.Registered && WorldFromWorkpiece.HasValue;

        public bool IsUsableAt(double monotonicNow, long expectedOriginGeneration) => IsValid && RegistrationMath.Finite(monotonicNow) && monotonicNow >= CapturedAt && monotonicNow - CapturedAt <= .25 && expectedOriginGeneration == OriginGeneration;
        public bool PhysicallyQualified => Binding?.Qualification == "Qualified";
        public bool EligibleForScoring => IsValid && Binding.ReadyForScoredRegistration;
        public int ObservationCount { get; }
        public double ObservationSpanSeconds { get; }
        public double? TranslationScatterMetres { get; }
        public double? AngularScatterRadians { get; }
        public double? ObservedWidthMetres { get; }
        public double? ObservedHeightMetres { get; }
        public string ObservedDimensionConvention { get; }
        public double? ConfirmedAt { get; }
        public double? AnchorReceivedAt { get; }
        public bool DimensionsChecked { get; }
        public bool AssemblyConfirmed { get; }
        public AnchorStatus? Anchor { get; }
        public PlatformStatus Platform { get; }
        public string QualityPolicy => "qr-stability-v1";
        public string DiagnosticDetail { get; }

        internal RegistrationSnapshot(RegistrationState state, RegistrationReason reason, long generation, long origin, double now, AssemblyBindingSnapshot binding, RigidPose? fixture, RigidPose? part, int count, double span, double? translation, double? angle, bool dimensions, bool confirmed, AnchorStatus? anchor, PlatformStatus platform, double? lastQr, double? sourceTime, string sessionId, QrObservation lastObservation, double? confirmedAt, double? anchorReceivedAt, string diagnosticDetail)
        {
            State = state;
            Reason = reason;
            Generation = generation;
            OriginGeneration = origin;
            CapturedAt = now;
            Binding = binding;
            WorldFromFixture = fixture;
            WorldFromWorkpiece = part;
            ObservationCount = count;
            ObservationSpanSeconds = span;
            TranslationScatterMetres = translation;
            AngularScatterRadians = angle;
            DimensionsChecked = dimensions;
            AssemblyConfirmed = confirmed;
            Anchor = anchor;
            Platform = platform;
            LastQrReceivedAt = lastQr;
            QrSourceCapturedAt = sourceTime;
            SessionId = sessionId;
            ObservedWidthMetres = lastObservation?.WidthMetres;
            ObservedHeightMetres = lastObservation?.HeightMetres;
            ObservedDimensionConvention = lastObservation?.DimensionConvention;
            ConfirmedAt = confirmedAt;
            AnchorReceivedAt = anchorReceivedAt;
            DiagnosticDetail = diagnosticDetail;
        }
    }
}
