using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Content
{
    public static class UnityDomainConversion
    {
        public static Vector3d ToDomainVector(Vector3 value) => new(value.x, value.y, value.z);
        public static Vector3 ToUnityVector(Vector3d value) => new((float)value.X, (float)value.Y, (float)value.Z);

        public static RigidPose ToDomainPose(Vector3 positionMetres, Quaternion rotation)
            => new(ToDomainVector(positionMetres), new Quaterniond(rotation.x, rotation.y, rotation.z, rotation.w));

        public static void ToUnityPose(RigidPose pose, out Vector3 positionMetres, out Quaternion rotation)
        {
            positionMetres = ToUnityVector(pose.PositionMetres);
            rotation = new Quaternion((float)pose.Rotation.X, (float)pose.Rotation.Y,
                (float)pose.Rotation.Z, (float)pose.Rotation.W);
        }
    }
}
