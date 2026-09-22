using System;

namespace WeldingTrainer.Content.Spatial
{
    public readonly struct RigidPose
    {
        private readonly bool initialized;
        public string Destination { get; }
        public string Source { get; }
        public Vec3 Position { get; }
        public Quat Rotation { get; }

        public RigidPose(PoseData data)
        {
            SpatialValidation.Pose(data, data?.destination, data?.source, "pose");
            Destination = data.destination;
            Source = data.source;
            Position = data.position;
            Rotation = data.rotation;
            initialized = true;
        }

        private RigidPose(string destination, string source, Vec3 position, Quat rotation) : this(new PoseData { destination = destination, source = source, position = position, rotation = rotation, scale = 1 })
        {
        }

        private void CheckInitialized() => SpatialValidation.Require(initialized, "pose", "uninitialized rigid pose is not identity");
        public Vec3 TransformPoint(Vec3 point)
        {
            CheckInitialized();
            SpatialValidation.Vector(point, "point");
            var result = Rotation.Rotate(point) + Position;
            SpatialValidation.Vector(result, "transformed point");
            return result;
        }

        public Vec3 TransformDirection(Vec3 direction)
        {
            CheckInitialized();
            SpatialValidation.Vector(direction, "direction");
            var result = Rotation.Rotate(direction);
            SpatialValidation.Vector(result, "transformed direction");
            return result;
        }

        public RigidPose Inverse()
        {
            CheckInitialized();
            return new RigidPose(Source, Destination, Rotation.Conjugate.Rotate(Position * -1), Rotation.Conjugate);
        }

        public static RigidPose operator *(RigidPose a, RigidPose b)
        {
            a.CheckInitialized();
            b.CheckInitialized();
            if (a.Source != b.Destination)
                throw new ArgumentException($"Frame mismatch: {a.Destination}From{a.Source} * {b.Destination}From{b.Source}");
            return new RigidPose(a.Destination, b.Source, a.TransformPoint(b.Position), a.Rotation * b.Rotation);
        }
    }
}
