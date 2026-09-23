using System;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public readonly struct NozzleChangeEvent
    {public readonly double TimeSeconds;public readonly long TransactionId;public readonly NozzleState Installed,Requested,Effective;public readonly NozzleChangeStatus Status;public readonly string Type;public NozzleChangeEvent(double time,NozzleChangeSnapshot snapshot){TimeSeconds=time;TransactionId=snapshot.TransactionId;Installed=snapshot.Installed;Requested=snapshot.Requested;Effective=snapshot.Effective;Status=snapshot.Status;Type=snapshot.Event;}}
    public interface INozzleChangeEventSink { void Append(NozzleChangeEvent value); }
    public sealed class NozzleChangeService
    {readonly NozzleChangeWorkflow workflow;readonly INozzleChangeEventSink sink;public NozzleChangeService(NozzleChangeWorkflow workflow,INozzleChangeEventSink sink){this.workflow=workflow??throw new ArgumentNullException(nameof(workflow));this.sink=sink??throw new ArgumentNullException(nameof(sink));}public NozzleChangeSnapshot Current=>workflow.Current;public NozzleChangeSnapshot Begin(NozzleState requested,bool disarmed,bool triggerReleased,bool fault,double time)=>Publish(workflow.Begin(requested,disarmed,triggerReleased,fault),time);public NozzleChangeSnapshot Confirm(long transactionId,bool disarmed,bool fault,double time)=>Publish(workflow.Confirm(transactionId,disarmed,fault),time);public NozzleChangeSnapshot Cancel(double time)=>Publish(workflow.Cancel(),time);public NozzleChangeSnapshot Fault(double time)=>Publish(workflow.OnFault(),time);NozzleChangeSnapshot Publish(NozzleChangeSnapshot value,double time){if(!double.IsFinite(time))throw new ArgumentException("Event time required");sink.Append(new NozzleChangeEvent(time,value));return value;}}
}
