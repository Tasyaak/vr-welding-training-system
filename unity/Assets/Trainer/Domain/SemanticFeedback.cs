using System;

namespace WeldingTrainer.Domain
{
    [Flags]
    public enum FeedbackCue : ulong
    {
        None = 0,
        TrackingLost = 1,
        RegistrationLost = 2,
        EmergencyStop = 4,
        ReflectionRisk = 8,
        ActivationBlocked = 16,
        NozzleMismatch = 32,
        ClampDisconnected = 64,
        ContactInvalid = 128,
        ReverseMotion = 256,
        OffPath = 512,
        TooSlow = 1024,
        CorrectSpeed = 2048,
        TooFast = 4096,
        TravelAngleError = 8192,
        WorkAngleError = 16384,
        CleaningIncomplete = 32768,
        Progress = 65536,
        Complete = 131072
    }

    public enum FeedbackSeverity
    {
        None,
        Informational,
        Coaching,
        Warning,
        Mandatory
    }

    public readonly struct FeedbackInput
    {
        public readonly long InputGeneration, RegistrationGeneration;
        public readonly double MonotonicSeconds, Progress;
        public readonly bool InputValid, RegistrationValid, OffPath, TooSlow, CorrectSpeed, TooFast, TravelAngleError, WorkAngleError, ReverseMotion, CleaningIncomplete, Complete;
        public readonly BlockReason BlockReasons;
        public FeedbackInput(long inputGeneration, long registrationGeneration, double time, double progress, bool inputValid, bool registrationValid, BlockReason blocked, bool offPath, bool tooSlow, bool correctSpeed, bool tooFast, bool travelAngle, bool workAngle, bool reverse, bool cleaningIncomplete, bool complete)
        {
            InputGeneration = inputGeneration;
            RegistrationGeneration = registrationGeneration;
            MonotonicSeconds = time;
            Progress = progress;
            InputValid = inputValid;
            RegistrationValid = registrationValid;
            BlockReasons = blocked;
            OffPath = offPath;
            TooSlow = tooSlow;
            CorrectSpeed = correctSpeed;
            TooFast = tooFast;
            TravelAngleError = travelAngle;
            WorkAngleError = workAngle;
            ReverseMotion = reverse;
            CleaningIncomplete = cleaningIncomplete;
            Complete = complete;
        }
    }

    public sealed class AssistanceProfile
    {
        public double MinimumCueSeconds { get; }
        public double MinimumCadenceSeconds { get; }
        public double MaximumCadenceSeconds { get; }

        public AssistanceProfile(double minimumCueSeconds = .2, double minimumCadenceSeconds = .12, double maximumCadenceSeconds = 1)
        {
            if (minimumCueSeconds < 0 || minimumCadenceSeconds <= 0 || maximumCadenceSeconds < minimumCadenceSeconds)
                throw new ArgumentException("Invalid assistance profile");
            MinimumCueSeconds = minimumCueSeconds;
            MinimumCadenceSeconds = minimumCadenceSeconds;
            MaximumCadenceSeconds = maximumCadenceSeconds;
        }
    }

    public readonly struct FeedbackState
    {
        public const int SchemaVersion = 1;
        public readonly FeedbackCue ActiveCues, PrimaryCue;
        public readonly FeedbackSeverity Severity;
        public readonly double Assistance, VisualStrength, AudioStrength, HapticStrength, CadenceSeconds, Progress;
        public readonly bool Mandatory, MetricsEligible;
        public FeedbackState(FeedbackCue active, FeedbackCue primary, FeedbackSeverity severity, double assistance, double visual, double audio, double haptic, double cadence, double progress, bool mandatory, bool metricsEligible)
        {
            ActiveCues = active;
            PrimaryCue = primary;
            Severity = severity;
            Assistance = assistance;
            VisualStrength = visual;
            AudioStrength = audio;
            HapticStrength = haptic;
            CadenceSeconds = cadence;
            Progress = progress;
            Mandatory = mandatory;
            MetricsEligible = metricsEligible;
        }
    }

    public sealed class SemanticFeedbackEngine
    {
        readonly AssistanceProfile profile;
        FeedbackCue retainedPrimary;
        double retainedSince = double.NegativeInfinity, lastTime = double.NegativeInfinity;
        public SemanticFeedbackEngine(AssistanceProfile profile = null)
        {
            this.profile = profile ?? new AssistanceProfile();
        }

        public void Reset()
        {
            retainedPrimary = FeedbackCue.None;
            retainedSince = double.NegativeInfinity;
            lastTime = double.NegativeInfinity;
        }

        public FeedbackState Evaluate(FeedbackInput input, double assistancePercent)
        {
            if (!double.IsFinite(input.MonotonicSeconds) || input.MonotonicSeconds < lastTime)
                throw new ArgumentException("Feedback time must be finite and monotonic");
            if (!double.IsFinite(assistancePercent) || assistancePercent < 0 || assistancePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(assistancePercent));
            lastTime = input.MonotonicSeconds;
            FeedbackCue cues = Collect(input);
            FeedbackCue candidate = Primary(cues);
            bool mandatory = IsMandatory(candidate);
            if (!mandatory && retainedPrimary != FeedbackCue.None && input.MonotonicSeconds - retainedSince < profile.MinimumCueSeconds && (cues & retainedPrimary) != 0)
                candidate = retainedPrimary;
            if (candidate != retainedPrimary)
            {
                retainedPrimary = candidate;
                retainedSince = input.MonotonicSeconds;
            }

            double assistance = assistancePercent / 100d;
            double strength = mandatory ? 1 : assistance;
            double cadence = mandatory ? profile.MinimumCadenceSeconds : Lerp(profile.MaximumCadenceSeconds, profile.MinimumCadenceSeconds, assistance);
            return new FeedbackState(cues, candidate, Severity(candidate), assistance, strength, strength, strength, cadence, Clamp01(input.Progress), mandatory, input.InputValid && input.RegistrationValid && (input.BlockReasons & BlockReason.OriginMismatch) == 0);
        }

        static FeedbackCue Collect(FeedbackInput i)
        {
            FeedbackCue c = FeedbackCue.None;
            if (!i.InputValid)
                c |= FeedbackCue.TrackingLost;
            if (!i.RegistrationValid || (i.BlockReasons & BlockReason.OriginMismatch) != 0)
                c |= FeedbackCue.RegistrationLost;
            if (i.BlockReasons != BlockReason.None)
                c |= FeedbackCue.ActivationBlocked;
            if ((i.BlockReasons & BlockReason.EmergencyStop) != 0)
                c |= FeedbackCue.EmergencyStop;
            if ((i.BlockReasons & BlockReason.NozzleMismatch) != 0)
                c |= FeedbackCue.NozzleMismatch;
            if ((i.BlockReasons & BlockReason.ClampDisconnected) != 0)
                c |= FeedbackCue.ClampDisconnected;
            if ((i.BlockReasons & (BlockReason.ReflectionUnknown | BlockReason.ReflectionUnsafe)) != 0)
                c |= FeedbackCue.ReflectionRisk;
            if ((i.BlockReasons & BlockReason.ContactInvalid) != 0)
                c |= FeedbackCue.ContactInvalid;
            if (i.ReverseMotion)
                c |= FeedbackCue.ReverseMotion;
            if (i.OffPath)
                c |= FeedbackCue.OffPath;
            if (i.TooSlow)
                c |= FeedbackCue.TooSlow;
            if (i.CorrectSpeed)
                c |= FeedbackCue.CorrectSpeed;
            if (i.TooFast)
                c |= FeedbackCue.TooFast;
            if (i.TravelAngleError)
                c |= FeedbackCue.TravelAngleError;
            if (i.WorkAngleError)
                c |= FeedbackCue.WorkAngleError;
            if (i.CleaningIncomplete)
                c |= FeedbackCue.CleaningIncomplete;
            if (i.Progress > 0)
                c |= FeedbackCue.Progress;
            if (i.Complete)
                c |= FeedbackCue.Complete;
            return c;
        }

        static FeedbackCue Primary(FeedbackCue c)
        {
            FeedbackCue[] order =
            {
                FeedbackCue.EmergencyStop,
                FeedbackCue.ReflectionRisk,
                FeedbackCue.TrackingLost,
                FeedbackCue.RegistrationLost,
                FeedbackCue.NozzleMismatch,
                FeedbackCue.ClampDisconnected,
                FeedbackCue.ContactInvalid,
                FeedbackCue.ActivationBlocked,
                FeedbackCue.ReverseMotion,
                FeedbackCue.OffPath,
                FeedbackCue.TravelAngleError,
                FeedbackCue.WorkAngleError,
                FeedbackCue.TooFast,
                FeedbackCue.TooSlow,
                FeedbackCue.CleaningIncomplete,
                FeedbackCue.Complete,
                FeedbackCue.CorrectSpeed,
                FeedbackCue.Progress
            };
            foreach (FeedbackCue cue in order)
                if ((c & cue) != 0)
                    return cue;
            return FeedbackCue.None;
        }

        static bool IsMandatory(FeedbackCue c) => (c & (FeedbackCue.EmergencyStop | FeedbackCue.ReflectionRisk | FeedbackCue.TrackingLost | FeedbackCue.RegistrationLost | FeedbackCue.ActivationBlocked | FeedbackCue.NozzleMismatch | FeedbackCue.ClampDisconnected | FeedbackCue.ContactInvalid)) != 0;
        static FeedbackSeverity Severity(FeedbackCue c) => c == FeedbackCue.None ? FeedbackSeverity.None : IsMandatory(c) ? FeedbackSeverity.Mandatory : (c == FeedbackCue.CorrectSpeed || c == FeedbackCue.Progress || c == FeedbackCue.Complete) ? FeedbackSeverity.Informational : (c == FeedbackCue.ReverseMotion ? FeedbackSeverity.Warning : FeedbackSeverity.Coaching);
        static double Clamp01(double v) => !double.IsFinite(v) ? 0 : Math.Max(0, Math.Min(1, v));
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
