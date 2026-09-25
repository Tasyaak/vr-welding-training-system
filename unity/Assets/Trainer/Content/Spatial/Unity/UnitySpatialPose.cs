using System;
using UnityEngine;

namespace WeldingTrainer.Content.Spatial.Unity
{
    // Ordinary rigid poses only. Marker-specific normalization remains in Registration.
    public static class UnitySpatialPose
    {
        public static Vec3 ToDomain(Vector3 v) => new Vec3(v.x, v.y, -v.z);
        public static Vector3 ToUnity(Vec3 v) => new Vector3((float)v.x, (float)v.y, (float)-v.z);
        public static Quat ToDomain(Quaternion q)
        {
            double n = (double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w;
            if (double.IsNaN(n) || double.IsInfinity(n) || Math.Abs(n - 1) > 1e-5)
                throw new ArgumentException("SDK quaternion must be finite and unit length");
            n = Math.Sqrt(n);
            // S_z R S_z: basis reflection, never an Euler correction.
            return new Quat(-q.x / n, -q.y / n, q.z / n, q.w / n);
        }
        public static Quaternion ToUnity(Quat q) => new Quaternion((float)-q.x, (float)-q.y, (float)q.z, (float)q.w);
        public static RigidPose Read(Transform transform, string source)
        {
            var s = transform.lossyScale;
            if (!float.IsFinite(s.x) || !float.IsFinite(s.y) || !float.IsFinite(s.z) || (s - Vector3.one).sqrMagnitude > 1e-10f)
                throw new ArgumentException("Spatial transform requires unit scale");
            return new RigidPose(new PoseData { destination = "World", source = source, position = ToDomain(transform.position), rotation = ToDomain(transform.rotation), scale = 1 });
        }
        public static void Write(Transform transform, RigidPose pose)
        {
            transform.SetPositionAndRotation(ToUnity(pose.Position), ToUnity(pose.Rotation));
            transform.localScale = Vector3.one;
        }
    }
}
