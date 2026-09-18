using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    /// Converts raw EvaluationResult into a prioritised FeedbackState.
    /// Assistance level controls cue intensity without modifying tolerances or scoring.
    public sealed class FeedbackEngine
    {
        private readonly FeedbackState _state;
        private float _previousProgress;

        public FeedbackState State => _state;

        public FeedbackEngine(float assistanceLevel = 100f)
        {
            _state = new FeedbackState(assistanceLevel);
        }

        public void Reset() => _previousProgress = 0f;

        /// Call once per evaluated sample to update the feedback state.
        public void Update(EvaluationResult result, bool attemptComplete)
        {
            if (attemptComplete)
            {
                _state.Update(FeedbackFlags.Complete);
                return;
            }

            FeedbackFlags flags = FeedbackFlags.None;

            // Priority 1: safety / tracking
            if (result.Status == WeldingStatus.Suspended)
            {
                _state.Update(FeedbackFlags.TrackingLost);
                _previousProgress = result.Progress;
                return;
            }

            // Priority 2: activation blocking
            if (result.Status == WeldingStatus.Inhibited)
                flags |= FeedbackFlags.Blocked;

            // Priority 3: position
            if (!result.DistanceGood)
                flags |= FeedbackFlags.OffPath;

            // Priority 4: speed (only meaningful while attempting to weld)
            if (result.WeldingActive || result.Status == WeldingStatus.Inhibited)
            {
                if (result.SpeedMetresPerSecond > 0f && !result.SpeedGood)
                {
                    float speed = result.SpeedMetresPerSecond;
                    // Determine if too fast or too slow from sign relative to target
                    // (FeedbackEngine doesn't store tolerance; use a simple heuristic:
                    // TooSlow if near zero, TooFast otherwise)
                    flags |= speed < 0.03f ? FeedbackFlags.TooSlow : FeedbackFlags.TooFast;
                }
            }

            // Priority 5: orientation
            if (!result.OrientationGood)
                flags |= FeedbackFlags.BadOrientation;

            // Priority 6: reverse motion
            if (result.Progress < _previousProgress - 0.005f)
                flags |= FeedbackFlags.ReverseMotion;

            _previousProgress = result.Progress;
            _state.Update(flags);
        }
    }
}
