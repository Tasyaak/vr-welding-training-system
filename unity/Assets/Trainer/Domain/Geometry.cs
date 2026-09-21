using System;

namespace WeldingTrainer.Domain
{
    public readonly struct Vector3d : IEquatable<Vector3d>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public bool IsFinite => ValidationRules.IsFinite(X) && ValidationRules.IsFinite(Y) && ValidationRules.IsFinite(Z);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double LengthSquared => X * X + Y * Y + Z * Z;
        public Vector3d Normalized => Length > 1e-12 ? this / Length : default;
        public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);
        public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vector3d Cross(Vector3d a, Vector3d b) => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
        public static double Distance(Vector3d a, Vector3d b) => (a - b).Length;
        public bool Equals(Vector3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vector3d other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode(); }
        }
    }

    public readonly struct Quaterniond
    {
        public readonly double X, Y, Z, W;
        public Quaterniond(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public bool IsFinite => ValidationRules.IsFinite(X) && ValidationRules.IsFinite(Y) && ValidationRules.IsFinite(Z) && ValidationRules.IsFinite(W);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        public static Quaterniond Identity => new(0, 0, 0, 1);
    }

    /// Rigid destination-from-source transform. Scale is intentionally absent.
    public readonly struct RigidPose
    {
        public readonly Vector3d PositionMetres;
        public readonly Quaterniond Rotation;
        public RigidPose(Vector3d positionMetres, Quaterniond rotation)
        {
            PositionMetres = positionMetres;
            Rotation = rotation;
        }
        public static RigidPose Identity => new(default, Quaterniond.Identity);
    }
}
