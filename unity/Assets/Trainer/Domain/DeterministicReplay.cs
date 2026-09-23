using System;
using System.Collections.Generic;

namespace WeldingTrainer.Domain
{
    public readonly struct ReplayRecord
    {
        public const int SchemaVersion=1;
        public readonly long Sequence,ActivationEpoch;
        public readonly ProcessMode Mode;
        public readonly AttemptSample Sample;
        public readonly bool PulseOutput,EmergencyStopLatched,ReflectionFault;
        public readonly double PulseT0Seconds,PulsePeriodSeconds,PulseDuty,WobblePhaseRadians;
        public ReplayRecord(long sequence,long epoch,ProcessMode mode,AttemptSample sample,bool pulseOutput,bool emergencyStop,bool reflectionFault,double pulseT0=0,double pulsePeriod=0,double pulseDuty=0,double wobblePhase=0)
        {Sequence=sequence;ActivationEpoch=epoch;Mode=mode;Sample=sample;PulseOutput=pulseOutput;EmergencyStopLatched=emergencyStop;ReflectionFault=reflectionFault;PulseT0Seconds=pulseT0;PulsePeriodSeconds=pulsePeriod;PulseDuty=pulseDuty;WobblePhaseRadians=wobblePhase;}
    }

    public readonly struct ReplayResult
    {
        public readonly AttemptSummary Summary; public readonly bool Complete; public readonly string Error; public readonly long LastSequence;
        public ReplayResult(AttemptSummary summary,bool complete,string error,long lastSequence){Summary=summary;Complete=complete;Error=error??string.Empty;LastSequence=lastSequence;}
    }

    /// <summary>Authoritative replay invariant checker. File parsing remains an infrastructure concern.</summary>
    public sealed class DeterministicReplay
    {
        readonly double tolerance;
        public DeterministicReplay(double numericTolerance=1e-9){if(!double.IsFinite(numericTolerance)||numericTolerance<0)throw new ArgumentOutOfRangeException(nameof(numericTolerance));tolerance=numericTolerance;}
        public ReplayResult Run(IEnumerable<ReplayRecord> records)
        {
            if(records==null)throw new ArgumentNullException(nameof(records));var metrics=new AttemptMetricsAccumulator();long sequence=0,epoch=0;double time=double.NegativeInfinity;bool any=false;
            foreach(ReplayRecord record in records)
            {
                any=true;if(record.Sequence<=sequence)return Fail(metrics,"Non-increasing replay sequence",sequence);if(record.Sample.TimeSeconds+tolerance<time)return Fail(metrics,"Non-monotonic replay time",sequence);if(record.ActivationEpoch<epoch)return Fail(metrics,"Activation epoch moved backwards",sequence);
                if(record.Sample.Active&&(record.EmergencyStopLatched||record.ReflectionFault||!record.Sample.Permission))return Fail(metrics,"Output exists while inhibited",record.Sequence);
                if(record.Mode==ProcessMode.Pulsed&&record.Sample.Active)
                {if(record.PulsePeriodSeconds<=0||record.PulseDuty<=0||record.PulseDuty>1)return Fail(metrics,"Invalid pulse timing evidence",record.Sequence);double phase=(record.Sample.TimeSeconds-record.PulseT0Seconds)%record.PulsePeriodSeconds;if(phase<0)phase+=record.PulsePeriodSeconds;bool expected=phase<record.PulsePeriodSeconds*record.PulseDuty;if(expected!=record.PulseOutput)return Fail(metrics,"Pulse window mismatch",record.Sequence);}
                if(record.Mode==ProcessMode.Wobble&&(!double.IsFinite(record.WobblePhaseRadians)))return Fail(metrics,"Invalid wobble phase",record.Sequence);
                metrics.Add(record.Sample);sequence=record.Sequence;epoch=record.ActivationEpoch;time=record.Sample.TimeSeconds;
            }
            AttemptSummary summary=metrics.Finish(any,false);return new ReplayResult(summary,any,any?"":"No replay records",sequence);
        }
        static ReplayResult Fail(AttemptMetricsAccumulator metrics,string error,long sequence)=>new(metrics.Finish(false,true),false,error,sequence);
    }
}
