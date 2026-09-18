using System;
using UnityEngine;

namespace WeldingTrainer.Domain
{
    /// Directed straight seam between two points in the same coordinate frame.
    public sealed class StraightSeam
    {
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public Vector3 Direction { get; }
        public float LengthMetres { get; }

        public StraightSeam(Vector3 start, Vector3 end)
        {
            Vector3 d = end - start;
            LengthMetres = d.magnitude;
            if (LengthMetres < 1e-6f)
                throw new ArgumentException("Seam length must be greater than zero.", nameof(end));
            Start = start;
            End = end;
            Direction = d / LengthMetres;
        }

        public SeamProjection Project(Vector3 point)
        {
            float dot = Vector3.Dot(point - Start, Direction);
            float unclampedProgress = dot / LengthMetres;
            float clampedProgress = Mathf.Clamp01(unclampedProgress);
            Vector3 closest = Start + clampedProgress * LengthMetres * Direction;
            float distance = Vector3.Distance(point, closest);
            return new SeamProjection(unclampedProgress, clampedProgress, distance, closest);
        }
    }

    public readonly struct SeamProjection
    {
        public float UnclampedProgress { get; }
        public float Progress { get; }
        public float DistanceMetres { get; }
        public Vector3 ClosestPoint { get; }

        public SeamProjection(float unclamped, float clamped, float dist, Vector3 closest)
        {
            UnclampedProgress = unclamped;
            Progress = clamped;
            DistanceMetres = dist;
            ClosestPoint = closest;
        }
    }
}
