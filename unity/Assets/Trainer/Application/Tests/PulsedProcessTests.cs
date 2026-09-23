using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class PulsedProcessTests
    {
        static readonly ActivationDecision Active=new(ActivationState.Active,BlockReason.None,true),Released=new(ActivationState.Armed,BlockReason.None,false),Stopped=new(ActivationState.ResetRequired,BlockReason.EmergencyStop,true);
        [Test]
        public void ExactOnDurationAcrossFractionalPeriodsIsAnalytic()
        {var kernel=Kernel(.1,.25);kernel.Update(I(0,0,Active));PulseResult result=kernel.Update(I(.35,.35,Active));Assert.That(result.ActiveDurationSeconds,Is.EqualTo(.1).Within(1e-10));Assert.That(result.Windows.Count,Is.EqualTo(4));}

        [Test]
        public void HalfOpenBoundariesAreUnambiguous()
        {var kernel=Kernel(.1,.25);Assert.That(kernel.Update(I(0,0,Active)).OutputOnAtSample,Is.True);Assert.That(kernel.Update(I(.025,.025,Active)).OutputOnAtSample,Is.False);Assert.That(kernel.Update(I(.1,.1,Active)).OutputOnAtSample,Is.True);}

        [Test]
        public void OffTravelLeavesGapsUnlessSpotsOverlap()
        {var kernel=Kernel(.1,.2,spot:.002);kernel.Update(I(0,0,Active));PulseResult result=kernel.Update(I(.3,.3,Active));Assert.That(result.Coverage.Intervals.Count,Is.GreaterThan(1));Assert.That(result.Coverage.AttemptedLengthMetres,Is.LessThan(.1));}

        [Test]
        public void ReleaseTruncatesCurrentWindowAndCancelsFuturePulses()
        {var kernel=Kernel(.1,.5);kernel.Update(I(0,0,Active));PulseResult release=kernel.Update(I(.015,.015,Released));Assert.That(release.ActiveDurationSeconds,Is.EqualTo(.015).Within(1e-10));Assert.That(release.Windows.Count,Is.EqualTo(1));PulseResult later=kernel.Update(I(.2,.2,Stopped));Assert.That(later.Windows,Is.Empty);Assert.That(later.OutputOnAtSample,Is.False);}

        [Test]
        public void TrackingLossDoesNotInterpolateOrScheduleCoverage()
        {var kernel=Kernel(.1,.5);kernel.Update(I(0,0,Active));PulseResult lost=kernel.Update(I(.04,.04,Active,valid:false));Assert.That(lost.Windows,Is.Empty);Assert.That(lost.Coverage.AttemptedLengthMetres,Is.Zero);}

        [Test]
        public void NewActivationEpochRestartsPhaseAndIsReplayable()
        {var kernel=Kernel(.1,.25);kernel.Update(I(0,0,Active));kernel.Update(I(.03,.03,Released));PulseResult restart=kernel.Update(I(.2,.2,Active));Assert.That(restart.Epoch,Is.EqualTo(2));Assert.That(restart.EpochStartSeconds,Is.EqualTo(.2));Assert.That(restart.OutputOnAtSample,Is.True);Assert.That(restart.PeriodSeconds,Is.EqualTo(.1));Assert.That(restart.DutyFraction,Is.EqualTo(.25));}

        [Test]
        public void OverlappingStationaryFootprintsCountUniqueCoverageOnce()
        {var kernel=Kernel(.05,.5,spot:.01);kernel.Update(I(0,.5,Active));PulseResult result=kernel.Update(I(.2,.5,Active));Assert.That(result.Coverage.AttemptedLengthMetres,Is.EqualTo(.01).Within(1e-9));Assert.That(result.Coverage.Intervals.Count,Is.GreaterThanOrEqualTo(1));}

        [TestCase(30)] [TestCase(72)] [TestCase(120)]
        public void RenderRateCannotChangeWindowsDurationOrCoverage(int hz)
        {var kernel=Kernel(.02,.3);double render=0,step=1d/hz;kernel.Update(I(0,0,Active));while(render<.4)render+=step;PulseResult result=kernel.Update(I(.4,.4,Active));Assert.That(result.ActiveDurationSeconds,Is.EqualTo(.12).Within(1e-9));Assert.That(result.Windows.Count,Is.EqualTo(20));}

        [Test]
        public void DutyOneReducesToContinuousIntersection()
        {var kernel=Kernel(.1,1);kernel.Update(I(0,0,Active));PulseResult result=kernel.Update(I(.3,.3,Active));Assert.That(result.ActiveDurationSeconds,Is.EqualTo(.3).Within(1e-9));}

        static PulsedProcessKernel Kernel(double period,double duty,double spot=.001){var seam=new DirectedSpline("seam",new[]{new Vec3(0,0,0),new Vec3(1,0,0)});return new PulsedProcessKernel(seam,new PulseProfile("pulse",1,"hash","seam",period,duty,spot,.5));}
        static PulseInput I(double time,double arc,ActivationDecision activation,bool valid=true){var projection=new SeamProjection(valid,false,0,arc,arc,new Vec3(arc,0,0),new Vec3(1,0,0));var path=new PathMetrics(valid,projection,default,0,0,0,valid,.1,.1,valid?SpeedClass.Correct:SpeedClass.Invalid,valid,0,valid,0,valid?MotionFlags.None:MotionFlags.TrackingGap);return new PulseInput(time,path,activation,true,1,1);}
    }
}
