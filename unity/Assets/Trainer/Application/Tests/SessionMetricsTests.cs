using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class SessionMetricsTests
    {
        [Test]
        public void MetricsAreTimeWeightedAndKeepWeldLengthSeparateFromCleaningArea()
        {var metrics=new AttemptMetricsAccumulator();metrics.Add(Sample(0,1,true,.1,1,.01,2));metrics.Add(Sample(1,3,true,.2,1,.03,2));AttemptSummary result=metrics.Finish(true,false);Assert.That(result.DurationSeconds,Is.EqualTo(4));Assert.That(result.MeanPositionErrorMetres,Is.EqualTo(.005).Within(1e-9));Assert.That(result.CoveredLengthMetres,Is.EqualTo(.3).Within(1e-9));Assert.That(result.CoverageFraction,Is.EqualTo(.3).Within(1e-9));Assert.That(result.CleaningCoverageFraction,Is.EqualTo(.02).Within(1e-9));}

        [Test]
        public void InvalidGenerationAccumulatesInvalidTimeAndCannotScore()
        {var metrics=new AttemptMetricsAccumulator();metrics.Add(new AttemptSample(0,2,default,default,true,true,2,1,0,0,0,RecordedSpeedClass.Invalid,0,0,0,false,false,false,false,false,false,BlockReason.OriginMismatch));AttemptSummary result=metrics.Finish(false,false);Assert.That(result.InvalidSeconds,Is.EqualTo(2));Assert.That(result.Scored,Is.False);}

        [Test]
        public void BlockedTriggerAndReverseAreExplicit()
        {var metrics=new AttemptMetricsAccumulator();metrics.Add(new AttemptSample(0,.5,default,default,true,true,1,1,.2,.001,.1,RecordedSpeedClass.InRange,0,0,0,true,false,false,true,true,false,BlockReason.ClampDisconnected));AttemptSummary result=metrics.Finish(false,false);Assert.That(result.BlockedTriggerSeconds,Is.EqualTo(.5));Assert.That(result.ReverseCount,Is.EqualTo(1));}

        static AttemptSample Sample(double time,double delta,bool valid,double newLength,double targetLength,double newArea,double targetArea)=>
            new(time,delta,default,default,valid,valid,1,1,.5,.002,.1,RecordedSpeedClass.InRange,.01,.02,.5,true,true,true,true,false,true,BlockReason.None,newLength,targetLength,newArea,targetArea);
    }
}
