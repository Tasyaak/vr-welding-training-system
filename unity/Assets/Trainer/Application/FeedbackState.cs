using System;

namespace WeldingTrainer.Application
{
    [Flags]
    public enum FeedbackFlags : uint
    {
        None             = 0,
        TrackingLost     = 1 << 0,
        OffPath          = 1 << 1,
        TooFast          = 1 << 2,
        TooSlow          = 1 << 3,
        BadOrientation   = 1 << 4,
        ReverseMotion    = 1 << 5,
        Blocked          = 1 << 6,   // trigger pressed but activation conditions not met
        Complete         = 1 << 7,
    }

    /// The single source of truth for what the trainee is currently experiencing.
    public sealed class FeedbackState
    {
        /// Bitmask of active conditions, ordered by priority (lower bit = higher priority).
        public FeedbackFlags Flags { get; private set; }

        /// Assistance level in [0, 100]. Higher values increase cue intensity and visibility.
        public float AssistanceLevel { get; }

        /// A cue's effective intensity = base * IntensityScale.
        public float IntensityScale => AssistanceLevel / 100f;

        public bool Has(FeedbackFlags flag) => (Flags & flag) != 0;
        public bool IsWelding => Flags == FeedbackFlags.None || Flags == FeedbackFlags.Complete;

        public FeedbackState(float assistanceLevel = 100f)
        {
            AssistanceLevel = Math.Clamp(assistanceLevel, 0f, 100f);
        }

        public void Update(FeedbackFlags flags) => Flags = flags;

        public override string ToString() =>
            Flags == FeedbackFlags.None ? "OK" : Flags.ToString();
    }
}
