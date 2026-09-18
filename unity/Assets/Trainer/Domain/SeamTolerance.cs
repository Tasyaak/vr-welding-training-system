using System;

namespace WeldingTrainer.Domain
{
    [Serializable]
    public sealed class SeamTolerance
    {
        public float GoodDistanceMetres = 0.025f;
        public float MaxDistanceMetres = 0.050f;
        public float MinSpeedMetresPerSecond = 0.05f;
        public float MaxSpeedMetresPerSecond = 0.15f;
        public float StartProgressThreshold = 0.08f;
        public float CompletionThreshold = 0.97f;
        public float MaxProgressJump = 0.08f;
        public float ReverseProgressTolerance = 0.03f;
        public bool EvaluateOrientation = false;
        public bool OrientationInhibitsWelding = false;
        public float TargetTravelAngleDegrees = 0f;
        public float TravelAngleToleranceDegrees = 15f;
        public float TargetWorkAngleDegrees = 45f;
        public float WorkAngleToleranceDegrees = 15f;

        public void Validate()
        {
            if (GoodDistanceMetres > MaxDistanceMetres)
                throw new InvalidOperationException(
                    "GoodDistanceMetres must not exceed MaxDistanceMetres.");
            if (MinSpeedMetresPerSecond > MaxSpeedMetresPerSecond)
                throw new InvalidOperationException(
                    "MinSpeedMetresPerSecond must not exceed MaxSpeedMetresPerSecond.");
            if (CompletionThreshold is < 0f or > 1f)
                throw new InvalidOperationException(
                    "CompletionThreshold must be in [0, 1].");
        }
    }
}
