using System.Collections.Generic;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class ProcessMenuTests
    {
        [Test] public void OpeningActiveProcessWaitsForAcknowledgedInactiveSnapshot()
        {var port=new Port(Snapshot(1,ActivationState.Active));var menu=new ProcessMenuController(port);menu.Open(0);Assert.That(port.Last.Operation,Is.EqualTo(MenuOperation.Open));Assert.That(menu.Interactive,Is.False);port.Value=Snapshot(2,ActivationState.ReadyDisarmed);menu.Refresh();Assert.That(menu.Interactive,Is.True);}
        [Test] public void HeldInputCannotClickThroughOpenOrClose()
        {var port=new Port(Snapshot(1,ActivationState.ReadyDisarmed));var menu=new ProcessMenuController(port);menu.Open(0);Assert.That(menu.UpdateInput(false,false,false,false,false,true,false,false,.1),Is.EqualTo(MenuInputAction.None));menu.UpdateInput(false,false,false,false,false,false,false,true,.2);Assert.That(menu.UpdateInput(false,false,false,false,false,true,false,false,.3),Is.EqualTo(MenuInputAction.Submit));menu.Close();Assert.That(menu.UpdateInput(false,false,false,false,false,true,false,false,.4),Is.EqualTo(MenuInputAction.None));}
        [Test] public void BAlwaysEmergencyStopsAndIsNeverBack()
        {var port=new Port(Snapshot(1,ActivationState.Active));var menu=new ProcessMenuController(port);MenuInputAction action=menu.UpdateInput(false,false,false,false,false,false,true,false,3);Assert.That(action,Is.EqualTo(MenuInputAction.EmergencyStop));Assert.That(port.EStops,Is.EqualTo(1));Assert.That(menu.IsOpen,Is.False);}
        [Test] public void ViewChangesOnlyFromAcknowledgedAuthoritativeSnapshot()
        {var port=new Port(Snapshot(4,ActivationState.ReadyDisarmed));var menu=new ProcessMenuController(port);menu.Open(0);menu.UpdateInput(false,false,false,false,false,false,false,true,.1);port.ResponseSnapshot=Snapshot(5,ActivationState.ReadyDisarmed,ClampState.Connected,NozzleState.Cleaning);MenuCommandResponse result=menu.Submit(MenuOperation.ConnectClamp);Assert.That(result.Accepted,Is.True);Assert.That(menu.View.Process.Clamp,Is.EqualTo(ClampState.Connected));Assert.That(menu.View.Process.Nozzle,Is.EqualTo(NozzleState.Cleaning));}
        [Test] public void StaleContextResponseIsRejected()
        {var port=new Port(Snapshot(1,ActivationState.ReadyDisarmed)){WrongContext=true};var menu=new ProcessMenuController(port);menu.Open(0);Assert.That(menu.LastExplanation,Does.Contain("old input context"));}
        [Test] public void TriggerHasNoMenuBinding()
        {Assert.That(System.Enum.GetNames(typeof(MenuInputAction)),Does.Not.Contain("Trigger"));}
        [Test] public void UnavailableFocusedOperationIsHonestAndParameterIsBounded()
        {var unavailable=new ProcessMenuItem("Pulsed",MenuOperation.SelectMode,"Pulsed",enabled:false,unavailableReason:"Kernel not installed");var port=new Port(Snapshot(1,ActivationState.ReadyDisarmed,items:new[]{unavailable}));var menu=new ProcessMenuController(port);menu.Open(0);menu.UpdateInput(false,false,false,false,false,false,false,true,.1);Assert.That(menu.ActivateFocused().Code,Is.EqualTo(MenuResponseCode.Unavailable));Assert.That(menu.LastExplanation,Does.Contain("Kernel"));var adjustable=new ProcessMenuItem("Speed",MenuOperation.AdjustParameter,"speed","m/s",.19,.05,.2,.05);port.Value=Snapshot(2,ActivationState.ReadyDisarmed,items:new[]{adjustable});menu.Refresh();menu.AdjustFocused(1);Assert.That(port.Last.Number,Is.EqualTo(.2));}

        static ProcessMenuSnapshot Snapshot(long seq,ActivationState activation,ClampState clamp=ClampState.Disconnected,NozzleState nozzle=NozzleState.Unknown,ProcessMenuItem[] items=null)
        {var process=new ProcessSnapshot(seq,"s","a",SessionState.Ready,activation,nozzle,clamp,BlockReason.None,false);return new ProcessMenuSnapshot(seq,process,"fixture","seam",ProcessMode.Fusion,"fusion",new Dictionary<string,double>(),NozzleChangeStatus.Idle,50,true,false,"",new[]{ProcessMode.Fusion},new string[0],items);}
        sealed class Port:IProcessMenuPort
        {public ProcessMenuSnapshot Value,ResponseSnapshot;public MenuCommand Last;public int EStops;public bool WrongContext;public Port(ProcessMenuSnapshot value){Value=value;}public ProcessMenuSnapshot Current=>Value;public MenuCommandResponse Execute(MenuCommand command){Last=command;var s=ResponseSnapshot??Value;Value=s;return new MenuCommandResponse(command.Id,WrongContext?command.ContextGeneration-1:command.ContextGeneration,MenuResponseCode.Accepted,"accepted",s);}public void EmergencyStop(double time)=>EStops++;}
    }
}
