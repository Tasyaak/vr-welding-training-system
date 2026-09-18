namespace WeldingTrainer.Domain
{
    /// Time-weighted metric accumulator for one training attempt.
    public sealed class AttemptMetrics
    {
        private float _weldingSeconds;
        private float _weightedErrorSum;
        private float _weightedSpeedSum;
        private float _qualitySeconds;
        private float _blockedTriggerSeconds;
        private float _invalidTrackingSeconds;
        private int _trackingInterruptionCount;
        private bool _inTrackingLoss;
        private float _attemptStartSeconds;
        private float _lastTimestamp;
        private bool _started;

        public int TrackingInterruptionCount => _trackingInterruptionCount;
        public float InvalidTrackingSeconds => _invalidTrackingSeconds;
        public float WeldingActiveSeconds => _weldingSeconds;
        public float BlockedTriggerSeconds => _blockedTriggerSeconds;

        public float AttemptElapsedSeconds =>
            _started ? _lastTimestamp - _attemptStartSeconds : 0f;

        public float AverageErrorMetres =>
            _weldingSeconds > 0f ? _weightedErrorSum / _weldingSeconds : 0f;

        public float AverageSpeedMetresPerSecond =>
            _weldingSeconds > 0f ? _weightedSpeedSum / _weldingSeconds : 0f;

        public float QualityInRangePercent =>
            _weldingSeconds > 0f ? 100f * _qualitySeconds / _weldingSeconds : 0f;

        public void RecordSample(EvaluationResult result, float timestamp)
        {
            if (!_started)
            {
                _attemptStartSeconds = timestamp;
                _lastTimestamp = timestamp;
                _started = true;
                return;
            }

            float dt = timestamp - _lastTimestamp;
            if (dt < 0f) dt = 0f;
            _lastTimestamp = timestamp;

            if (result.Status == WeldingStatus.Suspended)
            {
                if (!_inTrackingLoss)
                {
                    _inTrackingLoss = true;
                    _trackingInterruptionCount++;
                }
                _invalidTrackingSeconds += dt;
                return;
            }
            _inTrackingLoss = false;

            if (result.WeldingActive)
            {
                _weldingSeconds += dt;
                _weightedErrorSum += result.DistanceMetres * dt;
                _weightedSpeedSum += result.SpeedMetresPerSecond * dt;
                bool allGood = result.DistanceGood && result.SpeedGood && result.OrientationGood;
                if (allGood) _qualitySeconds += dt;
            }
            else if (result.Status == WeldingStatus.Inhibited)
            {
                _blockedTriggerSeconds += dt;
            }
        }

        public void Reset()
        {
            _weldingSeconds = 0f;
            _weightedErrorSum = 0f;
            _weightedSpeedSum = 0f;
            _qualitySeconds = 0f;
            _blockedTriggerSeconds = 0f;
            _invalidTrackingSeconds = 0f;
            _trackingInterruptionCount = 0;
            _inTrackingLoss = false;
            _started = false;
        }
    }
}
