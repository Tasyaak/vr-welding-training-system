using System.Linq;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class SeamCoverageTests
    {
        DirectedSpline seam;SeamCoverageTracker tracker;static readonly ActivationDecision Active=new(ActivationState.Active,BlockReason.None,true),Released=new(ActivationState.Armed,BlockReason.None,false),Inhibited=new(ActivationState.Inhibited,BlockReason.ContactInvalid,true);
        [SetUp] public void Setup(){seam=new DirectedSpline("seam",new[]{new Vec3(0,0,0),new Vec3(1,0,0)});tracker=new SeamCoverageTracker(seam,.3);}

        [Test]
        public void OnlyContiguousActiveTraversalCreatesCoverage()
        {tracker.Update(M(.1),Active,true,1,1);CoverageSnapshot result=tracker.Update(M(.3),Active,true,1,1);Assert.That(result.AttemptedLengthMetres,Is.EqualTo(.2).Within(1e-9));Assert.That(result.AcceptableLengthMetres,Is.EqualTo(.2).Within(1e-9));}

        [Test]
        public void ReleaseAndRestartPreserveVisibleHole()
        {tracker.Update(M(0),Active,true,1,1);tracker.Update(M(.2),Active,true,1,1);tracker.Update(M(.2),Released,true,1,1);tracker.Update(M(.5),Active,true,1,1);CoverageSnapshot result=tracker.Update(M(.7),Active,true,1,1);Assert.That(result.Intervals.Count,Is.EqualTo(2));Assert.That(result.Intervals[0].EndArcMetres,Is.EqualTo(.2));Assert.That(result.Intervals[1].StartArcMetres,Is.EqualTo(.5));}

        [Test]
        public void PoorRevisitCannotEraseEarlierPoorEvidence()
        {tracker.Update(M(0),Active,false,1,1);tracker.Update(M(.2),Active,false,1,1);tracker.Update(M(.2),Released,true,1,1);tracker.Update(M(0),Active,true,1,1);CoverageSnapshot result=tracker.Update(M(.2),Active,true,1,1);Assert.That(result.Intervals.All(x=>x.Quality==CoverageQuality.Poor),Is.True);Assert.That(result.AcceptableLengthMetres,Is.Zero);Assert.That(result.Intervals.All(x=>x.Visits==2),Is.True);}

        [Test]
        public void InvalidGenerationAndInhibitionCloseWithoutCoverage()
        {tracker.Update(M(.1),Active,true,1,1);CoverageSnapshot mismatch=tracker.Update(M(.2),Active,true,2,1);Assert.That(mismatch.AttemptedLengthMetres,Is.Zero);Assert.That(mismatch.LastCloseReason,Is.EqualTo(CoverageCloseReason.GenerationMismatch));tracker.Update(M(.2),Active,true,1,1);CoverageSnapshot blocked=tracker.Update(M(.3),Inhibited,true,1,1);Assert.That(blocked.AttemptedLengthMetres,Is.Zero);Assert.That(blocked.LastCloseReason,Is.EqualTo(CoverageCloseReason.Inhibited));}

        [Test]
        public void DiscontinuityNeverBackfillsSkippedRegion()
        {tracker.Update(M(.1),Active,true,1,1);CoverageSnapshot result=tracker.Update(M(.8,MotionFlags.Discontinuity|MotionFlags.SkippedRegion),Active,true,1,1);Assert.That(result.AttemptedLengthMetres,Is.Zero);Assert.That(result.LastCloseReason,Is.EqualTo(CoverageCloseReason.Discontinuity));}

        [Test]
        public void WorkpieceLocalExtractionPreservesCurvedVertices()
        {var curved=new DirectedSpline("curve",new[]{new Vec3(0,0,0),new Vec3(1,0,0),new Vec3(1,0,1)});var points=curved.Extract(.5,1.5);Assert.That(points.Count,Is.EqualTo(3));Assert.That(points[1].X,Is.EqualTo(1));Assert.That(points[1].Z,Is.EqualTo(0));}

        static PathMetrics M(double arc,MotionFlags flags=MotionFlags.None){var projection=new SeamProjection(true,false,0,arc,arc,new Vec3(arc,0,0),new Vec3(1,0,0));return new PathMetrics((flags&(MotionFlags.Discontinuity|MotionFlags.SkippedRegion))==0,projection,default,0,0,0,true,.1,.1,SpeedClass.Correct,true,0,true,0,flags);}
    }
}
