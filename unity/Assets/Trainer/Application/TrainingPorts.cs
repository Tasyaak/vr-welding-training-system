using WeldingTrainer.Domain;
namespace WeldingTrainer.Application
{
    public interface IMonotonicClock { double Seconds{get;} } public interface IIdSource { string NewId(); }
    public readonly struct TrainingInput { public readonly int SchemaVersion; public readonly double CapturedSeconds; public readonly long InputGeneration,OriginGeneration; public readonly bool Available,HeadValid,ToolValid,TriggerPressed,TriggerReleased,EmergencyStopPressed,SystemInvalid; public TrainingInput(double time,long inputGen,long originGen,bool available,bool head,bool tool,bool trigger,bool released,bool estop,bool invalid){SchemaVersion=1;CapturedSeconds=time;InputGeneration=inputGen;OriginGeneration=originGen;Available=available;HeadValid=head;ToolValid=tool;TriggerPressed=trigger;TriggerReleased=released;EmergencyStopPressed=estop;SystemInvalid=invalid;} }
    public readonly struct RegistrationInput { public readonly bool Available,Valid; public readonly long RegistrationGeneration,OriginGeneration; public readonly string FixtureId; public RegistrationInput(bool available,bool valid,long generation,long origin,string fixture){Available=available;Valid=valid;RegistrationGeneration=generation;OriginGeneration=origin;FixtureId=fixture;} }
    public readonly struct SafetyInput
    {
        public readonly bool Available,Allowed,FaultLatched,AssemblyConfirmed,BindingMatches,MenuOpen;
        public readonly BlockReason Reasons;
        public readonly ContactEvidence Contact;
        public readonly RiskState Risk;
        public SafetyInput(bool available,bool allowed,bool latched,BlockReason reasons)
        {
            Available=available;Allowed=allowed;FaultLatched=latched;Reasons=reasons;
            AssemblyConfirmed=available;BindingMatches=available;MenuOpen=false;
            Contact=allowed?new ContactEvidence(ContactState.Contact,ContactReason.None,"legacy",default,default,0,0,true,true):default;
            Risk=available?RiskState.Low:RiskState.Unknown;
        }
        public SafetyInput(bool available,bool latched,bool assembly,bool binding,bool menu,ContactEvidence contact,RiskState risk,BlockReason reasons)
        {
            Available=available;Allowed=contact.State==ContactState.Contact&&risk!=RiskState.Unknown&&risk!=RiskState.High;
            FaultLatched=latched;AssemblyConfirmed=assembly;BindingMatches=binding;MenuOpen=menu;Contact=contact;Risk=risk;Reasons=reasons;
        }
    }
    public interface ITrainingInputPort { TrainingInput Capture(double now); } public interface IRegistrationPort { RegistrationInput Capture(double now); void Teardown(); } public interface ISafetyPort { SafetyInput Evaluate(TrainingInput input,RegistrationInput registration,AttemptConfiguration attempt); }
    public interface IRecorderPort { bool Available{get;} void Append(ProcessEvent e); void Begin(AttemptConfiguration attempt); bool Finish(string attemptId,bool interrupted,out string error); void Teardown(); }
    public interface IAttemptDataRecorder { bool TryAppend(AttemptSample sample); long DroppedSamples{get;} }
    public interface ISnapshotSink { void Publish(ProcessSnapshot snapshot); } public interface IHapticStop { void StopImmediately(); }
    public interface IProcessStopSink { void StopAll(long activationEpoch,BlockReason reason); }
}
