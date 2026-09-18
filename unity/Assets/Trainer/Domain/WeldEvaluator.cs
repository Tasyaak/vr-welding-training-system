using UnityEngine;

namespace WeldingTrainer.Domain
{
    /// Stateful evaluator that compares each tool sample against a straight seam.
    /// One instance spans one training attempt; call BeginAttempt before the first sample.
    public sealed class WeldEvaluator
    {
        private const float SpeedSmoothingTauSeconds = 0.1f;

        private readonly StraightSeam _seam;
        private readonly SeamTolerance _tolerance;
        private readonly Vector3 _localToolForwardAxis;

        private float _previousUnclampedProgress;
        private float _previousTimestamp;
        private float _smoothedSpeed;
        private bool _startedWithinThreshold;
        private bool _hasFirstSample;

        public WeldEvaluator(StraightSeam seam, SeamTolerance tolerance, Vector3 localToolForwardAxis)
        {
            _seam = seam;
            _tolerance = tolerance;
            _localToolForwardAxis = localToolForwardAxis.normalized;
        }

        public void BeginAttempt()
        {
            _previousUnclampedProgress = 0f;
            _previousTimestamp = 0f;
            _smoothedSpeed = 0f;
            _startedWithinThreshold = false;
            _hasFirstSample = false;
        }

        public EvaluationResult Evaluate(ToolSample sample)
        {
            if (!sample.IsTracked)
            {
                _smoothedSpeed = 0f;
                _hasFirstSample = false;
                return new EvaluationResult(
                    _previousUnclampedProgress < 0 ? 0 : Mathf.Clamp01(_previousUnclampedProgress),
                    0f, 0f, 0f, 0f,
                    false, false, false, false, WeldingStatus.Suspended);
            }

            SeamProjection proj = _seam.Project(sample.TipPosition);
            float distance = proj.DistanceMetres;

            // Speed: filtered rate of seam-progress change
            float dt = _hasFirstSample
                ? Mathf.Max(0f, sample.TimestampSeconds - _previousTimestamp)
                : 0f;
            float rawSpeed = 0f;
            if (_hasFirstSample && dt > 1e-6f)
            {
                float progressDelta = proj.UnclampedProgress - _previousUnclampedProgress;
                rawSpeed = Mathf.Abs(progressDelta) * _seam.LengthMetres / dt;
            }
            float alpha = dt > 0f ? 1f - Mathf.Exp(-dt / SpeedSmoothingTauSeconds) : 0f;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, rawSpeed, alpha);

            // Angles
            Vector3 worldForward = sample.TipRotation * _localToolForwardAxis;
            float travelAngle = Vector3.Angle(worldForward, _seam.Direction);
            float workAngle = 0f; // requires workpiece surface normal; reported as 0 until integrated

            bool orientGood = !_tolerance.EvaluateOrientation ||
                (Mathf.Abs(travelAngle - _tolerance.TargetTravelAngleDegrees) <=
                 _tolerance.TravelAngleToleranceDegrees);

            // Progress continuity
            float progressDelta = proj.UnclampedProgress - _previousUnclampedProgress;
            bool forwardJump = progressDelta > _tolerance.MaxProgressJump;
            bool reverseStep = progressDelta < -_tolerance.ReverseProgressTolerance;

            if (!_startedWithinThreshold && proj.Progress <= _tolerance.StartProgressThreshold)
                _startedWithinThreshold = true;

            bool progressOk = _startedWithinThreshold && !forwardJump && !reverseStep;

            // Quality gates
            bool distGood = distance <= _tolerance.GoodDistanceMetres;
            bool speedGood = _smoothedSpeed >= _tolerance.MinSpeedMetresPerSecond &&
                             _smoothedSpeed <= _tolerance.MaxSpeedMetresPerSecond;
            bool weldingAllowed = progressOk && distGood &&
                (!_tolerance.OrientationInhibitsWelding || orientGood);

            bool weldingActive = sample.TriggerPressed && weldingAllowed;
            WeldingStatus status = (sample.TriggerPressed && !weldingAllowed)
                ? WeldingStatus.Inhibited
                : WeldingStatus.Active;

            _hasFirstSample = true;
            _previousUnclampedProgress = proj.UnclampedProgress;
            _previousTimestamp = sample.TimestampSeconds;

            return new EvaluationResult(
                proj.Progress, distance, _smoothedSpeed,
                travelAngle, workAngle,
                distGood, speedGood, orientGood,
                weldingActive, status);
        }

        public bool IsComplete(float progress) => progress >= _tolerance.CompletionThreshold;
    }
}
