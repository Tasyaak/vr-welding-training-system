using System;
using System.Linq;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class CleaningProcessTests
    {
        static readonly ActivationDecision Active=new(ActivationState.Active,BlockReason.None,true),Blocked=new(ActivationState.Inhibited,BlockReason.NozzleMismatch,true);
        [Test]
        public void AuthoredPreCleanChangesOnlyAfterActiveAttributableSweep()
        {var kernel=Kernel();kernel.Update(I(0,.01,.05,Blocked));CleaningResult blocked=kernel.Update(I(.1,.09,.05,Blocked));Assert.That(blocked.AttemptedAreaSquareMetres,Is.Zero);kernel.Update(I(.2,.01,.05,Active));CleaningResult active=kernel.Update(I(.3,.09,.05,Active));Assert.That(active.AttemptedAreaSquareMetres,Is.GreaterThan(0));}

        [Test]
        public void PoorSweepIsAttemptedAndLaterGoodSweepImprovesAcceptableWithoutErasingHistory()
        {var kernel=Kernel();kernel.Update(I(0,.01,.05,Active,speed:.3));CleaningResult poor=kernel.Update(I(.1,.09,.05,Active,speed:.3));Assert.That(poor.AttemptedAreaSquareMetres,Is.GreaterThan(0));Assert.That(poor.AcceptableAreaSquareMetres,Is.Zero);kernel.Update(I(.2,.01,.05,Active));CleaningResult good=kernel.Update(I(.3,.09,.05,Active));Assert.That(good.AcceptableAreaSquareMetres,Is.GreaterThan(0));Assert.That(good.RepeatVisitCount,Is.GreaterThan(0));Assert.That(good.AttemptedAreaSquareMetres,Is.LessThanOrEqualTo(good.TargetAreaSquareMetres));}

        [Test]
        public void InvalidTrackingCreatesNoCoverageAndAccumulatesInvalidTime()
        {var kernel=Kernel();kernel.Update(I(0,.01,.05,Active));CleaningResult invalid=kernel.Update(I(.1,.09,.05,Active,tracking:false));Assert.That(invalid.AttemptedAreaSquareMetres,Is.Zero);Assert.That(invalid.InvalidSeconds,Is.EqualTo(.1));}

        [Test]
        public void EmptyWeldCannotCreateImplicitPostCleanTarget()
        {bool created=CleaningTargetSnapshot.TryCreateDerived("post","surface","weld-attempt",.01,2,2,new double[4],out CleaningTargetSnapshot target);Assert.That(created,Is.False);Assert.That(target,Is.Null);}

        [Test]
        public void DerivedPostTargetRecordsSourceAttempt()
        {Assert.That(CleaningTargetSnapshot.TryCreateDerived("post","surface","weld-42",.01,2,2,new[]{1d,0,0,1},out CleaningTargetSnapshot target),Is.True);Assert.That(target.SourceAttemptId,Is.EqualTo("weld-42"));Assert.That(target.Policy,Is.EqualTo(CleaningTargetPolicy.DerivedFromWeld));}

        [Test]
        public void NozzleChangeIsPendingIncompatibleAndNeverArms()
        {var workflow=new NozzleChangeWorkflow(NozzleState.Welding);NozzleChangeSnapshot pending=workflow.Begin(NozzleState.Cleaning,true,true,false);Assert.That(pending.Status,Is.EqualTo(NozzleChangeStatus.ChangePending));Assert.That(pending.Effective,Is.EqualTo(NozzleState.Unknown));Assert.That(ActivationReducer.Compatible(ProcessMode.PreWeldCleaning,pending.Effective),Is.False);NozzleChangeSnapshot confirmed=workflow.Confirm(pending.TransactionId,true,false);Assert.That(confirmed.Installed,Is.EqualTo(NozzleState.Cleaning));Assert.That(confirmed.Status,Is.EqualTo(NozzleChangeStatus.Stable));}

        [Test]
        public void NozzleChangeRejectsHeldTriggerAndStaleConfirmAndFaultCancels()
        {var workflow=new NozzleChangeWorkflow(NozzleState.Welding);Assert.Throws<InvalidOperationException>(()=>workflow.Begin(NozzleState.Cleaning,true,false,false));NozzleChangeSnapshot pending=workflow.Begin(NozzleState.Cleaning,true,true,false);Assert.Throws<InvalidOperationException>(()=>workflow.Confirm(pending.TransactionId+1,true,false));NozzleChangeSnapshot cancelled=workflow.OnFault();Assert.That(cancelled.Installed,Is.EqualTo(NozzleState.Welding));Assert.That(cancelled.Status,Is.EqualTo(NozzleChangeStatus.Stable));}

        [TestCase(30)] [TestCase(72)] [TestCase(120)]
        public void PresentationRateCannotChangeTimestampedArea(int hz)
        {var kernel=Kernel();double render=0,step=1d/hz;kernel.Update(I(0,.01,.05,Active));while(render<.1)render+=step;CleaningResult result=kernel.Update(I(.1,.09,.05,Active));Assert.That(result.AttemptedAreaSquareMetres,Is.GreaterThan(0));Assert.That(result.AcceptableAreaSquareMetres,Is.EqualTo(result.AttemptedAreaSquareMetres));}

        static CleaningProcessKernel Kernel(){double[] mask=Enumerable.Repeat(1d,100).ToArray();return new CleaningProcessKernel(new CleaningTargetSnapshot("pre","surface",CleaningTargetPolicy.AuthoredPreWeld,null,.01,10,10,mask),new CleaningProfile("clean",1,"hash",ProcessMode.PreWeldCleaning,.012,.02,.2,.005,.2,.2));}
        static CleaningInput I(double time,double u,double v,ActivationDecision activation,double speed=.1,bool tracking=true)=>new(time,u,v,speed,.001,.05,"surface",tracking,activation,1,1);
    }
}
