using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.TrainingTest
{
    [DisallowMultipleComponent] public sealed class FakeTrainingComposition:MonoBehaviour
    {
        TrainingCoordinator coordinator; readonly FakePorts ports=new(); bool prepared,preparationSubmitted,armSubmitted; public ProcessSnapshot Current=>coordinator?.Current;
        void Awake(){coordinator=new TrainingCoordinator(ports,ports,ports,ports,ports,ports,ports,ports);coordinator.StartSession();Debug.LogWarning("FAKE TRAINING COMPOSITION: synthetic valid ports; never use in production Bootstrap.",this);}
        void Update(){if(coordinator==null)return;coordinator.Tick();if(Current.Session==SessionState.Selecting&&!prepared){coordinator.PrepareAttempt(new ProcessConfiguration("synthetic-fixture","synthetic-seam",new ProcessProfile("synthetic-fusion",1,ProcessMode.Fusion,"synthetic-profile-v1",.1)));prepared=true;}else if(Current.Session==SessionState.Ready&&!preparationSubmitted){coordinator.Submit(TrainingCommandType.SelectWeldingNozzle,ports.Seconds);coordinator.Submit(TrainingCommandType.ConnectClamp,ports.Seconds);preparationSubmitted=true;}else if(Current.Session==SessionState.Ready&&preparationSubmitted&&!armSubmitted){coordinator.Submit(TrainingCommandType.Arm,ports.Seconds);armSubmitted=true;}}
        void OnDisable(){coordinator?.Dispose();coordinator=null;}
        sealed class FakePorts:IMonotonicClock,IIdSource,ITrainingInputPort,IRegistrationPort,ISafetyPort,IRecorderPort,ISnapshotSink,IHapticStop
        {int id;public double Seconds=>Time.realtimeSinceStartupAsDouble;public string NewId()=>(++id).ToString();public TrainingInput Capture(double now)=>new(now,1,1,true,true,true,false,true,false,false);RegistrationInput IRegistrationPort.Capture(double now)=>new(true,true,1,1,"synthetic-fixture");public void Teardown(){}public SafetyInput Evaluate(TrainingInput i,RegistrationInput r,AttemptConfiguration a)=>new(true,true,false,BlockReason.None);public bool Available=>true;public void Append(ProcessEvent e){}public void Begin(AttemptConfiguration a){}public bool Finish(string id,bool interrupted,out string error){error=null;return true;}public void Publish(ProcessSnapshot s){}public void StopImmediately(){} }
    }
}
