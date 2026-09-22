using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class WeldPathEvaluatorTests
    {
        static readonly Vec3 Up=new(0,1,0),Forward=new(0,-1,0);static PathEvaluationPolicy Policy()=>new(.05,.15,.01,.2,.25,.01,.0001);
        [Test] public void StraightProjectionReportsSeamLocalError(){var e=new WeldPathEvaluator(new DirectedSpline("s",new[]{new Vec3(0,0,0),new Vec3(1,0,0)}),Policy());PathMetrics r=e.Evaluate(S(.1,new Vec3(.25,.01,.02)),1,1);Assert.That(r.Projection.Progress,Is.EqualTo(.25).Within(1e-9));Assert.That(r.NormalErrorMetres,Is.EqualTo(.01).Within(1e-9));Assert.That(System.Math.Abs(r.LateralErrorMetres),Is.EqualTo(.02).Within(1e-9));}
        [Test] public void CurvedPolylineProjectsOntoLaterSegment(){var e=new WeldPathEvaluator(new DirectedSpline("c",new[]{new Vec3(0,0,0),new Vec3(1,0,0),new Vec3(1,0,1)}),Policy());PathMetrics r=e.Evaluate(S(.1,new Vec3(1,.01,.5)),1,1);Assert.That(r.Projection.SegmentIndex,Is.EqualTo(1));Assert.That(r.Projection.Progress,Is.EqualTo(.75).Within(1e-9));}
        [Test] public void ActualTimeProducesSpeedClasses(){var e=new WeldPathEvaluator(new DirectedSpline("s",new[]{new Vec3(0,0,0),new Vec3(1,0,0)}),Policy());e.Evaluate(S(0,new Vec3(0,0,0)),1,1);PathMetrics correct=e.Evaluate(S(.1,new Vec3(.01,0,0)),1,1);Assert.That(correct.SpeedClass,Is.EqualTo(SpeedClass.Correct));PathMetrics fast=e.Evaluate(S(.2,new Vec3(.21,0,0)),1,1);Assert.That(fast.SpeedClass,Is.EqualTo(SpeedClass.TooFast));}
        [Test] public void ReverseAndJumpAreExplicit(){var e=new WeldPathEvaluator(new DirectedSpline("s",new[]{new Vec3(0,0,0),new Vec3(1,0,0)}),Policy());e.Evaluate(S(0,new Vec3(.4,0,0)),1,1);Assert.That(e.Evaluate(S(.1,new Vec3(.3,0,0)),1,1).Flags.HasFlag(MotionFlags.Reverse),Is.True);e.Reset();e.Evaluate(S(0,new Vec3(0,0,0)),1,1);Assert.That(e.Evaluate(S(.1,new Vec3(.5,0,0)),1,1).Flags.HasFlag(MotionFlags.Discontinuity),Is.True);}
        [Test] public void GapResetsVelocityHistory(){var e=new WeldPathEvaluator(new DirectedSpline("s",new[]{new Vec3(0,0,0),new Vec3(1,0,0)}),Policy());e.Evaluate(S(0,new Vec3(0,0,0)),1,1);PathMetrics gap=e.Evaluate(S(1,new Vec3(.1,0,0)),1,1);Assert.That(gap.SpeedValid,Is.False);Assert.That(gap.Valid,Is.False);Assert.That(gap.Flags.HasFlag(MotionFlags.TrackingGap),Is.True);}
        [Test] public void GenerationMismatchCannotScore(){var e=new WeldPathEvaluator(new DirectedSpline("s",new[]{new Vec3(0,0,0),new Vec3(1,0,0)}),Policy());PathMetrics r=e.Evaluate(S(0,new Vec3(.1,0,0),registration:2),1,1);Assert.That(r.Valid,Is.False);Assert.That(r.Flags.HasFlag(MotionFlags.GenerationMismatch),Is.True);}
        [Test] public void TravelAngleInvalidAtRestAndValidDuringMotion(){var e=new WeldPathEvaluator(new DirectedSpline("s",new[]{new Vec3(0,0,0),new Vec3(1,0,0)}),Policy());e.Evaluate(S(0,new Vec3(0,0,0)),1,1);Assert.That(e.Evaluate(S(.1,new Vec3(0,0,0)),1,1).TravelAngleValid,Is.False);Assert.That(e.Evaluate(S(.2,new Vec3(.01,0,.01)),1,1).TravelAngleValid,Is.True);}
        static PathSample S(double t,Vec3 p,long registration=1)=>new(t,p,Forward,Up,true,registration,1);
    }
}
