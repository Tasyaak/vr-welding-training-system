using System;
using System.Collections.Generic;

namespace WeldingTrainer.Domain
{
    public enum ContactState { Invalid, Separated, Contact }
    public enum ContactInvalidReason { None, TrackingInvalid, OutsideBounds, WrongApproach, Ambiguous, InvalidNormal }

    public readonly struct ContactEvidence
    {
        public readonly ContactState State;
        public readonly ContactInvalidReason InvalidReason;
        public readonly string SurfaceId;
        public readonly Vector3d ClosestPoint, OutwardNormal;
        public readonly double SignedStandoffMetres, EdgeDistanceMetres;
        public readonly bool ApproachValid, InsideBounds;
        public ContactEvidence(ContactState state,ContactInvalidReason reason,string surfaceId,
            Vector3d closest,Vector3d normal,double standoff,double edgeDistance,bool approach,bool inside)
        {State=state;InvalidReason=reason;SurfaceId=surfaceId;ClosestPoint=closest;OutwardNormal=normal;
         SignedStandoffMetres=standoff;EdgeDistanceMetres=edgeDistance;ApproachValid=approach;InsideBounds=inside;}
    }

    public sealed class FiniteSurfaceContactEvaluator
    {
        private string _contactSurfaceId;
        public ContactEvidence Evaluate(WorkpieceDefinition workpiece,Vector3d tipLocal,
            bool trackingValid,double enterMetres,double exitMetres)
        {
            if(!trackingValid||workpiece==null||!tipLocal.IsFinite){_contactSurfaceId=null;return Invalid(ContactInvalidReason.TrackingInvalid);}
            double band=_contactSurfaceId==null?enterMetres:exitMetres;
            var candidates=new List<ContactEvidence>();
            foreach(SurfacePatch patch in workpiece.Surfaces)
            {
                Vector3d n=patch.OutwardNormal.Normalized;if(n.Length<.999){_contactSurfaceId=null;return Invalid(ContactInvalidReason.InvalidNormal);}
                Vector3d origin=patch.BoundaryMetres[0];double signed=Vector3d.Dot(tipLocal-origin,n);
                Vector3d projected=tipLocal-n*signed;bool inside=Inside(projected,patch,n);
                double edge=EdgeDistance(projected,patch);bool approach=patch.ApproachSide>0?signed>=-1e-9:signed<=1e-9;
                if(inside&&approach&&Math.Abs(signed)<=band)
                    candidates.Add(new ContactEvidence(ContactState.Contact,ContactInvalidReason.None,patch.Id,projected,n,signed,edge,true,true));
            }
            if(candidates.Count>1){_contactSurfaceId=null;return Invalid(ContactInvalidReason.Ambiguous);}
            if(candidates.Count==1){_contactSurfaceId=candidates[0].SurfaceId;return candidates[0];}
            _contactSurfaceId=null;
            SurfacePatch nearest=null;double nearestPlane=double.PositiveInfinity;ContactEvidence evidence=default;
            foreach(SurfacePatch patch in workpiece.Surfaces){Vector3d n=patch.OutwardNormal.Normalized;Vector3d origin=patch.BoundaryMetres[0];double signed=Vector3d.Dot(tipLocal-origin,n);Vector3d projected=tipLocal-n*signed;
                double distance=Math.Abs(signed);if(distance<nearestPlane){nearestPlane=distance;nearest=patch;bool inside=Inside(projected,patch,n);bool approach=patch.ApproachSide>0?signed>=-1e-9:signed<=1e-9;
                    evidence=new ContactEvidence(ContactState.Separated,!inside?ContactInvalidReason.OutsideBounds:!approach?ContactInvalidReason.WrongApproach:ContactInvalidReason.None,patch.Id,projected,n,signed,EdgeDistance(projected,patch),approach,inside);}}
            return nearest==null?Invalid(ContactInvalidReason.OutsideBounds):evidence;
        }

        public static bool TryRayImpact(SurfacePatch patch,Vector3d origin,Vector3d direction,out Vector3d impact)
        {
            impact=default;Vector3d n=patch.OutwardNormal.Normalized;double denominator=Vector3d.Dot(direction,n);
            if(Math.Abs(denominator)<1e-10)return false;double t=Vector3d.Dot(patch.BoundaryMetres[0]-origin,n)/denominator;
            if(t<0)return false;impact=origin+direction*t;return Inside(impact,patch,n);
        }

        private static ContactEvidence Invalid(ContactInvalidReason reason)=>new(ContactState.Invalid,reason,null,default,default,0,0,false,false);
        private static bool Inside(Vector3d point,SurfacePatch patch,Vector3d normal)
        {
            double sign=0;for(int i=0;i<patch.BoundaryMetres.Count;i++){Vector3d a=patch.BoundaryMetres[i],b=patch.BoundaryMetres[(i+1)%patch.BoundaryMetres.Count];double side=Vector3d.Dot(Vector3d.Cross(b-a,point-a),normal);
                if(Math.Abs(side)<1e-9)continue;if(sign==0)sign=Math.Sign(side);else if(Math.Sign(side)!=sign)return false;}return true;
        }
        private static double EdgeDistance(Vector3d point,SurfacePatch patch)
        {double best=double.PositiveInfinity;for(int i=0;i<patch.BoundaryMetres.Count;i++){Vector3d a=patch.BoundaryMetres[i],b=patch.BoundaryMetres[(i+1)%patch.BoundaryMetres.Count],d=b-a;double t=Math.Max(0,Math.Min(1,Vector3d.Dot(point-a,d)/d.LengthSquared));best=Math.Min(best,Vector3d.Distance(point,a+d*t));}return best;}
    }
}
