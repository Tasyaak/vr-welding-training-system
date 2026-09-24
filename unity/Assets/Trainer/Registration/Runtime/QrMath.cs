using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration
{
    public static class QrPayload
    {
        public const int MaximumBytes = 68;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        public static bool TryParse(byte[] payload, out string partId)
        {
            partId = null;
            if (payload == null || payload.Length < 5 || payload.Length > MaximumBytes)
                return false;
            string text;
            try
            {
                text = StrictUtf8.GetString(payload);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (!Regex.IsMatch(text, @"\ALW1:[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\z"))
                return false;
            partId = text.Substring(4);
            return true;
        }
    }

    public static class RegistrationMath
    {
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static RigidPose Pose(string destination, string source, Vec3 position, Quat rotation) => new RigidPose(new PoseData { destination = destination, source = source, position = position, rotation = rotation, scale = 1 });
        public static bool IsPose(RigidPose? pose, string destination, string source)
        {
            if (!pose.HasValue || pose.Value.Destination != destination || pose.Value.Source != source)
                return false;
            try
            {
                pose.Value.TransformPoint(new Vec3());
                return true;
            }
            catch (SpatialContentException)
            {
                return false;
            }
        }

        public static double Angle(Quat a, Quat b)
        {
            return 2 * Math.Acos(Math.Min(1, Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w)));
        }

        public static RigidPose Mean(IReadOnlyList<QrObservation> samples)
        {
            if (samples.Count == 0)
                throw new ArgumentException("Empty pose window");
            Vec3 p = new Vec3();
            Quat q = new Quat();
            var reference = samples[0].WorldFromMarker.Value.Rotation;
            foreach (var sample in samples)
            {
                var pose = sample.WorldFromMarker.Value;
                p += pose.Position;
                var r = pose.Rotation;
                double sign = reference.x * r.x + reference.y * r.y + reference.z * r.z + reference.w * r.w < 0 ? -1 : 1;
                q.x += sign * r.x;
                q.y += sign * r.y;
                q.z += sign * r.z;
                q.w += sign * r.w;
            }

            double length = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (length < 1e-9)
                throw new ArgumentException("Indeterminate rotation mean");
            return Pose("World", "Marker", p * (1.0 / samples.Count), new Quat(q.x / length, q.y / length, q.z / length, q.w / length));
        }

        // Input already uses RH World and RH MrukPlane coordinates (S_z on both sides of the Unity pose).
        // MRUK reflects native plane X. Marker restores physical printed +X and outward +Z via Ry(pi).
        public static RigidPose CenterMrukPlane(RigidPose worldFromTrackable, double centerX, double centerY)
        {
            if (!IsPose(worldFromTrackable, "World", "MrukPlane"))
                throw new ArgumentException("Expected WorldFromMrukPlane");
            return worldFromTrackable * Pose("MrukPlane", "Marker", new Vec3(centerX, centerY, 0), new Quat(0, 1, 0, 0));
        }
    }

    // Software repeatability gates, NOT a measured physical error budget. Versioned and immutable.
    public sealed class RegistrationQuality
    {
        public int MinimumObservations => 4;
        public int MaximumObservations => 32;
        public double MinimumSpan => 2.0;
        public double Window => 6.0;
        public double QrMaxAge => 1.5;
        public double RequiredTrackingMaxAge => .25;
        public double AcquireTimeout => 45;
        public double PreviewTimeout => 30;
        public double AnchorTimeout => 15;
        public double TranslationScatter => .003;
        public double AngularScatter => 2 * Math.PI / 180;
        public double ConflictTranslation => .015;
        public double ConflictAngle => 8 * Math.PI / 180;
        public double DimensionRelativeTolerance => .08;
        public double MinimumViewingDistance => .15;
        public double MaximumViewingDistance => 2;
        public double MinimumFacingCosine => .5;
    }
}
