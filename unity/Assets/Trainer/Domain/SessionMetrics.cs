using System;

namespace WeldingTrainer.Domain
{
    public enum RecordedSpeedClass { Invalid, TooSlow, InRange, TooFast }
    public readonly struct AttemptSample
    {
        public const int SchemaVersion=1;
        public readonly double TimeSeconds,DeltaSeconds,Progress,PositionErrorMetres,SpeedMps,TravelAngleErrorRadians,WorkAngleErrorRadians,Assistance,NewlyCoveredLengthMetres,TargetLengthMetres,NewlyCoveredAreaSquareMetres,TargetAreaSquareMetres;
        public readonly Vec3 TipPositionMetres; public readonly Quat TipRotation;
        public readonly bool PoseValid,RegistrationValid,TriggerPressed,Permission,Active,WithinPositionTolerance,ReverseMotion,Covered;
        public readonly RecordedSpeedClass SpeedClass; public readonly BlockReason BlockReasons;
        public readonly long InputGeneration,RegistrationGeneration;
        public AttemptSample(double time,double delta,Vec3 position,Quat rotation,bool poseValid,bool registrationValid,long inputGeneration,long registrationGeneration,double progress,double positionError,double speed,RecordedSpeedClass speedClass,double travelAngle,double workAngle,double assistance,bool trigger,bool permission,bool active,bool withinTolerance,bool reverse,bool covered,BlockReason reasons)
        :this(time,delta,position,rotation,poseValid,registrationValid,inputGeneration,registrationGeneration,progress,positionError,speed,speedClass,travelAngle,workAngle,assistance,trigger,permission,active,withinTolerance,reverse,covered,reasons,0,0,0,0){}
        public AttemptSample(double time,double delta,Vec3 position,Quat rotation,bool poseValid,bool registrationValid,long inputGeneration,long registrationGeneration,double progress,double positionError,double speed,RecordedSpeedClass speedClass,double travelAngle,double workAngle,double assistance,bool trigger,bool permission,bool active,bool withinTolerance,bool reverse,bool covered,BlockReason reasons,double newlyCoveredLength,double targetLength,double newlyCoveredArea,double targetArea)
        {TimeSeconds=time;DeltaSeconds=delta;TipPositionMetres=position;TipRotation=rotation;PoseValid=poseValid;RegistrationValid=registrationValid;InputGeneration=inputGeneration;RegistrationGeneration=registrationGeneration;Progress=progress;PositionErrorMetres=positionError;SpeedMps=speed;SpeedClass=speedClass;TravelAngleErrorRadians=travelAngle;WorkAngleErrorRadians=workAngle;Assistance=assistance;TriggerPressed=trigger;Permission=permission;Active=active;WithinPositionTolerance=withinTolerance;ReverseMotion=reverse;Covered=covered;BlockReasons=reasons;NewlyCoveredLengthMetres=Math.Max(0,newlyCoveredLength);TargetLengthMetres=Math.Max(0,targetLength);NewlyCoveredAreaSquareMetres=Math.Max(0,newlyCoveredArea);TargetAreaSquareMetres=Math.Max(0,targetArea);}
        public bool Valid=>PoseValid&&RegistrationValid&&InputGeneration==RegistrationGeneration&&DeltaSeconds>=0&&double.IsFinite(TimeSeconds)&&double.IsFinite(DeltaSeconds);
    }

    public readonly struct AttemptSummary
    {
        public readonly double DurationSeconds,ValidSeconds,InvalidSeconds,BlockedTriggerSeconds,MeanPositionErrorMetres,MaximumPositionErrorMetres,PositionInRangeFraction,SpeedInRangeFraction,MeanTravelAngleErrorRadians,MeanWorkAngleErrorRadians,CoverageFraction,MissedFraction,CoveredLengthMetres,MissedLengthMetres,CleaningCoverageFraction,CoveredAreaSquareMetres,MissedAreaSquareMetres,MaximumProgress;
        public readonly int ReverseCount,SampleCount; public readonly bool Completed,Interrupted,Scored;
        public AttemptSummary(double duration,double valid,double invalid,double blocked,double meanPosition,double maxPosition,double positionFraction,double speedFraction,double meanTravel,double meanWork,double coverage,double missed,double coveredLength,double missedLength,double cleaningCoverage,double coveredArea,double missedArea,double maximumProgress,int reverseCount,int sampleCount,bool completed,bool interrupted,bool scored)
        {DurationSeconds=duration;ValidSeconds=valid;InvalidSeconds=invalid;BlockedTriggerSeconds=blocked;MeanPositionErrorMetres=meanPosition;MaximumPositionErrorMetres=maxPosition;PositionInRangeFraction=positionFraction;SpeedInRangeFraction=speedFraction;MeanTravelAngleErrorRadians=meanTravel;MeanWorkAngleErrorRadians=meanWork;CoverageFraction=coverage;MissedFraction=missed;CoveredLengthMetres=coveredLength;MissedLengthMetres=missedLength;CleaningCoverageFraction=cleaningCoverage;CoveredAreaSquareMetres=coveredArea;MissedAreaSquareMetres=missedArea;MaximumProgress=maximumProgress;ReverseCount=reverseCount;SampleCount=sampleCount;Completed=completed;Interrupted=interrupted;Scored=scored;}
    }

    public sealed class AttemptMetricsAccumulator
    {
        double last=double.NaN,valid,invalid,blocked,positionWeighted,maxPosition,positionGood,speedGood,travelWeighted,workWeighted,coverageWeighted,activeWeighted,coveredLength,targetLength,coveredArea,targetArea,maxProgress; int reverses,samples;
        public void Add(AttemptSample s)
        {if(!double.IsFinite(s.TimeSeconds)||!double.IsFinite(s.DeltaSeconds)||s.DeltaSeconds<0)throw new ArgumentException("Invalid sample time");if(!double.IsNaN(last)&&s.TimeSeconds<last)throw new ArgumentException("Non-monotonic sample");last=s.TimeSeconds;samples++;maxProgress=Math.Max(maxProgress,Clamp01(s.Progress));targetLength=Math.Max(targetLength,s.TargetLengthMetres);targetArea=Math.Max(targetArea,s.TargetAreaSquareMetres);if(s.ReverseMotion)reverses++;if(!s.Valid){invalid+=s.DeltaSeconds;return;}valid+=s.DeltaSeconds;if(s.TriggerPressed&&!s.Permission)blocked+=s.DeltaSeconds;if(s.Active){activeWeighted+=s.DeltaSeconds;positionWeighted+=Math.Abs(s.PositionErrorMetres)*s.DeltaSeconds;maxPosition=Math.Max(maxPosition,Math.Abs(s.PositionErrorMetres));if(s.WithinPositionTolerance)positionGood+=s.DeltaSeconds;if(s.SpeedClass==RecordedSpeedClass.InRange)speedGood+=s.DeltaSeconds;travelWeighted+=Math.Abs(s.TravelAngleErrorRadians)*s.DeltaSeconds;workWeighted+=Math.Abs(s.WorkAngleErrorRadians)*s.DeltaSeconds;if(s.Covered)coverageWeighted+=s.DeltaSeconds;coveredLength+=s.NewlyCoveredLengthMetres;coveredArea+=s.NewlyCoveredAreaSquareMetres;}}
        public AttemptSummary Finish(bool completed,bool interrupted)
        {double duration=valid+invalid,d=Math.Max(activeWeighted,1e-12),timeCoverage=coverageWeighted/d,lengthCoverage=targetLength>0?coveredLength/targetLength:timeCoverage,areaCoverage=targetArea>0?coveredArea/targetArea:0;bool scored=!interrupted&&valid>0&&invalid<=valid;return new AttemptSummary(duration,valid,invalid,blocked,positionWeighted/d,maxPosition,positionGood/d,speedGood/d,travelWeighted/d,workWeighted/d,Clamp01(lengthCoverage),1-Clamp01(lengthCoverage),coveredLength,Math.Max(0,targetLength-coveredLength),Clamp01(areaCoverage),coveredArea,Math.Max(0,targetArea-coveredArea),maxProgress,reverses,samples,completed,interrupted,scored);}
        static double Clamp01(double v)=>!double.IsFinite(v)?0:Math.Max(0,Math.Min(1,v));
    }
}
