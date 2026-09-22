using System;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public interface IReflectionFaultControl { bool Latched { get; } void RequestAcknowledgement(); }
    public interface IReflectionRiskSource { ReflectionRiskResult LastResult { get; } bool ReflectionLatched { get; } }

    public sealed class ReflectionRiskService : IProspectiveRiskPort, IReflectionFaultControl, IReflectionRiskSource
    {
        public const double HeadRadiusMetres=.14, PositionUncertaintyMetres=.03;
        public const double MarginalConfirmationSeconds=.05, StableLowSeconds=.2;
        private double _highSince=double.NaN,_lowSince=double.NaN; private bool _acknowledged;
        public bool Latched { get; private set; }
        public bool ReflectionLatched=>Latched;
        public ReflectionRiskResult LastResult { get; private set; }

        public RiskDisposition Evaluate(EvaluationRequest request,ContactEvidence contact)
        {
            if(!request.Input.Head.IsValid||!request.Input.Tool.IsValid||contact.State!=ContactState.Contact)
            {LastResult=new ReflectionRiskResult(ReflectionRiskLevel.Unknown,ReflectionUnknownReason.MissingTracking,null,null,default,default,default,default,0,false);return RiskDisposition.Unknown;}
            RigidPose localFromWorld=RigidPoseMath.Inverse(request.Registration.WorldFromWorkpiece);
            Vector3d head=RigidPoseMath.Rotate(localFromWorld.Rotation,request.Input.Head.Pose.PositionMetres)+localFromWorld.PositionMetres;
            Vector3d incidentWorld=RigidPoseMath.Rotate(request.Input.Tool.Pose.Rotation,request.Attempt.Content.Entry.Tool.IncidentBeamDirectionFromHead).Normalized;
            Vector3d incident=RigidPoseMath.Rotate(localFromWorld.Rotation,incidentWorld).Normalized;
            var target=new ReflectionTarget("tracked-head-volume",head,HeadRadiusMetres+PositionUncertaintyMetres+request.Attempt.Profile.ReflectionTargetMarginMetres);
            LastResult=SimulatedBackReflectionEvaluator.Evaluate(contact.SurfaceId,contact.ClosestPoint,incident,
                contact.OutwardNormal,target,request.Attempt.Profile.ReflectionConeRadians,.05);
            UpdateLatch(request.TimestampSeconds,request.Input.TriggerPressed);
            if(LastResult.Level==ReflectionRiskLevel.Unknown)return RiskDisposition.Unknown;
            if(Latched||LastResult.Level==ReflectionRiskLevel.High)return RiskDisposition.Unsafe;
            return RiskDisposition.Safe;
        }

        private void UpdateLatch(double now,bool trigger)
        {
            if(LastResult.Level==ReflectionRiskLevel.High){_lowSince=double.NaN;
                if(LastResult.IdealRayIntersection){Latched=true;return;}
                if(double.IsNaN(_highSince))_highSince=now;if(now-_highSince>=MarginalConfirmationSeconds)Latched=true;return;}
            _highSince=double.NaN;
            if(LastResult.Level==ReflectionRiskLevel.Low){if(double.IsNaN(_lowSince))_lowSince=now;
                if(Latched&&_acknowledged&&!trigger&&now-_lowSince>=StableLowSeconds){Latched=false;_acknowledged=false;}}
            else _lowSince=double.NaN;
        }
        public void RequestAcknowledgement()=>_acknowledged=true;
    }
}
