using System;

namespace WeldingTrainer.Domain
{
    public enum ReflectionRiskLevel { Unknown, Low, Warning, High }
    public enum ReflectionUnknownReason { None, MissingTracking, MissingImpact, InvalidNormal, InvalidIncident, WrongIncidence, MixedGeneration }

    public readonly struct ReflectionTarget
    {
        public readonly string Id; public readonly Vector3d Center; public readonly double RadiusMetres;
        public ReflectionTarget(string id,Vector3d center,double radius){Id=id;Center=center;RadiusMetres=radius;}
    }
    public sealed class ReflectionRiskResult
    {
        public ReflectionRiskLevel Level { get; } public ReflectionUnknownReason UnknownReason { get; }
        public string SurfaceId { get; } public string TargetId { get; }
        public Vector3d Impact,Incident,Normal,Reflected;
        public double AngularClearanceRadians { get; } public bool IdealRayIntersection { get; }
        public ReflectionRiskResult(ReflectionRiskLevel level,ReflectionUnknownReason reason,string surface,string target,
            Vector3d impact,Vector3d incident,Vector3d normal,Vector3d reflected,double clearance,bool ideal)
        {Level=level;UnknownReason=reason;SurfaceId=surface;TargetId=target;Impact=impact;Incident=incident;Normal=normal;Reflected=reflected;AngularClearanceRadians=clearance;IdealRayIntersection=ideal;}
    }

    public static class SimulatedBackReflectionEvaluator
    {
        public static ReflectionRiskResult Evaluate(string surfaceId,Vector3d impact,Vector3d incident,
            Vector3d normal,ReflectionTarget target,double coneHalfAngleRadians,double warningMarginRadians)
        {
            if(string.IsNullOrWhiteSpace(surfaceId)||!impact.IsFinite)return Unknown(ReflectionUnknownReason.MissingImpact);
            if(!normal.IsFinite||Math.Abs(normal.Length-1)>.001)return Unknown(ReflectionUnknownReason.InvalidNormal);
            if(!incident.IsFinite||Math.Abs(incident.Length-1)>.001)return Unknown(ReflectionUnknownReason.InvalidIncident);
            if(coneHalfAngleRadians<=0||coneHalfAngleRadians>=Math.PI/2||target.RadiusMetres<=0)return Unknown(ReflectionUnknownReason.InvalidIncident);
            double incidence=Vector3d.Dot(incident,normal);if(incidence>=-1e-6)return Unknown(ReflectionUnknownReason.WrongIncidence);
            Vector3d reflected=(incident-normal*(2*incidence)).Normalized;
            Vector3d v=target.Center-impact;double distance=v.Length;
            if(distance<=target.RadiusMetres)return Result(ReflectionRiskLevel.High,target,impact,incident,normal,reflected,-Math.PI,true,surfaceId);
            double forward=Vector3d.Dot(reflected,v);bool ideal=false;
            if(forward>=0){double closestSquared=v.LengthSquared-forward*forward;ideal=closestSquared<=target.RadiusMetres*target.RadiusMetres;}
            if(forward<=0)return Result(ReflectionRiskLevel.Low,target,impact,incident,normal,reflected,Math.PI,false,surfaceId);
            double beta=Math.Acos(Clamp(Vector3d.Dot(reflected,v/distance)));
            double angularRadius=Math.Asin(Clamp(target.RadiusMetres/distance));
            double clearance=beta-(coneHalfAngleRadians+angularRadius);
            ReflectionRiskLevel level=ideal||clearance<=0?ReflectionRiskLevel.High:
                clearance<=warningMarginRadians?ReflectionRiskLevel.Warning:ReflectionRiskLevel.Low;
            return Result(level,target,impact,incident,normal,reflected,clearance,ideal,surfaceId);
        }
        private static double Clamp(double x)=>Math.Max(-1,Math.Min(1,x));
        private static ReflectionRiskResult Unknown(ReflectionUnknownReason reason)=>new(ReflectionRiskLevel.Unknown,reason,null,null,default,default,default,default,0,false);
        private static ReflectionRiskResult Result(ReflectionRiskLevel l,ReflectionTarget t,Vector3d p,Vector3d d,Vector3d n,Vector3d r,double c,bool ideal,string s)
            =>new(l,ReflectionUnknownReason.None,s,t.Id,p,d,n,r,c,ideal);
    }
}
