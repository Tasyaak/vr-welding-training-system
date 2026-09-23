using System;
using System.Collections.Generic;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum MenuOperation { Open, Close, Register, Reregister, SelectFixture, SelectSeam, SelectMode, AdjustParameter, ConnectClamp, DisconnectClamp, BeginNozzleChange, ConfirmNozzleChange, CancelNozzleChange, Acknowledge, Reset, Arm, Start, FinishAttempt, RetryAttempt, EndSession, SetAssistance }
    public enum MenuResponseCode { Accepted, WaitingForInactive, StaleContext, InvalidState, Unavailable, OutOfRange, Rejected }
    public enum MenuInputAction { None, Open, NavigatePrevious, NavigateNext, Decrease, Increase, Submit, EmergencyStop }

    public readonly struct MenuCommand
    {
        public readonly long Id, ContextGeneration, SnapshotSequence;
        public readonly MenuOperation Operation;
        public readonly string Value;
        public readonly double Number;
        public MenuCommand(long id,long context,long snapshot,MenuOperation operation,string value=null,double number=0){Id=id;ContextGeneration=context;SnapshotSequence=snapshot;Operation=operation;Value=value;Number=number;}
    }

    public readonly struct MenuCommandResponse
    {
        public readonly long CommandId, ContextGeneration;
        public readonly MenuResponseCode Code;
        public readonly string Explanation;
        public readonly ProcessMenuSnapshot Snapshot;
        public bool Accepted=>Code==MenuResponseCode.Accepted;
        public MenuCommandResponse(long id,long context,MenuResponseCode code,string explanation,ProcessMenuSnapshot snapshot){CommandId=id;ContextGeneration=context;Code=code;Explanation=explanation??string.Empty;Snapshot=snapshot;}
    }

    public sealed class ProcessMenuItem
    {
        public string Label{get;} public MenuOperation Operation{get;} public string Value{get;} public string Unit{get;} public double Current{get;} public double Minimum{get;} public double Maximum{get;} public double Step{get;} public bool Enabled{get;} public string UnavailableReason{get;}
        public ProcessMenuItem(string label,MenuOperation operation,string value=null,string unit="",double current=0,double minimum=0,double maximum=0,double step=0,bool enabled=true,string unavailableReason="")
        {Label=label??throw new ArgumentNullException(nameof(label));Operation=operation;Value=value;Unit=unit??string.Empty;Current=current;Minimum=minimum;Maximum=maximum;Step=step;Enabled=enabled;UnavailableReason=unavailableReason??string.Empty;}
    }

    public sealed class ProcessMenuSnapshot
    {
        public long Sequence{get;} public ProcessSnapshot Process{get;} public string FixtureId{get;} public string SeamId{get;} public ProcessMode Mode{get;} public string ProfileId{get;} public IReadOnlyDictionary<string,double> Parameters{get;} public NozzleChangeStatus NozzleChange{get;} public double AssistancePercent{get;} public bool RegistrationValid{get;} public bool Saving{get;} public string ResultStatus{get;} public IReadOnlyCollection<ProcessMode> AvailableModes{get;} public IReadOnlyList<string> InterlockReasons{get;} public IReadOnlyList<ProcessMenuItem> Items{get;}
        public ProcessMenuSnapshot(long sequence,ProcessSnapshot process,string fixture,string seam,ProcessMode mode,string profile,IReadOnlyDictionary<string,double> parameters,NozzleChangeStatus nozzleChange,double assistance,bool registrationValid,bool saving,string result,IReadOnlyCollection<ProcessMode> availableModes,IReadOnlyList<string> reasons,IReadOnlyList<ProcessMenuItem> items=null)
        {Sequence=sequence;Process=process;FixtureId=fixture??string.Empty;SeamId=seam??string.Empty;Mode=mode;ProfileId=profile??string.Empty;Parameters=parameters??new Dictionary<string,double>();NozzleChange=nozzleChange;AssistancePercent=assistance;RegistrationValid=registrationValid;Saving=saving;ResultStatus=result??string.Empty;AvailableModes=availableModes??Array.Empty<ProcessMode>();InterlockReasons=reasons??Array.Empty<string>();Items=items??Array.Empty<ProcessMenuItem>();}
    }

    public interface IProcessMenuPort
    {
        ProcessMenuSnapshot Current{get;}
        MenuCommandResponse Execute(MenuCommand command);
        void EmergencyStop(double monotonicSeconds);
    }

    /// <summary>Safe one-controller menu context. It dispatches commands and renders acknowledged snapshots only.</summary>
    public sealed class ProcessMenuController
    {
        readonly IProcessMenuPort port; long commandId,context; bool open,interactive,neutralRequired=true; int focus;
        public bool IsOpen=>open; public bool Interactive=>interactive; public int FocusIndex=>focus; public long ContextGeneration=>context; public string LastExplanation{get;private set;}=""; public ProcessMenuSnapshot View{get;private set;}
        public ProcessMenuController(IProcessMenuPort port){this.port=port??throw new ArgumentNullException(nameof(port));View=port.Current;}

        public MenuCommandResponse Open(double time)
        {
            context++;open=true;interactive=false;neutralRequired=true;focus=0;
            var response=Dispatch(MenuOperation.Open);
            Refresh();
            interactive=IsInactive(View.Process);
            if(!interactive) LastExplanation="Waiting for acknowledged inactive state";
            return response;
        }

        public MenuCommandResponse Close()
        {
            var response=Dispatch(MenuOperation.Close);open=false;interactive=false;neutralRequired=true;focus=0;Refresh();return response;
        }

        public void Refresh()
        {
            ProcessMenuSnapshot latest=port.Current;
            if(latest!=null&&latest.Sequence>=View.Sequence)View=latest;
            if(open&&!interactive&&IsInactive(View.Process))interactive=true;
        }

        public MenuCommandResponse Submit(MenuOperation operation,string value=null,double number=0)
        {
            if(!open||!interactive)return Local(MenuResponseCode.WaitingForInactive,"Menu is not interactive yet");
            if(neutralRequired)return Local(MenuResponseCode.Rejected,"Release controls before using this context");
            return Dispatch(operation,value,number);
        }

        public MenuCommandResponse ActivateFocused()
        {ProcessMenuItem item=Focused();if(item==null)return Local(MenuResponseCode.Unavailable,"No focused operation");if(!item.Enabled)return Local(MenuResponseCode.Unavailable,item.UnavailableReason);return Submit(item.Operation,item.Value,item.Current);}
        public MenuCommandResponse AdjustFocused(int direction)
        {ProcessMenuItem item=Focused();if(item==null||item.Step<=0)return Local(MenuResponseCode.Unavailable,"Focused operation is not adjustable");double next=Math.Max(item.Minimum,Math.Min(item.Maximum,item.Current+Math.Sign(direction)*item.Step));return Submit(item.Operation,item.Value,next);}

        public MenuInputAction UpdateInput(bool thumbstickClicked,bool previous,bool next,bool decrease,bool increase,bool aPressed,bool bPressed,bool allReleased,double time)
        {
            if(bPressed){port.EmergencyStop(time);Refresh();return MenuInputAction.EmergencyStop;}
            if(allReleased){neutralRequired=false;return MenuInputAction.None;}
            if(neutralRequired)return MenuInputAction.None;
            if(thumbstickClicked){if(open)Close();else Open(time);neutralRequired=true;return MenuInputAction.Open;}
            if(!open||!interactive)return MenuInputAction.None;
            if(previous){focus=Math.Max(0,focus-1);neutralRequired=true;return MenuInputAction.NavigatePrevious;}
            if(next){focus=Math.Min(Math.Max(0,(View?.Items.Count??1)-1),focus+1);neutralRequired=true;return MenuInputAction.NavigateNext;}
            if(decrease){neutralRequired=true;return MenuInputAction.Decrease;}
            if(increase){neutralRequired=true;return MenuInputAction.Increase;}
            if(aPressed){neutralRequired=true;return MenuInputAction.Submit;}
            return MenuInputAction.None;
        }

        MenuCommandResponse Dispatch(MenuOperation operation,string value=null,double number=0)
        {var response=port.Execute(new MenuCommand(++commandId,context,View?.Sequence??0,operation,value,number));if(response.ContextGeneration!=context)return Local(MenuResponseCode.StaleContext,"Response belongs to an old input context");LastExplanation=response.Explanation;if(response.Snapshot!=null&&response.Snapshot.Sequence>=(View?.Sequence??0))View=response.Snapshot;return response;}
        MenuCommandResponse Local(MenuResponseCode code,string explanation){LastExplanation=explanation;return new MenuCommandResponse(commandId,context,code,explanation,View);}
        ProcessMenuItem Focused()=>View!=null&&focus>=0&&focus<View.Items.Count?View.Items[focus]:null;
        static bool IsInactive(ProcessSnapshot snapshot)=>snapshot!=null&&snapshot.Activation!=ActivationState.Active&&snapshot.Activation!=ActivationState.Armed;
    }
}
