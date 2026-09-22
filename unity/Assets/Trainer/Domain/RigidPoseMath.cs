namespace WeldingTrainer.Domain
{
    public static class RigidPoseMath
    {
        public static RigidPose Compose(RigidPose destinationFromMiddle, RigidPose middleFromSource)
        {
            return new RigidPose(destinationFromMiddle.PositionMetres + Rotate(destinationFromMiddle.Rotation,
                middleFromSource.PositionMetres), Multiply(destinationFromMiddle.Rotation, middleFromSource.Rotation));
        }

        public static Vector3d Rotate(Quaterniond q, Vector3d v)
        {
            var u = new Vector3d(q.X, q.Y, q.Z);
            return v + Vector3d.Cross(u, v) * (2 * q.W) + Vector3d.Cross(u, Vector3d.Cross(u, v)) * 2;
        }

        public static Quaterniond Multiply(Quaterniond a, Quaterniond b) => new(
            a.W*b.X + a.X*b.W + a.Y*b.Z - a.Z*b.Y,
            a.W*b.Y - a.X*b.Z + a.Y*b.W + a.Z*b.X,
            a.W*b.Z + a.X*b.Y - a.Y*b.X + a.Z*b.W,
            a.W*b.W - a.X*b.X - a.Y*b.Y - a.Z*b.Z);
    }
}
