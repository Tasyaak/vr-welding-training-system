using System;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    /// Common fail-closed geometry/risk/prerequisite decision for every process mode.
    public sealed class UnifiedActivationSafety : IActivationSafetyPort
    {
        private readonly FiniteSurfaceContactEvaluator _contact=new();
        private readonly IProspectiveRiskPort _risk;
        private readonly IProcessPrerequisitePort _prerequisites;
        public UnifiedActivationSafety(IProspectiveRiskPort risk,IProcessPrerequisitePort prerequisites)
        { _risk=risk;_prerequisites=prerequisites; }

        public SafetyDecision Evaluate(EvaluationRequest request)
        {
            if(request.Attempt==null)return SafetyDecision.Missing;
            bool registration=request.Registration.Available&&request.Registration.Valid;
            bool tracking=request.Input.Available&&request.Input.Tip.IsValid;
            Vector3d tipLocal=default;
            if(registration&&tracking)
            {RigidPose localFromWorld=RigidPoseMath.Inverse(request.Registration.WorldFromWorkpiece);
             tipLocal=RigidPoseMath.Rotate(localFromWorld.Rotation,request.Input.Tip.Pose.PositionMetres)+localFromWorld.PositionMetres;}
            ContactEvidence contact=_contact.Evaluate(request.Attempt.Content.Entry.Workpiece,tipLocal,
                registration&&tracking,request.Attempt.Profile.ContactEnterMetres,request.Attempt.Profile.ContactExitMetres);
            InhibitReason reasons=ContactReasons(contact);
            if(!Enum.IsDefined(typeof(ProcessMode),request.Attempt.Mode))reasons|=InhibitReason.UnsupportedProcess;
            RiskDisposition risk=_risk?.Evaluate(request,contact)??RiskDisposition.Unknown;
            if(risk==RiskDisposition.Unknown)reasons|=InhibitReason.ReflectionUnknown;
            else if(risk==RiskDisposition.Unsafe)reasons|=InhibitReason.ReflectionUnsafe;
            reasons|=_prerequisites?.Evaluate(request,contact)??InhibitReason.ProcessPrerequisiteMissing;
            return new SafetyDecision(true,reasons==InhibitReason.None,reasons,contact);
        }

        private static InhibitReason ContactReasons(ContactEvidence evidence)
        {
            if(evidence.State==ContactState.Contact)return InhibitReason.None;
            InhibitReason reason=InhibitReason.ContactInvalid;
            if(evidence.InvalidReason==ContactInvalidReason.OutsideBounds)reason|=InhibitReason.OutsideFiniteSurface;
            if(evidence.InvalidReason==ContactInvalidReason.WrongApproach)reason|=InhibitReason.WrongApproachSide;
            if(evidence.InvalidReason==ContactInvalidReason.Ambiguous)reason|=InhibitReason.SurfaceAmbiguous;
            return reason;
        }
    }
}
