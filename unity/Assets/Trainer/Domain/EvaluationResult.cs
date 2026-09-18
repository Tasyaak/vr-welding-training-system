namespace WeldingTrainer.Domain
{
    public enum WeldingStatus
    {
        /// Normal state: tracking valid, trigger may or may not be pressed.
        Active,
        /// Trigger pressed but one or more welding conditions are not met.
        Inhibited,
        /// Tracking unavailable; evaluation is suspended.
        Suspended,
    }

    public readonly struct EvaluationResult
    {
        public float Progress { get; }
        public float DistanceMetres { get; }
        public float SpeedMetresPerSecond { get; }
        public float TravelAngleDegrees { get; }
        public float WorkAngleDegrees { get; }
        public bool DistanceGood { get; }
        public bool SpeedGood { get; }
        public bool OrientationGood { get; }
        public bool WeldingActive { get; }
        public WeldingStatus Status { get; }

        public EvaluationResult(
            float progress,
            float distanceMetres,
            float speedMetresPerSecond,
            float travelAngleDegrees,
            float workAngleDegrees,
            bool distanceGood,
            bool speedGood,
            bool orientationGood,
            bool weldingActive,
            WeldingStatus status)
        {
            Progress = progress;
            DistanceMetres = distanceMetres;
            SpeedMetresPerSecond = speedMetresPerSecond;
            TravelAngleDegrees = travelAngleDegrees;
            WorkAngleDegrees = workAngleDegrees;
            DistanceGood = distanceGood;
            SpeedGood = speedGood;
            OrientationGood = orientationGood;
            WeldingActive = weldingActive;
            Status = status;
        }
    }
}
