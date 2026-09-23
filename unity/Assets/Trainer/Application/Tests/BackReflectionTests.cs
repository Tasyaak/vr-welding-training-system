using System;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class BackReflectionTests
    {
        static ReflectionProfile Profile(double confirmation=.05,double stable=.1)=>new("training-v1",.1,.05,.2,.01,3,.1,confirmation,stable);

        [Test]
        public void NormalAndObliqueReflectionMatchAnalyticVectors()
        {ReflectionRiskResult normal=SimulatedBackReflectionEvaluator.Evaluate(Input(new Vec3(0,-1,0),new Vec3(0,1,0),new HazardSphere("head",new Vec3(2,1,0),.1)),Profile());Assert.That(normal.ReflectedDirection.X,Is.EqualTo(0).Within(1e-9));Assert.That(normal.ReflectedDirection.Y,Is.EqualTo(1).Within(1e-9));double q=Math.Sqrt(.5);ReflectionRiskResult oblique=SimulatedBackReflectionEvaluator.Evaluate(Input(new Vec3(q,-q,0),new Vec3(0,1,0),new HazardSphere("head",new Vec3(2,0,2),.1)),Profile());Assert.That(oblique.ReflectedDirection.X,Is.EqualTo(q).Within(1e-9));Assert.That(oblique.ReflectedDirection.Y,Is.EqualTo(q).Within(1e-9));}

        [Test]
        public void ConeDetectsSphereWhoseCentreIsOutsideIdealRay()
        {var target=new HazardSphere("head",new Vec3(.15,1,0),.1);ReflectionRiskResult result=SimulatedBackReflectionEvaluator.Evaluate(Input(new Vec3(0,-1,0),new Vec3(0,1,0),target),Profile());Assert.That(result.Level,Is.EqualTo(RiskState.High));Assert.That(result.ConeIntersection,Is.True);Assert.That(result.DirectIdealIntersection,Is.False);}

        [Test]
        public void BehindTargetIsLowAndApexInsideSphereIsImmediateHigh()
        {Assert.That(SimulatedBackReflectionEvaluator.Evaluate(Input(new Vec3(0,-1,0),new Vec3(0,1,0),new HazardSphere("behind",new Vec3(0,-1,0),.1)),Profile()).Level,Is.EqualTo(RiskState.Low));ReflectionRiskResult apex=SimulatedBackReflectionEvaluator.Evaluate(Input(new Vec3(0,-1,0),new Vec3(0,1,0),new HazardSphere("head",new Vec3(0,0,.01),.2)),Profile());Assert.That(apex.Level,Is.EqualTo(RiskState.High));Assert.That(apex.Severe,Is.True);}

        [TestCase(false,true,true,ReflectionReason.MissingEvidence)]
        [TestCase(true,false,true,ReflectionReason.TrackingInvalid)]
        [TestCase(true,true,false,ReflectionReason.TrackingInvalid)]
        public void MissingRequiredEvidenceIsUnknown(bool impact,bool tool,bool head,ReflectionReason expected)
        {var i=new ReflectionInput(0,0,"surface",default,new Vec3(0,-1,0),new Vec3(0,1,0),new[]{new HazardSphere("head",new Vec3(0,1,0),.1)},impact,tool,head,1,1);ReflectionRiskResult result=SimulatedBackReflectionEvaluator.Evaluate(i,Profile());Assert.That(result.Level,Is.EqualTo(RiskState.Unknown));Assert.That(result.Reasons.HasFlag(expected),Is.True);}

        [Test]
        public void ReversedOrZeroNormalAndGenerationMismatchAreUnknown()
        {var target=new HazardSphere("head",new Vec3(0,1,0),.1);Assert.That(SimulatedBackReflectionEvaluator.Evaluate(Input(new Vec3(0,-1,0),new Vec3(0,-1,0),target),Profile()).Reasons.HasFlag(ReflectionReason.WrongSurfaceSide),Is.True);var mismatch=new ReflectionInput(0,0,"surface",default,new Vec3(0,-1,0),new Vec3(0,1,0),new[]{target},true,true,true,2,1);Assert.That(SimulatedBackReflectionEvaluator.Evaluate(mismatch,Profile()).Level,Is.EqualTo(RiskState.Unknown));}

        [Test]
        public void FootprintWorstCaseCannotHideUnknownOrHighNormal()
        {var head=new HazardSphere("head",new Vec3(0,1,0),.1);ReflectionInput low=Input(new Vec3(0,-1,0),new Vec3(0,1,0),new HazardSphere("away",new Vec3(2,0,0),.1));ReflectionInput high=Input(new Vec3(0,-1,0),new Vec3(0,1,0),head);Assert.That(SimulatedBackReflectionEvaluator.EvaluateWorstCase(new[]{low,high},Profile()).Level,Is.EqualTo(RiskState.High));var unknown=new ReflectionInput(0,0,"surface",default,new Vec3(0,-1,0),default,new[]{head},true,true,true,1,1);Assert.That(SimulatedBackReflectionEvaluator.EvaluateWorstCase(new[]{high,unknown},Profile()).Level,Is.EqualTo(RiskState.Unknown));}

        [Test]
        public void AssistanceIsAbsentFromRiskAuthority()
        {var input=Input(new Vec3(0,-1,0),new Vec3(0,1,0),new HazardSphere("head",new Vec3(0,1,0),.1));ReflectionRiskResult first=SimulatedBackReflectionEvaluator.Evaluate(input,Profile());ReflectionRiskResult second=SimulatedBackReflectionEvaluator.Evaluate(input,Profile());Assert.That(second.Level,Is.EqualTo(first.Level));Assert.That(second.AngularMarginRadians,Is.EqualTo(first.AngularMarginRadians));}

        [Test]
        public void MarginalHighInhibitsImmediatelyThenLatchesByMonotonicDeadline()
        {ReflectionProfile profile=Profile();var reducer=new ReflectionFaultReducer(profile);ReflectionRiskResult marginal=At(RiskState.High,0,false);ReflectionFaultState first=reducer.Update(marginal,true);Assert.That(first.OutputInhibited,Is.True);Assert.That(first.Latched,Is.False);Assert.That(first.ConfirmationPending,Is.True);Assert.That(reducer.Update(At(RiskState.High,.049,false),true).Latched,Is.False);Assert.That(reducer.Update(At(RiskState.High,.05,false),true).Latched,Is.True);}

        [Test]
        public void SevereTripLatchesImmediatelyAndNeedsLowReleaseAcknowledge()
        {var reducer=new ReflectionFaultReducer(Profile(0,.1));ReflectionFaultState trip=reducer.Update(At(RiskState.High,0,true),true);Assert.That(trip.Latched,Is.True);ReflectionFaultState low=reducer.Update(At(RiskState.Low,.05,false),false);Assert.That(reducer.AcknowledgeAndReset(low),Is.False);low=reducer.Update(At(RiskState.Low,.15,false),false);Assert.That(reducer.AcknowledgeAndReset(low),Is.True);}

        [TestCase(.033333333)] [TestCase(.013888889)] [TestCase(.008333333)]
        public void ConfirmationIsRenderScheduleIndependent(double step)
        {var reducer=new ReflectionFaultReducer(Profile());ReflectionFaultState state=default;double t=0;while(t<.051){state=reducer.Update(At(RiskState.High,t,false),true);t+=step;}if(!state.Latched)state=reducer.Update(At(RiskState.High,.051,false),true);Assert.That(state.Latched,Is.True);}

        static ReflectionInput Input(Vec3 incident,Vec3 normal,HazardSphere target)=>new(0,0,"surface",default,incident,normal,new[]{target},true,true,true,1,1);
        static ReflectionRiskResult At(RiskState state,double time,bool severe)=>new(state,state==RiskState.High?ReflectionReason.ConeIntersection:ReflectionReason.None,"surface","profile","head",default,default,default,new Vec3(0,1,0),0,time,1,1,severe,state==RiskState.High,severe,true);
    }
}
