using System;
using System.Collections.Generic;
using System.Linq;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum TrainingCommandType { EmergencyStop, EmergencyStopReleased, Abort, FinishSession, FinishAttempt, Pause, ResetEmergencyStop, Disarm, DisconnectClamp, ConnectClamp, SelectWeldingNozzle, SelectCleaningNozzle, Arm, Resume }
    public readonly struct TrainingCommand { public readonly TrainingCommandType Type; public readonly double Time; public readonly long Sequence; public TrainingCommand(TrainingCommandType type,double time,long sequence){Type=type;Time=time;Sequence=sequence;} public int Priority=>Type switch{TrainingCommandType.EmergencyStop=>0,TrainingCommandType.Abort=>1,TrainingCommandType.FinishSession=>2,TrainingCommandType.FinishAttempt=>3,TrainingCommandType.Pause=>4,TrainingCommandType.EmergencyStopReleased=>5,TrainingCommandType.ResetEmergencyStop=>6,TrainingCommandType.Disarm=>7,TrainingCommandType.DisconnectClamp=>8,TrainingCommandType.ConnectClamp=>9,TrainingCommandType.SelectWeldingNozzle=>10,TrainingCommandType.SelectCleaningNozzle=>10,TrainingCommandType.Arm=>11,TrainingCommandType.Resume=>12,_=>99}; }

    public sealed class TrainingCoordinator : IDisposable
    {
        readonly IMonotonicClock _clock; readonly IIdSource _ids; readonly ITrainingInputPort _input; readonly IRegistrationPort _registration; readonly ISafetyPort _safety; readonly IRecorderPort _recorder; readonly ISnapshotSink _sink; readonly IHapticStop _haptics;
        readonly EmergencyStopReducer _estop=new();readonly List<TrainingCommand> _commands=new(); long _commandSeq,_eventSeq,_snapshotSeq,_activationEpoch; string _sessionId; AttemptConfiguration _attempt; SessionState _state=SessionState.Idle; ActivationState _activation=ActivationState.Inhibited; NozzleState _nozzle=NozzleState.Unknown; ClampState _clamp=ClampState.Disconnected; BlockReason _reasons=BlockReason.NoSession; long _registrationGeneration; bool _releaseRequired,_estopPressedThisTick,_disposed;
        public ProcessSnapshot Current{get;private set;} public event Action<ProcessEvent> EventEmitted;
        public TrainingCoordinator(IMonotonicClock clock,IIdSource ids,ITrainingInputPort input,IRegistrationPort registration,ISafetyPort safety,IRecorderPort recorder,ISnapshotSink sink,IHapticStop haptics){_clock=clock??throw new ArgumentNullException(nameof(clock));_ids=ids??throw new ArgumentNullException(nameof(ids));_input=input??throw new ArgumentNullException(nameof(input));_registration=registration??throw new ArgumentNullException(nameof(registration));_safety=safety??throw new ArgumentNullException(nameof(safety));_recorder=recorder??throw new ArgumentNullException(nameof(recorder));_sink=sink??throw new ArgumentNullException(nameof(sink));_haptics=haptics??throw new ArgumentNullException(nameof(haptics));Publish(default);}
        public void StartSession(){if(_state!=SessionState.Idle&&_state!=SessionState.Completed&&_state!=SessionState.Aborted)throw new InvalidOperationException("Session active");_sessionId=_ids.NewId();_attempt=null;_nozzle=NozzleState.Unknown;_clamp=ClampState.Disconnected;_releaseRequired=true;Transition(SessionState.Initializing,ProcessEventType.SessionStarted,"created");Transition(SessionState.Registering,ProcessEventType.StateChanged,"await-registration");}
        public AttemptConfiguration PrepareAttempt(ProcessConfiguration process){if(_state!=SessionState.Selecting&&_state!=SessionState.Reviewing)throw new InvalidOperationException("Prepare only from Selecting/Reviewing");if(_activation==ActivationState.Armed||_activation==ActivationState.Active)throw new InvalidOperationException("Disarm first");var registration=_registration.Capture(_clock.Seconds);if(!registration.Valid||registration.FixtureId!=process.FixtureId)throw new InvalidOperationException("Registration/config mismatch");_attempt=new AttemptConfiguration(_ids.NewId(),process,registration.RegistrationGeneration);_recorder.Begin(_attempt);_activation=ActivationState.ReadyDisarmed;Transition(SessionState.Ready,ProcessEventType.AttemptPrepared,process.Profile.Hash);return _attempt;}
        public long Submit(TrainingCommandType type,double time){long s=++_commandSeq;_commands.Add(new TrainingCommand(type,time,s));return s;}
        public void Tick(){double now=_clock.Seconds;TrainingInput input=_input.Capture(now);RegistrationInput registration=_registration.Capture(now);_estopPressedThisTick=false;if(input.EmergencyStopPressed)_commands.Add(new TrainingCommand(TrainingCommandType.EmergencyStop,now,++_commandSeq));TrainingCommand[] due=_commands.Where(x=>x.Time<=now).OrderBy(x=>x.Time).ThenBy(x=>x.Priority).ThenBy(x=>x.Sequence).ToArray();foreach(var c in due.Where(x=>x.Type==TrainingCommandType.EmergencyStop||x.Type==TrainingCommandType.EmergencyStopReleased)){Apply(c,input,registration);_commands.Remove(c);}_estop.ObserveTrigger(input.TriggerPressed,now);foreach(var c in due.Where(x=>x.Type!=TrainingCommandType.EmergencyStop&&x.Type!=TrainingCommandType.EmergencyStopReleased)){Apply(c,input,registration);_commands.Remove(c);}if(_state==SessionState.Registering&&registration.Valid){_registrationGeneration=registration.RegistrationGeneration;Emit(ProcessEventType.RegistrationAccepted,registration.FixtureId,now);Transition(SessionState.Selecting,ProcessEventType.StateChanged,"select");}if(_state==SessionState.Ready||_state==SessionState.Running||_state==SessionState.Suspended)Evaluate(input,registration,now);Publish(input);}
        void Evaluate(TrainingInput input,RegistrationInput registration,double now)
        {
            if(_releaseRequired&&input.TriggerReleased&&!input.TriggerPressed)_releaseRequired=false;
            SafetyInput safety=_attempt==null?default:_safety.Evaluate(input,registration,_attempt);
            BlockReason processReasons=safety.Reasons;
            if(registration.OriginGeneration!=input.OriginGeneration)processReasons|=BlockReason.OriginMismatch;
            if(_attempt!=null&&now-input.CapturedSeconds>_attempt.Process.Profile.MaximumSampleGapSeconds)processReasons|=BlockReason.StaleInput;
            if(!safety.Available)processReasons|=BlockReason.SafetyUnknown;
            if(safety.Available&&!safety.Allowed)processReasons|=BlockReason.SafetyRejected;
            if(_estop.Current.Latched)processReasons|=BlockReason.EmergencyStop;
            var interlock=new InterlockInputs(
                _attempt!=null,
                registration.Available&&registration.Valid,
                safety.AssemblyConfirmed,
                safety.BindingMatches,
                input.HeadValid,
                input.ToolValid,
                input.Available&&!input.SystemInvalid,
                _recorder.Available,
                safety.MenuOpen,
                input.TriggerPressed,
                !_releaseRequired,
                _state==SessionState.Running&&(_activation==ActivationState.Armed||_activation==ActivationState.Active),
                _estop.Current.Latched||safety.FaultLatched,
                _nozzle,
                _clamp,
                _attempt?.Process.Profile.Mode??ProcessMode.Fusion,
                safety.Contact,
                safety.Risk,
                processReasons);
            ActivationDecision decision=ActivationReducer.Evaluate(interlock);
            if(_state==SessionState.Running&&decision.Reasons!=BlockReason.None){_activation=decision.State;_releaseRequired=true;StopAll(decision.Reasons);Transition(SessionState.Suspended,ProcessEventType.Suspended,decision.Reasons.ToString());}
            else _activation=decision.State;
            _reasons=decision.Reasons;
        }
        void Apply(TrainingCommand c,TrainingInput input,RegistrationInput registration)
        {
            switch(c.Type)
            {
                case TrainingCommandType.EmergencyStop:
                    _estopPressedThisTick=true;bool newlyLatched=!_estop.Current.Latched;EStopSnapshot stop=_estop.Press(c.Time,input.TriggerPressed);_releaseRequired=true;_activation=ActivationState.ResetRequired;StopAll(BlockReason.EmergencyStop);Emit(ProcessEventType.EmergencyStop,(newlyLatched?"latched:":"repeated:")+stop.FaultId,c.Time);if(_state==SessionState.Running)Transition(SessionState.Suspended,ProcessEventType.Suspended,"estop");break;
                case TrainingCommandType.EmergencyStopReleased:_estop.ObserveBRelease(c.Time);break;
                case TrainingCommandType.ResetEmergencyStop:
                    if(_estopPressedThisTick){Emit(ProcessEventType.EmergencyResetRejected,"same-update-estop",c.Time);break;}
                    SafetyInput recoverySafety=_attempt==null?default:_safety.Evaluate(input,registration,_attempt);bool nozzle=_attempt!=null&&ActivationReducer.Compatible(_attempt.Process.Profile.Mode,_nozzle);var recovery=new EStopRecoveryInput(registration.Available&&registration.Valid&&registration.OriginGeneration==input.OriginGeneration,input.HeadValid,input.ToolValid,input.Available&&!input.SystemInvalid,_clamp==ClampState.Connected,recoverySafety.Contact.State==ContactState.Contact,nozzle,recoverySafety.Risk==RiskState.Low&&!recoverySafety.FaultLatched,_recorder.Available);EStopSnapshot reset=_estop.TryReset(recovery,_clock.Seconds);if(reset.Latched)Emit(ProcessEventType.EmergencyResetRejected,reset.ResetRejected.ToString(),c.Time);else{_activation=ActivationState.ReadyDisarmed;_releaseRequired=true;Emit(ProcessEventType.EmergencyReset,"reset-disarmed",c.Time);}break;
                case TrainingCommandType.Abort:Finish(true);_state=SessionState.Aborted;Emit(ProcessEventType.SessionAborted,"abort",c.Time);break;
                case TrainingCommandType.FinishAttempt:Finish(false);break;
                case TrainingCommandType.FinishSession:if(_state!=SessionState.Reviewing)throw new InvalidOperationException();_state=SessionState.Completed;Emit(ProcessEventType.SessionCompleted,"complete",c.Time);break;
                case TrainingCommandType.Pause:if(_state==SessionState.Running){_activation=ActivationState.ReadyDisarmed;Transition(SessionState.Suspended,ProcessEventType.Suspended,"pause");}break;
                case TrainingCommandType.Resume:if(_state!=SessionState.Suspended)throw new InvalidOperationException();_activation=ActivationState.ReadyDisarmed;Transition(SessionState.Ready,ProcessEventType.Resumed,"ready");break;
                case TrainingCommandType.Disarm:_activation=ActivationState.ReadyDisarmed;_releaseRequired=input.TriggerPressed;Emit(ProcessEventType.Disarmed,"disarm",c.Time);break;
                case TrainingCommandType.ConnectClamp:Mutable();_clamp=ClampState.Connected;Emit(ProcessEventType.ClampChanged,"connected",c.Time);break;
                case TrainingCommandType.DisconnectClamp:_clamp=ClampState.Disconnected;if(_state==SessionState.Running){_activation=ActivationState.Inhibited;StopAll(BlockReason.ClampDisconnected);Transition(SessionState.Suspended,ProcessEventType.Suspended,"clamp");}Emit(ProcessEventType.ClampChanged,"disconnected",c.Time);break;
                case TrainingCommandType.SelectWeldingNozzle:Mutable();_nozzle=NozzleState.Welding;Emit(ProcessEventType.NozzleChanged,"welding",c.Time);break;
                case TrainingCommandType.SelectCleaningNozzle:Mutable();_nozzle=NozzleState.Cleaning;Emit(ProcessEventType.NozzleChanged,"cleaning",c.Time);break;
                case TrainingCommandType.Arm:if(_state!=SessionState.Ready)throw new InvalidOperationException();if(_estop.Current.Latched||input.TriggerPressed||_releaseRequired){_releaseRequired=true;break;}_activationEpoch=_estop.BeginActivationEpoch(_clock.Seconds);_activation=ActivationState.Armed;Emit(ProcessEventType.Rearmed,_activationEpoch.ToString(),c.Time);Transition(SessionState.Running,ProcessEventType.Armed,"armed");break;
            }
        }
        void StopAll(BlockReason reason){_haptics.StopImmediately();if(_haptics is IProcessStopSink process)process.StopAll(_activationEpoch,reason);}
        void Mutable(){if(_state==SessionState.Running||_activation==ActivationState.Armed||_activation==ActivationState.Active)throw new InvalidOperationException("Preparation locked");}
        void Finish(bool interrupted){if(_attempt==null)return;_activation=ActivationState.Inhibited;Transition(SessionState.Saving,ProcessEventType.SavingStarted,interrupted?"interrupted":"complete");if(!_recorder.Finish(_attempt.AttemptId,interrupted,out string error))Transition(SessionState.Faulted,ProcessEventType.SaveFailed,error);else Transition(SessionState.Reviewing,ProcessEventType.ReviewReady,_attempt.AttemptId);}
        void Transition(SessionState state,ProcessEventType type,string payload){_state=state;Emit(type,payload,_clock.Seconds);}void Emit(ProcessEventType type,string payload,double time){var e=new ProcessEvent(++_eventSeq,time,_sessionId,_attempt?.AttemptId,0,_registrationGeneration,_attempt?.Process.Profile.Hash,type,payload);EventEmitted?.Invoke(e);if(_recorder.Available)_recorder.Append(e);}void Publish(TrainingInput input){Current=new ProcessSnapshot(++_snapshotSeq,_sessionId,_attempt?.AttemptId,_state,_activation,_nozzle,_clamp,_reasons,input.TriggerPressed);_sink.Publish(Current);}public void Dispose(){if(_disposed)return;_disposed=true;_activation=ActivationState.Inhibited;_haptics.StopImmediately();_registration.Teardown();_recorder.Teardown();}
    }
}
