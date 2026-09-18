using UnityEngine;

namespace WeldingTrainer.Domain
{
    /// A timestamped snapshot of the tracked welding-tool tip pose and input state.
    public readonly struct ToolSample
    {
        public float TimestampSeconds { get; }
        public Vector3 TipPosition { get; }
        public Quaternion TipRotation { get; }
        public bool IsTracked { get; }
        public bool TriggerPressed { get; }

        public ToolSample(float timestamp, Vector3 tipPosition, Quaternion tipRotation,
            bool isTracked, bool triggerPressed)
        {
            TimestampSeconds = timestamp;
            TipPosition = tipPosition;
            TipRotation = tipRotation;
            IsTracked = isTracked;
            TriggerPressed = triggerPressed;
        }

        public static ToolSample Untracked(float timestamp) =>
            new(timestamp, Vector3.zero, Quaternion.identity, false, false);
    }
}
