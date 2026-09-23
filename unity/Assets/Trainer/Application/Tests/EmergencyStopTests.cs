using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class EmergencyStopTests
    {
        static readonly EStopRecoveryInput Valid=new(true,true,true,true,true,true,true,true,true);
        [Test]
        public void PressLatchesIdempotentlyAndAdvancesEvidenceSequenceOnce()
        {var reducer=new EmergencyStopReducer();EStopSnapshot first=reducer.Press(1,true);EStopSnapshot repeat=reducer.Press(1.1,true);Assert.That(first.Latched,Is.True);Assert.That(first.FaultId,Is.Not.Null);Assert.That(repeat.StopSequence,Is.EqualTo(first.StopSequence));Assert.That(repeat.FaultId,Is.EqualTo(first.FaultId));}

        [Test]
        public void ReleaseOrCorrectedPoseAloneNeverClearsLatch()
        {var reducer=new EmergencyStopReducer();reducer.Press(0,true);reducer.ObserveBRelease(.1);EStopSnapshot released=reducer.ObserveTrigger(false,.2);Assert.That(released.Latched,Is.True);Assert.That(released.BReleased&&released.TriggerReleased,Is.True);}

        [Test]
        public void ResetRejectsEveryInvalidRecoveryConditionWithTypedReason()
        {AssertRejected(new EStopRecoveryInput(false,true,true,true,true,true,true,true,true),ResetRejection.RegistrationInvalid);AssertRejected(new EStopRecoveryInput(true,false,true,true,true,true,true,true,true),ResetRejection.HeadInvalid);AssertRejected(new EStopRecoveryInput(true,true,false,true,true,true,true,true,true),ResetRejection.ToolInvalid);AssertRejected(new EStopRecoveryInput(true,true,true,false,true,true,true,true,true),ResetRejection.SystemInvalid);AssertRejected(new EStopRecoveryInput(true,true,true,true,false,true,true,true,true),ResetRejection.ClampInvalid);AssertRejected(new EStopRecoveryInput(true,true,true,true,true,false,true,true,true),ResetRejection.ContactInvalid);AssertRejected(new EStopRecoveryInput(true,true,true,true,true,true,false,true,true),ResetRejection.NozzleInvalid);AssertRejected(new EStopRecoveryInput(true,true,true,true,true,true,true,false,true),ResetRejection.ReflectionNotResetEligible);AssertRejected(new EStopRecoveryInput(true,true,true,true,true,true,true,true,false),ResetRejection.RecorderInvalid);}

        [Test]
        public void DeliberateResetReturnsDisarmedAndNewEpochRequiresSeparateCommand()
        {var reducer=new EmergencyStopReducer();reducer.Press(0,true);reducer.ObserveBRelease(.1);reducer.ObserveTrigger(false,.2);EStopSnapshot reset=reducer.TryReset(Valid,.3);Assert.That(reset.Latched,Is.False);long epoch=reducer.BeginActivationEpoch(.4);Assert.That(epoch,Is.GreaterThan(reset.ActivationEpoch));}

        [Test]
        public void TriggerAndBReleaseAreBothMandatory()
        {var reducer=new EmergencyStopReducer();reducer.Press(0,true);EStopSnapshot rejected=reducer.TryReset(Valid,.1);Assert.That(rejected.ResetRejected.HasFlag(ResetRejection.BReleaseRequired),Is.True);Assert.That(rejected.ResetRejected.HasFlag(ResetRejection.TriggerReleaseRequired),Is.True);}

        static void AssertRejected(EStopRecoveryInput input,ResetRejection reason){var reducer=new EmergencyStopReducer();reducer.Press(0,false);reducer.ObserveBRelease(.1);EStopSnapshot result=reducer.TryReset(input,.2);Assert.That(result.Latched,Is.True);Assert.That(result.ResetRejected.HasFlag(reason),Is.True);}
    }
}
