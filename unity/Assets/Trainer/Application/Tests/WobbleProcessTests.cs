using System;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class WobbleProcessTests
    {
        static readonly ActivationDecision Active=new(ActivationState.Active,BlockReason.None,true),Blocked=new(ActivationState.ResetRequired,BlockReason.EmergencyStop,true);
        [TestCase(0)] [TestCase(.03125)] [TestCase(.125)] [TestCase(.251)]
        public void PhaseIsAnalyticFromEpochTime(double time)
        {var kernel=Kernel(frequency:4);kernel.Update(I(1,0,Active));WobbleResult result=kernel.Update(I(1+time,.1,Active));double expected=(2*Math.PI*4*time)%(2*Math.PI);Assert.That(result.PhaseRadians,Is.EqualTo(expected).Within(1e-9));}

        [Test]
        public void PartialCycleSweepsOnlyReachedSubset()
        {var kernel=Kernel(amplitude:.01,frequency:1,spot:.002);kernel.Update(I(0,0,Active));WobbleResult result=kernel.Update(I(.125,.1,Active));Assert.That(result.Delta.LateralMinimumMetres,Is.EqualTo(-.001).Within(1e-9));Assert.That(result.Delta.LateralMaximumMetres,Is.EqualTo(.01*Math.Sin(Math.PI/4)+.001).Within(1e-9));Assert.That(result.Delta.LateralMaximumMetres,Is.LessThan(.011));}

        [Test]
        public void HighFrequencyUsesFullEnvelopeWithoutCycleLoopOrFpsCap()
        {var kernel=Kernel(amplitude:.01,frequency:5000,spot:.002);kernel.Update(I(0,0,Active));WobbleResult result=kernel.Update(I(.01,.1,Active));Assert.That(result.Delta.LateralMinimumMetres,Is.EqualTo(-.011).Within(1e-9));Assert.That(result.Delta.LateralMaximumMetres,Is.EqualTo(.011).Within(1e-9));}

        [Test]
        public void WidthNeverChangesActualHandCentreError()
        {var narrow=Kernel(amplitude:0),wide=Kernel(amplitude:.02);narrow.Update(I(0,0,Active,lateralError:.007));wide.Update(I(0,0,Active,lateralError:.007));Assert.That(narrow.Update(I(.1,.1,Active,lateralError:.007)).ActualCentreLateralErrorMetres,Is.EqualTo(.007));Assert.That(wide.Update(I(.1,.1,Active,lateralError:.007)).ActualCentreLateralErrorMetres,Is.EqualTo(.007));}

        [Test]
        public void FiniteTargetBoundsClipSweptCoverage()
        {var kernel=Kernel(amplitude:.1,frequency:10,spot:.02,lateralMin:-.015,lateralMax:.015);kernel.Update(I(0,0,Active));WobbleResult result=kernel.Update(I(.1,.1,Active));Assert.That(result.Delta.LateralMinimumMetres,Is.EqualTo(-.015));Assert.That(result.Delta.LateralMaximumMetres,Is.EqualTo(.015));Assert.That(result.Coverage.AttemptedAreaSquareMetres,Is.LessThanOrEqualTo(result.Coverage.TargetAreaSquareMetres));}

        [Test]
        public void InhibitionClosesEpochAndCreatesNoDelayedCoverage()
        {var kernel=Kernel();kernel.Update(I(0,0,Active));WobbleResult stopped=kernel.Update(I(.1,.1,Blocked));Assert.That(stopped.OutputOn,Is.False);Assert.That(stopped.Delta.Valid,Is.False);WobbleResult restarted=kernel.Update(I(.2,.5,Active));Assert.That(restarted.Delta.Valid,Is.False);Assert.That(restarted.PhaseRadians,Is.EqualTo(0).Within(1e-9));WobbleResult next=kernel.Update(I(.3,.6,Active));Assert.That(next.Delta.Epoch,Is.EqualTo(2));}

        [TestCase(30)] [TestCase(72)] [TestCase(120)]
        public void PresentationScheduleCannotChangePhaseOrCoverage(int hz)
        {var kernel=Kernel(frequency:37);double render=0,step=1d/hz;WobbleResult result=null;for(int i=0;i<=4;i++){double t=i*.1;while(render<t)render+=step;result=kernel.Update(I(t,i*.1,Active));}Assert.That(result.PhaseRadians,Is.EqualTo((2*Math.PI*37*.4)%(2*Math.PI)).Within(1e-8));Assert.That(result.Coverage.AttemptedAreaSquareMetres,Is.GreaterThan(0));}

        [Test]
        public void DegenerateBasisFailsClosed()
        {var kernel=Kernel();WobbleResult result=kernel.Update(I(0,0,Active,normal:new Vec3(1,0,0)));Assert.That(result.OutputOn,Is.False);}

        static WobbleProcessKernel Kernel(double amplitude=.01,double frequency=10,double spot=.002,double lateralMin=-.05,double lateralMax=.05){var seam=new DirectedSpline("seam",new[]{new Vec3(0,0,0),new Vec3(1,0,0)});return new WobbleProcessKernel(seam,new WobbleProfile("wobble",1,"hash","seam",amplitude,frequency,0,spot,lateralMin,lateralMax,.001,.2));}
        static WobbleInput I(double time,double arc,ActivationDecision activation,double lateralError=.003,Vec3? normal=null){var projection=new SeamProjection(true,false,0,arc,arc,new Vec3(arc,0,0),new Vec3(1,0,0));var path=new PathMetrics(true,projection,new Vec3(0,lateralError,0),0,lateralError,0,true,.1,.1,SpeedClass.Correct,true,0,true,0,MotionFlags.None);return new WobbleInput(time,path,normal??new Vec3(0,1,0),activation,true,1,1);}
    }
}
