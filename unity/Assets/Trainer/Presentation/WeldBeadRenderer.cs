using System.Collections.Generic;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    /// Builds a progressive weld-bead mesh as the trainee passes along the seam.
    ///
    /// Maintains two coverage sets:
    ///   AttemptedSegments — any welding-active samples, regardless of quality
    ///   AcceptableSegments — samples inside all quality tolerances
    ///
    /// Call BeginAttempt() to reset, then UpdateBead() each sample tick.
    /// The renderer is replaced by swapping the material references.
    public sealed class WeldBeadRenderer : MonoBehaviour
    {
        [SerializeField] private Material _acceptableMaterial;
        [SerializeField] private Material _attemptedMaterial;
        [SerializeField] private float _beadRadiusMetres = 0.003f;
        [SerializeField] private int _circleSegments = 6;

        private readonly List<Vector3> _acceptablePoints = new();
        private readonly List<Vector3> _attemptedPoints = new();
        private LineRenderer _acceptableLine;
        private LineRenderer _attemptedLine;
        private bool _triggerWasPressed;

        private void Awake()
        {
            _acceptableLine = CreateLine("AcceptableBead", _acceptableMaterial);
            _attemptedLine = CreateLine("AttemptedBead", _attemptedMaterial);
        }

        public void BeginAttempt()
        {
            _acceptablePoints.Clear();
            _attemptedPoints.Clear();
            _triggerWasPressed = false;
            Apply();
        }

        /// Call once per tick from the training scene's Update.
        public void UpdateBead(ToolSample sample, EvaluationResult result, FeedbackState feedback)
        {
            if (!sample.IsTracked) return;

            bool triggerNow = sample.TriggerPressed;

            // Close previous segment on trigger release (creates a break in the bead)
            if (_triggerWasPressed && !triggerNow)
            {
                if (_attemptedPoints.Count > 0) _attemptedPoints.Add(Vector3.positiveInfinity);
                if (_acceptablePoints.Count > 0) _acceptablePoints.Add(Vector3.positiveInfinity);
            }

            if (triggerNow)
            {
                // Attempted coverage: any trigger-pressed sample
                _attemptedPoints.Add(sample.TipPosition);

                // Acceptable coverage: all quality conditions met
                bool qualityGood = result.WeldingActive &&
                    result.DistanceGood && result.SpeedGood &&
                    (result.OrientationGood || !feedback.Has(FeedbackFlags.BadOrientation));
                if (qualityGood) _acceptablePoints.Add(sample.TipPosition);
            }

            _triggerWasPressed = triggerNow;
            Apply();
        }

        private void Apply()
        {
            SetLine(_attemptedLine, _attemptedPoints);
            SetLine(_acceptableLine, _acceptablePoints);
        }

        private static void SetLine(LineRenderer line, List<Vector3> points)
        {
            // Filter out gap sentinels for the position array, use positionCount carefully
            var real = new List<Vector3>(points.Count);
            foreach (var p in points)
                if (!float.IsInfinity(p.x)) real.Add(p);

            line.positionCount = real.Count;
            if (real.Count > 0)
                line.SetPositions(real.ToArray());
        }

        private LineRenderer CreateLine(string childName, Material mat)
        {
            var go = new GameObject(childName) { transform = { parent = transform } };
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = mat;
            lr.startWidth = _beadRadiusMetres * 2f;
            lr.endWidth = _beadRadiusMetres * 2f;
            lr.positionCount = 0;
            return lr;
        }
    }
}
