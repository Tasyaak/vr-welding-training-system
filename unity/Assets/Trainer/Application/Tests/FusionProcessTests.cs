using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class FusionProcessTests
    {
        static readonly ActivationDecision Active=new(ActivationState.Active,BlockReason.None,true),Blocked=new(ActivationState.Inhibited,BlockReason.ContactInvalid,true),Released=new(ActivationState.Armed,BlockReason.None,false);
        [Test]
        public void ContinuousActiveFusionProducesEpochTaggedCoverage()
        {var kernel=Kernel();kernel.Update(I(0,0,Active));FusionResult result=kernel.Update(I(.2,.2,Active));Assert.That(result.OutputOn,Is.True);Assert.That(result.Delta.Valid,Is.True);Assert.That(result.Delta.Mode,Is.EqualTo(ProcessMode.Fusion));Assert.That(result.Delta.ActivationEpoch,Is.EqualTo(1));Assert.That(result.Coverage.AttemptedLengthMetres,Is.EqualTo(.2).Within(1e-9));}

        [Test]
        public void BlockedTriggerAndReleaseCreateNoCoverageOrBridge()
        {var kernel=Kernel();kernel.Update(I(0,0,Blocked));FusionResult blocked=kernel.Update(I(.2,.2,Blocked));Assert.That(blocked.Coverage.AttemptedLengthMetres,Is.Zero);Assert.That(blocked.Metrics.BlockedTriggerSeconds,Is.EqualTo(.2));kernel.Update(I(.3,.2,Released));kernel.Update(I(.4,.5,Active));FusionResult resumed=kernel.Update(I(.6,.7,Active));Assert.That(resumed.Coverage.Intervals.Count,Is.EqualTo(1));Assert.That(resumed.Coverage.Intervals[0].StartArcMetres,Is.EqualTo(.5));}

        [Test]
        public void PoorAttributableIntervalRemainsDistinct()
        {var kernel=Kernel();kernel.Update(I(0,0,Active));FusionResult poor=kernel.Update(I(.2,.2,Active,lateral:.03));Assert.That(poor.OutputOn,Is.True);Assert.That(poor.Acceptable,Is.False);Assert.That(poor.QualityFlags.HasFlag(FusionQualityFlags.Position),Is.True);Assert.That(poor.Coverage.PoorLengthMetres,Is.EqualTo(.2).Within(1e-9));}

        [Test]
        public void TouchingEndWithoutContiguousCoverageDoesNotComplete()
        {var kernel=Kernel();kernel.Update(I(0,0,Active));kernel.Update(I(.2,.2,Active));kernel.Update(I(.3,.2,Released));kernel.Update(I(.4,.8,Active));FusionResult end=kernel.Update(I(.6,1,Active));Assert.That(end.Complete,Is.False);Assert.That(end.Coverage.AttemptedLengthMetres,Is.LessThan(.5));}

        [Test]
        public void ContiguousAcceptableCoverageAndEndGateComplete()
        {var kernel=Kernel();FusionResult result=null;for(int i=0;i<=5;i++)result=kernel.Update(I(i*.2,i*.2,Active));Assert.That(result.Complete,Is.True);Assert.That(result.Coverage.AcceptableLengthMetres,Is.EqualTo(1).Within(1e-9));}

        [Test]
        public void RawErrorsRemainInTimeWeightedMetrics()
        {var kernel=Kernel();kernel.Update(I(0,0,Active));kernel.Update(I(.1,.1,Active,lateral:.01,normal:.002,speed:.08,travel:.02,work:.03));FusionMetrics m=kernel.Update(I(.3,.3,Active,lateral:.03,normal:.006,speed:.16,travel:.06,work:.09)).Metrics;Assert.That(m.MeanAbsoluteLateralErrorMetres,Is.EqualTo((.01*.1+.03*.2)/.3).Within(1e-9));Assert.That(m.MeanAbsoluteNormalErrorMetres,Is.EqualTo((.002*.1+.006*.2)/.3).Within(1e-9));Assert.That(m.MeanAbsoluteTravelAngleErrorRadians,Is.GreaterThan(0));}

        [TestCase(30)] [TestCase(72)] [TestCase(120)]
        public void PresentationRateDoesNotChangeTimestampedFusionEvidence(int presentationHz)
        {FusionResult result=RunGolden(presentationHz);Assert.That(result.Coverage.AttemptedLengthMetres,Is.EqualTo(.8).Within(1e-9));Assert.That(result.Metrics.ActiveSeconds,Is.EqualTo(.8).Within(1e-9));}

        [Test]
        public void StartInMiddleIsBlockedByFrozenStartGate()
        {FusionResult result=Kernel().Update(I(0,.5,Active));Assert.That(result.OutputOn,Is.False);Assert.That(result.BlockReasons.HasFlag(FusionBlockReason.StartGate),Is.True);}

        static FusionResult RunGolden(int presentationHz){var kernel=Kernel();double presentationStep=1d/presentationHz,nextPresentation=0;FusionResult result=null;for(int i=0;i<=4;i++){double time=i*.2;while(nextPresentation<time)nextPresentation+=presentationStep;result=kernel.Update(I(time,i*.2,Active));}return result;}
        static FusionProcessKernel Kernel(){var seam=new DirectedSpline("seam",new[]{new Vec3(0,0,0),new Vec3(1,0,0)});return new FusionProcessKernel("attempt",seam,new FusionProfile("fusion",1,"hash","seam",.02,.01,.05,.15,.2,.2,.03,.03,.3,.25,.001,.004));}
        static FusionInput I(double time,double arc,ActivationDecision activation,double lateral=0,double normal=0,double speed=.1,double travel=.05,double work=.05){var p=new SeamProjection(true,false,0,arc,arc,new Vec3(arc,0,0),new Vec3(1,0,0));var metrics=new PathMetrics(true,p,new Vec3(0,lateral,normal),0,lateral,normal,true,speed,speed,SpeedClass.Correct,true,travel,true,work,MotionFlags.None);return new FusionInput(time,metrics,activation,1,1);}
    }
}
