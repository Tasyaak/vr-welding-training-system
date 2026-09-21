using System;

namespace WeldingTrainer.FusionDemo
{
    /// <summary>
    /// Presentation model, not a temperature/physics solver. Each existing seam cell
    /// stores equivalent seconds at reference power, dwell time and irreversible damage.
    /// A swept, finite-width footprint gives frame-rate independent residence times.
    /// </summary>
    public sealed class SeamThermalModel
    {
        public readonly float[] HeatSeconds;
        public readonly float[] DwellSeconds;
        public readonly float[] PenetrationMm;
        public readonly bool[] Formed;
        public readonly bool[] Burned;
        public readonly bool[] Active;
        public int Count => HeatSeconds.Length;
        public int BurnedCount { get; private set; }
        public float Length { get; }
        public float ReferencePower = 1500f;
        public float Footprint = 0.024f;
        public float CoolingTime = 2f;
        public float FormationDose = 0.12f;
        public float ReferenceExposure = 0.18f;
        public float ReferenceDepth = 3f;
        public float Thickness = 6f;
        public float DepthExponent = 1f;
        public float WarningFraction = 0.85f;
        public float BurnExtraDose = 0.35f;

        public float FullDepthDose => FormationDose + ReferenceExposure *
            (float)Math.Pow(Thickness / Math.Max(0.01f, ReferenceDepth), 1 / Math.Max(0.1f, DepthExponent));
        public float BurnDose => FullDepthDose + BurnExtraDose;
        public float WarningDose => FormationDose + ReferenceExposure *
            (float)Math.Pow(Thickness * WarningFraction / Math.Max(0.01f, ReferenceDepth), 1 / Math.Max(0.1f, DepthExponent));
        public bool IsWarning(int i) => !Burned[i] && HeatSeconds[i] >= WarningDose;

        public SeamThermalModel(int count, float length)
        {
            if (count < 1 || !(length > 0)) throw new ArgumentOutOfRangeException();
            Length = length;
            HeatSeconds = new float[count]; DwellSeconds = new float[count];
            PenetrationMm = new float[count]; Formed = new bool[count];
            Burned = new bool[count]; Active = new bool[count];
        }

        public void Step(float seconds, float powerWatts, float from, float to, bool welding)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double dt = seconds;
            double tau = Math.Max(0.01f, CoolingTime);
            double power = welding ? Math.Max(0, powerWatts) / Math.Max(1, ReferencePower) : 0;
            double radius = Math.Max(Length / Count, Footprint) * 0.5;
            double travel = to - from;
            for (int i = 0; i < Count; i++)
            {
                double centre = (i + 0.5) * Length / Count;
                double enter = 0, leave = 0;
                if (power > 0 && dt > 0)
                {
                    if (Math.Abs(travel) < 1e-8)
                    { if (Math.Abs(centre - to) <= radius) leave = dt; }
                    else
                    {
                        double a = (centre - radius - from) / travel;
                        double b = (centre + radius - from) / travel;
                        enter = Math.Max(0, Math.Min(1, Math.Min(a, b))) * dt;
                        leave = Math.Max(0, Math.Min(1, Math.Max(a, b))) * dt;
                    }
                }
                double exposure = Math.Max(0, leave - enter);
                Active[i] = exposure > 0;
                double heat = HeatSeconds[i] * Math.Exp(-enter / tau);
                double decay = Math.Exp(-exposure / tau);
                heat = heat * decay + power * tau * (1 - decay);
                if (Active[i])
                {
                    DwellSeconds[i] += (float)exposure;
                    Formed[i] |= heat >= FormationDose;
                    float depth = ReferenceDepth * (float)Math.Pow(
                        Math.Max(0, heat - FormationDose) / Math.Max(0.001f, ReferenceExposure),
                        Math.Max(0.1f, DepthExponent));
                    PenetrationMm[i] = Math.Max(PenetrationMm[i], Math.Min(Thickness, depth));
                    if (!Burned[i] && heat >= BurnDose)
                    { Burned[i] = true; BurnedCount++; }
                }
                HeatSeconds[i] = (float)(heat * Math.Exp(-(dt - leave) / tau));
            }
        }

        public void Clear()
        {
            Array.Clear(HeatSeconds, 0, Count); Array.Clear(DwellSeconds, 0, Count);
            Array.Clear(PenetrationMm, 0, Count); Array.Clear(Formed, 0, Count);
            Array.Clear(Burned, 0, Count); Array.Clear(Active, 0, Count);
            BurnedCount = 0;
        }
    }
}
