using System;

namespace WeldingTrainer.Domain
{
    public enum ProcessMode { Fusion, Wobble, Pulsed, PreWeldCleaning, PostWeldCleaning }

    public sealed class RangeSetting
    {
        public double Minimum { get; }
        public double Maximum { get; }
        public RangeSetting(double minimum, double maximum) { Minimum = minimum; Maximum = maximum; }
        public void ValidateInto(string path, ValidationResult result)
        {
            ValidationRules.Finite(Minimum, path + ".minimum", result);
            ValidationRules.Finite(Maximum, path + ".maximum", result);
            if (ValidationRules.IsFinite(Minimum) && ValidationRules.IsFinite(Maximum) && Minimum > Maximum)
                result.Add(path, "minimum must not exceed maximum");
        }
    }

    public abstract class ProcessSettings { public abstract ProcessMode Mode { get; } }
    public sealed class FusionSettings : ProcessSettings { public override ProcessMode Mode => ProcessMode.Fusion; }
    public sealed class WobbleSettings : ProcessSettings
    {
        public override ProcessMode Mode => ProcessMode.Wobble;
        public double WidthMetres { get; }
        public double FrequencyHertz { get; }
        public WobbleSettings(double width, double frequency) { WidthMetres = width; FrequencyHertz = frequency; }
    }
    public sealed class PulsedSettings : ProcessSettings
    {
        public override ProcessMode Mode => ProcessMode.Pulsed;
        public double OnSeconds { get; }
        public double OffSeconds { get; }
        public PulsedSettings(double onSeconds, double offSeconds) { OnSeconds = onSeconds; OffSeconds = offSeconds; }
    }
    public sealed class CleaningSettings : ProcessSettings
    {
        public override ProcessMode Mode { get; }
        public CleaningSettings(ProcessMode mode)
        {
            if (mode != ProcessMode.PreWeldCleaning && mode != ProcessMode.PostWeldCleaning)
                throw new ArgumentException("Cleaning settings require a cleaning mode.", nameof(mode));
            Mode = mode;
        }
    }

    public sealed class ProcessProfile
    {
        public string Id { get; }
        public int SchemaVersion { get; }
        public ProcessSettings Settings { get; }
        public string RequiredNozzleId { get; }
        public string MetricSchemaId => Settings != null &&
            (Settings.Mode == ProcessMode.PreWeldCleaning || Settings.Mode == ProcessMode.PostWeldCleaning)
            ? "cleaning-coverage-v1" : "welding-quality-v1";
        public double FootprintWidthMetres { get; }
        public double FootprintLengthMetres { get; }
        public RangeSetting SpeedMetresPerSecond { get; }
        public RangeSetting StandoffMetres { get; }
        public RangeSetting WorkAngleRadians { get; }
        public RangeSetting TravelAngleRadians { get; }
        public double ContactEnterMetres { get; }
        public double ContactExitMetres { get; }
        public double MaximumSampleGapSeconds { get; }
        public double ReflectionConeRadians { get; }
        public double ReflectionTargetMarginMetres { get; }
        public double AssistancePercent { get; }

        public ProcessProfile(string id, int schemaVersion, ProcessSettings settings, string requiredNozzleId,
            double footprintWidth, double footprintLength, RangeSetting speed, RangeSetting standoff,
            RangeSetting workAngle, RangeSetting travelAngle, double contactEnter, double contactExit,
            double maxSampleGap, double reflectionCone, double reflectionTargetMargin, double assistancePercent)
        { Id = id; SchemaVersion = schemaVersion; Settings = settings; RequiredNozzleId = requiredNozzleId;
          FootprintWidthMetres = footprintWidth; FootprintLengthMetres = footprintLength;
          SpeedMetresPerSecond = speed; StandoffMetres = standoff; WorkAngleRadians = workAngle;
          TravelAngleRadians = travelAngle; ContactEnterMetres = contactEnter; ContactExitMetres = contactExit;
          MaximumSampleGapSeconds = maxSampleGap; ReflectionConeRadians = reflectionCone;
          ReflectionTargetMarginMetres = reflectionTargetMargin; AssistancePercent = assistancePercent; }

        public ValidationResult Validate()
        {
            var result = new ValidationResult();
            ValidationRules.Id(Id, "id", result);
            if (SchemaVersion != 1) result.Add("schemaVersion", "unsupported process-profile schema; expected 1");
            if (Settings == null) result.Add("settings", "must contain exactly one mode-specific settings object");
            ValidationRules.Id(RequiredNozzleId, "requiredNozzleId", result);
            ValidationRules.Positive(FootprintWidthMetres, "footprintWidthMetres", result);
            ValidationRules.Positive(FootprintLengthMetres, "footprintLengthMetres", result);
            SpeedMetresPerSecond?.ValidateInto("speedMetresPerSecond", result);
            StandoffMetres?.ValidateInto("standoffMetres", result);
            WorkAngleRadians?.ValidateInto("workAngleRadians", result);
            TravelAngleRadians?.ValidateInto("travelAngleRadians", result);
            if (SpeedMetresPerSecond == null) result.Add("speedMetresPerSecond", "is required");
            if (StandoffMetres == null) result.Add("standoffMetres", "is required");
            if (WorkAngleRadians == null) result.Add("workAngleRadians", "is required");
            if (TravelAngleRadians == null) result.Add("travelAngleRadians", "is required");
            ValidationRules.Positive(ContactEnterMetres, "contactEnterMetres", result);
            ValidationRules.Positive(ContactExitMetres, "contactExitMetres", result);
            if (ValidationRules.IsFinite(ContactEnterMetres) && ValidationRules.IsFinite(ContactExitMetres) && ContactExitMetres < ContactEnterMetres)
                result.Add("contactExitMetres", "must be at least the enter threshold to provide hysteresis");
            ValidationRules.Positive(MaximumSampleGapSeconds, "maximumSampleGapSeconds", result);
            ValidationRules.Positive(ReflectionConeRadians, "reflectionConeRadians", result);
            ValidationRules.Positive(ReflectionTargetMarginMetres, "reflectionTargetMarginMetres", result);
            if (!ValidationRules.IsFinite(AssistancePercent) || AssistancePercent < 0 || AssistancePercent > 100)
                result.Add("assistancePercent", "must be finite and in [0, 100]");

            if (Settings is WobbleSettings wobble)
            { ValidationRules.Positive(wobble.WidthMetres, "settings.widthMetres", result); ValidationRules.Positive(wobble.FrequencyHertz, "settings.frequencyHertz", result); }
            else if (Settings is PulsedSettings pulsed)
            { ValidationRules.Positive(pulsed.OnSeconds, "settings.onSeconds", result); ValidationRules.Positive(pulsed.OffSeconds, "settings.offSeconds", result); }
            return result;
        }
    }
}
