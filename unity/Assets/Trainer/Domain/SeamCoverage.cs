using System;
using System.Collections.Generic;
using System.Linq;

namespace WeldingTrainer.Domain
{
    public enum CoverageQuality { Acceptable, Poor }
    public enum CoverageCloseReason { None,Released,Inhibited,InvalidEvidence,Discontinuity,GenerationMismatch,ExplicitStop }
    public readonly struct CoverageInterval
    {public readonly double StartArcMetres,EndArcMetres;public readonly CoverageQuality Quality;public readonly int Visits,SegmentId;public double LengthMetres=>EndArcMetres-StartArcMetres;public CoverageInterval(double start,double end,CoverageQuality quality,int visits,int segmentId){StartArcMetres=start;EndArcMetres=end;Quality=quality;Visits=visits;SegmentId=segmentId;}}
    public sealed class CoverageSnapshot
    {public const int SchemaVersion=1;public string SeamId{get;}public long Revision{get;}public double SeamLengthMetres{get;}public IReadOnlyList<CoverageInterval> Intervals{get;}public double AttemptedLengthMetres{get;}public double AcceptableLengthMetres{get;}public double PoorLengthMetres=>AttemptedLengthMetres-AcceptableLengthMetres;public double AttemptedFraction=>SeamLengthMetres<=0?0:AttemptedLengthMetres/SeamLengthMetres;public double AcceptableFraction=>SeamLengthMetres<=0?0:AcceptableLengthMetres/SeamLengthMetres;public CoverageCloseReason LastCloseReason{get;}public CoverageSnapshot(string seam,long revision,double length,IEnumerable<CoverageInterval> intervals,CoverageCloseReason reason){SeamId=seam;Revision=revision;SeamLengthMetres=length;Intervals=Array.AsReadOnly(intervals.ToArray());AttemptedLengthMetres=Intervals.Sum(x=>x.LengthMetres);AcceptableLengthMetres=Intervals.Where(x=>x.Quality==CoverageQuality.Acceptable).Sum(x=>x.LengthMetres);LastCloseReason=reason;}}
    public sealed class SeamCoverageTracker
    {
        readonly DirectedSpline seam;readonly double maximumStep;readonly int maximumIntervals;List<CoverageInterval> intervals=new();bool anchored;double priorArc;int segmentId;long revision;CoverageCloseReason closeReason;
        public SeamCoverageTracker(DirectedSpline seam,double maximumAttributableStepMetres=.05,int maximumIntervals=4096){this.seam=seam??throw new ArgumentNullException(nameof(seam));if(maximumAttributableStepMetres<=0||maximumIntervals<1)throw new ArgumentException("Invalid coverage policy");maximumStep=maximumAttributableStepMetres;this.maximumIntervals=maximumIntervals;}
        public CoverageSnapshot Update(PathMetrics metrics,ActivationDecision activation,bool acceptable,long sampleGeneration,long requiredGeneration)
        {
            if(sampleGeneration!=requiredGeneration){Close(CoverageCloseReason.GenerationMismatch);return Snapshot();}
            if(!activation.Active){Close(activation.Requested?CoverageCloseReason.Inhibited:CoverageCloseReason.Released);return Snapshot();}
            if(metrics==null||!metrics.Valid||!metrics.Projection.Valid){Close(CoverageCloseReason.InvalidEvidence);return Snapshot();}
            if((metrics.Flags&(MotionFlags.Discontinuity|MotionFlags.SkippedRegion|MotionFlags.TrackingGap|MotionFlags.AmbiguousProjection|MotionFlags.NonMonotonicTime))!=0){Close(CoverageCloseReason.Discontinuity);return Snapshot();}
            double arc=metrics.Projection.ArcLengthMetres;if(!anchored){anchored=true;priorArc=arc;segmentId++;closeReason=CoverageCloseReason.None;return Snapshot();}
            double distance=Math.Abs(arc-priorArc);if(distance>maximumStep){Close(CoverageCloseReason.Discontinuity);anchored=true;priorArc=arc;segmentId++;return Snapshot();}
            if(distance>1e-9){ApplyInterval(Math.Min(priorArc,arc),Math.Max(priorArc,arc),acceptable&&!metrics.Flags.HasFlag(MotionFlags.Reverse)?CoverageQuality.Acceptable:CoverageQuality.Poor,segmentId);priorArc=arc;revision++;}
            return Snapshot();
        }
        public CoverageSnapshot Stop(){Close(CoverageCloseReason.ExplicitStop);return Snapshot();}
        public CoverageSnapshot Current=>Snapshot();
        public CoverageSnapshot AddAttributedInterval(double startArcMetres,double endArcMetres,CoverageQuality quality)
        {double start=Math.Max(0,Math.Min(startArcMetres,endArcMetres)),end=Math.Min(seam.LengthMetres,Math.Max(startArcMetres,endArcMetres));if(end-start<=1e-9)return Snapshot();segmentId++;ApplyInterval(start,end,quality,segmentId);revision++;closeReason=CoverageCloseReason.None;return Snapshot();}
        void Close(CoverageCloseReason reason){if(anchored){anchored=false;revision++;}closeReason=reason;}
        void ApplyInterval(double start,double end,CoverageQuality quality,int currentSegment)
        {var boundaries=new SortedSet<double>{start,end};foreach(var x in intervals){boundaries.Add(x.StartArcMetres);boundaries.Add(x.EndArcMetres);}double[] values=boundaries.ToArray();var next=new List<CoverageInterval>();for(int i=0;i<values.Length-1;i++){double a=values[i],b=values[i+1];if(b-a<=1e-10)continue;CoverageInterval? old=intervals.Where(x=>x.StartArcMetres<=a+1e-10&&x.EndArcMetres>=b-1e-10).Select(x=>(CoverageInterval?)x).FirstOrDefault();bool covered=a>=start-1e-10&&b<=end+1e-10;if(!old.HasValue&&!covered)continue;CoverageQuality q=old.HasValue?old.Value.Quality:quality;int visits=old.HasValue?old.Value.Visits:0,owner=old.HasValue?old.Value.SegmentId:currentSegment;if(covered){visits++;if(quality==CoverageQuality.Poor)q=CoverageQuality.Poor;owner=currentSegment;}AppendMerged(next,new CoverageInterval(a,b,q,visits,owner));}if(next.Count>maximumIntervals)throw new InvalidOperationException("Coverage interval limit exceeded");intervals=next;}
        static void AppendMerged(List<CoverageInterval> list,CoverageInterval value){if(list.Count>0){CoverageInterval last=list[list.Count-1];if(Math.Abs(last.EndArcMetres-value.StartArcMetres)<=1e-10&&last.Quality==value.Quality&&last.Visits==value.Visits&&last.SegmentId==value.SegmentId){list[list.Count-1]=new CoverageInterval(last.StartArcMetres,value.EndArcMetres,value.Quality,value.Visits,value.SegmentId);return;}}list.Add(value);}
        CoverageSnapshot Snapshot()=>new(seam.Id,revision,seam.LengthMetres,intervals,closeReason);
    }
}
