using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class DeterministicReplayTests
    {
        [Test] public void IdenticalEvidenceProducesIdenticalSummary()
        {ReplayRecord[] records={Record(1,0),Record(2,.1)};ReplayResult a=new DeterministicReplay().Run(records),b=new DeterministicReplay().Run(records);Assert.That(a.Complete,Is.True);Assert.That(a.Summary.DurationSeconds,Is.EqualTo(b.Summary.DurationSeconds));Assert.That(a.Summary.CoverageFraction,Is.EqualTo(b.Summary.CoverageFraction));}
        [Test] public void RejectsOutputAfterEmergencyStop()
        {ReplayRecord invalid=new ReplayRecord(1,1,ProcessMode.Fusion,Sample(0,true),false,true,false);ReplayResult result=new DeterministicReplay().Run(new[]{invalid});Assert.That(result.Complete,Is.False);Assert.That(result.Error,Does.Contain("inhibited"));}
        [Test] public void ValidatesPulseWindowsBetweenDisplaySamples()
        {var sample=Sample(.075,true);var record=new ReplayRecord(1,1,ProcessMode.Pulsed,sample,false,false,false,0,.1,.5);Assert.That(new DeterministicReplay().Run(new[]{record}).Complete,Is.True);var wrong=new ReplayRecord(1,1,ProcessMode.Pulsed,sample,true,false,false,0,.1,.5);Assert.That(new DeterministicReplay().Run(new[]{wrong}).Complete,Is.False);}
        static ReplayRecord Record(long sequence,double time)=>new(sequence,1,ProcessMode.Fusion,Sample(time,true),false,false,false);
        static AttemptSample Sample(double time,bool active)=>new(time,.1,new Vec3(0,0,0),new Quat(0,0,0,1),true,true,1,1,time,0,.1,RecordedSpeedClass.InRange,0,0,0,true,true,active,true,false,true,BlockReason.None,.1,1,0,0);
    }
}
