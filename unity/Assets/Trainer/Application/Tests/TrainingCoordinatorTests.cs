using System;
using System.Collections.Generic;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class TrainingCoordinatorTests
    {
        Clock clock; Input input; Registration registration; Safety safety; Recorder recorder; Sink sink; Haptics haptics; TrainingCoordinator coordinator;
        [SetUp] public void Setup(){clock=new Clock();input=new Input();registration=new Registration();safety=new Safety();recorder=new Recorder();sink=new Sink();haptics=new Haptics();coordinator=new TrainingCoordinator(clock,new Ids(),input,registration,safety,recorder,sink,haptics);}
        [TearDown] public void Down()=>coordinator.Dispose();
        [Test] public void RegistrationIsRequiredBeforeSelection(){coordinator.StartSession();coordinator.Tick();Assert.That(coordinator.Current.Session,Is.EqualTo(SessionState.Registering));registration.Value=new RegistrationInput(true,true,7,3,"fixture");input.Value=Valid(false,true);coordinator.Tick();Assert.That(coordinator.Current.Session,Is.EqualTo(SessionState.Selecting));}
        [Test] public void MissingSafetyNeverActivates(){Ready();safety.Value=default;PrepareAndArm();Assert.That(coordinator.Current.Activation,Is.EqualTo(ActivationState.Inhibited));Assert.That(coordinator.Current.Reasons.HasFlag(BlockReason.SafetyUnknown),Is.True);}
        [Test] public void EmergencyStopWinsSameTimestamp(){Ready();Prepare();coordinator.Submit(TrainingCommandType.Arm,clock.Seconds);coordinator.Submit(TrainingCommandType.EmergencyStop,clock.Seconds);coordinator.Tick();Assert.That(coordinator.Current.Activation,Is.EqualTo(ActivationState.ResetRequired));Assert.That(haptics.Count,Is.GreaterThan(0));}
        [Test] public void RegistrationLossSuspendsRunningAttempt(){Ready();PrepareAndArm();registration.Value=new RegistrationInput(true,false,7,3,"fixture");coordinator.Tick();Assert.That(coordinator.Current.Session,Is.EqualTo(SessionState.Suspended));}
        [Test] public void AttemptConfigurationCannotChangeWhileRunning(){Ready();PrepareAndArm();Assert.Throws<InvalidOperationException>(()=>coordinator.PrepareAttempt(Config(ProcessMode.Wobble)));}
        [Test] public void RetryHasDistinctIdAndPreservesPreviousAttempt(){Ready();PrepareAndArm();string first=coordinator.Current.AttemptId;coordinator.Submit(TrainingCommandType.FinishAttempt,clock.Seconds);coordinator.Tick();coordinator.PrepareAttempt(Config(ProcessMode.Fusion));Assert.That(coordinator.Current.AttemptId,Is.Not.EqualTo(first));Assert.That(recorder.Finished,Does.Contain(first));}
        [Test] public void RecorderFailureIsExplicit(){Ready();PrepareAndArm();recorder.FinishOk=false;coordinator.Submit(TrainingCommandType.FinishAttempt,clock.Seconds);coordinator.Tick();Assert.That(coordinator.Current.Session,Is.EqualTo(SessionState.Faulted));}
        [Test] public void DisposeStopsOutputsAndPorts(){coordinator.StartSession();coordinator.Dispose();Assert.That(haptics.Count,Is.EqualTo(1));Assert.That(registration.TornDown,Is.True);Assert.That(recorder.TornDown,Is.True);}
        void Ready(){coordinator.StartSession();registration.Value=new RegistrationInput(true,true,7,3,"fixture");input.Value=Valid(false,true);coordinator.Tick();}
        void Prepare(){coordinator.PrepareAttempt(Config(ProcessMode.Fusion));coordinator.Submit(TrainingCommandType.SelectWeldingNozzle,clock.Seconds);coordinator.Submit(TrainingCommandType.ConnectClamp,clock.Seconds);coordinator.Tick();}
        void PrepareAndArm(){Prepare();input.Value=Valid(false,true);coordinator.Tick();coordinator.Submit(TrainingCommandType.Arm,clock.Seconds);coordinator.Tick();}
        TrainingInput Valid(bool trigger,bool released=false)=>new(clock.Seconds,1,3,true,true,true,trigger,released,false,false);
        static ProcessConfiguration Config(ProcessMode mode)=>new("fixture","seam",new ProcessProfile(mode.ToString(),1,mode,"hash-"+mode,.1));
        sealed class Clock:IMonotonicClock{public double Seconds{get;set;}=1;} sealed class Ids:IIdSource{int n;public string NewId()=>(++n).ToString();}
        sealed class Input:ITrainingInputPort{public TrainingInput Value;public TrainingInput Capture(double n)=>Value;} sealed class Registration:IRegistrationPort{public RegistrationInput Value;public bool TornDown;public RegistrationInput Capture(double n)=>Value;public void Teardown()=>TornDown=true;}
        sealed class Safety:ISafetyPort{public SafetyInput Value=new(true,true,false,BlockReason.None);public SafetyInput Evaluate(TrainingInput i,RegistrationInput r,AttemptConfiguration a)=>Value;} sealed class Sink:ISnapshotSink{public ProcessSnapshot Last;public void Publish(ProcessSnapshot s)=>Last=s;} sealed class Haptics:IHapticStop{public int Count;public void StopImmediately()=>Count++;}
        sealed class Recorder:IRecorderPort{public bool Available=>true;public bool FinishOk=true,TornDown;public readonly List<string> Finished=new();public void Append(ProcessEvent e){}public void Begin(AttemptConfiguration a){}public bool Finish(string id,bool interrupted,out string error){Finished.Add(id);error=FinishOk?null:"disk";return FinishOk;}public void Teardown()=>TornDown=true;}
    }
}
