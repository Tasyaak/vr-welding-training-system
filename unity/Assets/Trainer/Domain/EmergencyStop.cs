using System;
namespace WeldingTrainer.Domain
{
    [Flags] public enum ResetRejection { None=0,BReleaseRequired=1,TriggerReleaseRequired=2,RegistrationInvalid=4,HeadInvalid=8,ToolInvalid=16,SystemInvalid=32,ClampInvalid=64,ContactInvalid=128,NozzleInvalid=256,ReflectionNotResetEligible=512,RecorderInvalid=1024 }
    public readonly struct EStopRecoveryInput {public readonly bool RegistrationValid,HeadValid,ToolValid,SystemValid,ClampValid,ContactValid,NozzleValid,ReflectionResetEligible,RecorderValid;public EStopRecoveryInput(bool registration,bool head,bool tool,bool system,bool clamp,bool contact,bool nozzle,bool reflection,bool recorder){RegistrationValid=registration;HeadValid=head;ToolValid=tool;SystemValid=system;ClampValid=clamp;ContactValid=contact;NozzleValid=nozzle;ReflectionResetEligible=reflection;RecorderValid=recorder;}}
    public readonly struct EStopSnapshot {public readonly bool Latched,BReleased,TriggerReleased;public readonly long StopSequence,ActivationEpoch;public readonly double LatchedAtSeconds;public readonly ResetRejection ResetRejected;public readonly string FaultId;public EStopSnapshot(bool latched,bool bReleased,bool triggerReleased,long stopSequence,long epoch,double at,ResetRejection rejected,string faultId){Latched=latched;BReleased=bReleased;TriggerReleased=triggerReleased;StopSequence=stopSequence;ActivationEpoch=epoch;LatchedAtSeconds=at;ResetRejected=rejected;FaultId=faultId;}}
    public sealed class EmergencyStopReducer
    {
        bool latched,bReleased=true,triggerReleased=true;long stops,epoch;double latchedAt=double.NaN,lastTime=double.NegativeInfinity;string faultId;
        public EStopSnapshot Press(double time,bool triggerPressed){Time(time);if(!latched){latched=true;latchedAt=time;faultId="estop-"+(++stops).ToString();}bReleased=false;if(triggerPressed)triggerReleased=false;return State();}
        public EStopSnapshot ObserveBRelease(double time){Time(time);bReleased=true;return State();}
        public EStopSnapshot ObserveTrigger(bool pressed,double time){Time(time);if(!pressed)triggerReleased=true;else if(latched)triggerReleased=false;return State();}
        public EStopSnapshot TryReset(EStopRecoveryInput input,double time){Time(time);ResetRejection rejected=Reasons(input);if(!latched)return State();if(rejected!=ResetRejection.None)return State(rejected);latched=false;faultId=null;epoch++;return State();}
        public long BeginActivationEpoch(double time){Time(time);if(latched)throw new InvalidOperationException("E-stop latched");return ++epoch;}
        public EStopSnapshot Current=>State();
        ResetRejection Reasons(EStopRecoveryInput i){ResetRejection r=ResetRejection.None;if(!bReleased)r|=ResetRejection.BReleaseRequired;if(!triggerReleased)r|=ResetRejection.TriggerReleaseRequired;if(!i.RegistrationValid)r|=ResetRejection.RegistrationInvalid;if(!i.HeadValid)r|=ResetRejection.HeadInvalid;if(!i.ToolValid)r|=ResetRejection.ToolInvalid;if(!i.SystemValid)r|=ResetRejection.SystemInvalid;if(!i.ClampValid)r|=ResetRejection.ClampInvalid;if(!i.ContactValid)r|=ResetRejection.ContactInvalid;if(!i.NozzleValid)r|=ResetRejection.NozzleInvalid;if(!i.ReflectionResetEligible)r|=ResetRejection.ReflectionNotResetEligible;if(!i.RecorderValid)r|=ResetRejection.RecorderInvalid;return r;}
        void Time(double time){if(!double.IsFinite(time)||time<lastTime)throw new ArgumentException("E-stop time must be finite and monotonic");lastTime=time;}
        EStopSnapshot State(ResetRejection rejected=ResetRejection.None)=>new(latched,bReleased,triggerReleased,stops,epoch,latchedAt,rejected,faultId);
    }
}
