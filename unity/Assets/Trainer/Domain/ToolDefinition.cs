using System;

namespace WeldingTrainer.Domain
{
    public enum GripPoseConvention { RightHandTouchPlusGrip }

    public sealed class ToolDefinition
    {
        public string Id { get; }
        public int Version { get; }
        public GripPoseConvention GripPose { get; }
        public RigidPose ToolFromController { get; }
        public RigidPose EffectiveTipFromTool { get; }
        public Vector3d BackwardToolAxis { get; }
        public Vector3d IncidentBeamDirectionFromHead { get; }

        public ToolDefinition(string id, int version, GripPoseConvention gripPose,
            RigidPose toolFromController, RigidPose effectiveTipFromTool,
            Vector3d backwardToolAxis, Vector3d incidentBeamDirectionFromHead)
        { Id = id; Version = version; GripPose = gripPose; ToolFromController = toolFromController;
          EffectiveTipFromTool = effectiveTipFromTool; BackwardToolAxis = backwardToolAxis;
          IncidentBeamDirectionFromHead = incidentBeamDirectionFromHead; }

        public ValidationResult Validate()
        {
            var result = new ValidationResult();
            ValidationRules.Id(Id, "id", result);
            if (Version < 1) result.Add("version", "must be at least 1");
            ValidationRules.Unit(BackwardToolAxis, "backwardToolAxis", result);
            ValidationRules.Unit(IncidentBeamDirectionFromHead, "incidentBeamDirectionFromHead", result);
            ValidatePose(ToolFromController, "toolFromController", result);
            ValidatePose(EffectiveTipFromTool, "effectiveTipFromTool", result);
            return result;
        }

        private static void ValidatePose(RigidPose pose, string path, ValidationResult result)
        {
            if (!pose.PositionMetres.IsFinite || !pose.Rotation.IsFinite || Math.Abs(pose.Rotation.Length - 1) > 1e-5)
                result.Add(path, "must be a finite rigid pose with unit quaternion; scale must remain one");
        }
    }
}
