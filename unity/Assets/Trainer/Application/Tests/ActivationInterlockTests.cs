using System;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class ActivationInterlockTests
    {
        static readonly ContactPolicy Policy = new(.005, .010);

        [Test]
        public void ContactUsesFiniteBoundsApproachAndHysteresis()
        {
            var evaluator = new FiniteContactEvaluator();
            FinitePatch patch = Square("front", 0);

            Assert.That(evaluator.Evaluate(new[] { patch }, new Vec3(.5, .5, .004), true, Policy).State, Is.EqualTo(ContactState.Contact));
            Assert.That(evaluator.Evaluate(new[] { patch }, new Vec3(.5, .5, .009), true, Policy).State, Is.EqualTo(ContactState.Contact));
            Assert.That(evaluator.Evaluate(new[] { patch }, new Vec3(.5, .5, .011), true, Policy).State, Is.EqualTo(ContactState.Separated));
            Assert.That(evaluator.Evaluate(new[] { patch }, new Vec3(1.1, .5, .001), true, Policy).Reason, Is.EqualTo(ContactReason.OutsideBounds));
            Assert.That(evaluator.Evaluate(new[] { patch }, new Vec3(.5, .5, -.001), true, Policy).Reason, Is.EqualTo(ContactReason.WrongApproach));
        }

        [Test]
        public void OnlyRetainedSurfaceReceivesExitBand()
        {
            var evaluator = new FiniteContactEvaluator();
            FinitePatch retained = Square("retained", 0);
            FinitePatch adjacent = Square("adjacent", .003);

            evaluator.Evaluate(new[] { retained }, new Vec3(.5, .5, .001), true, Policy);
            ContactEvidence result = evaluator.Evaluate(new[] { retained, adjacent }, new Vec3(.5, .5, .009), true, Policy);

            Assert.That(result.State, Is.EqualTo(ContactState.Contact));
            Assert.That(result.SurfaceId, Is.EqualTo("retained"));
        }

        [Test]
        public void OverlappingEntryCandidatesAreAmbiguousAndTrackingLossClearsRetention()
        {
            var evaluator = new FiniteContactEvaluator();
            FinitePatch a = Square("a", 0), b = Square("b", .001);
            Assert.That(evaluator.Evaluate(new[] { a, b }, new Vec3(.5, .5, .002), true, Policy).Reason, Is.EqualTo(ContactReason.Ambiguous));
            Assert.That(evaluator.Evaluate(new[] { a }, new Vec3(.5, .5, .001), true, Policy).State, Is.EqualTo(ContactState.Contact));
            Assert.That(evaluator.Evaluate(new[] { a }, default, false, Policy).Reason, Is.EqualTo(ContactReason.TrackingInvalid));
            Assert.That(evaluator.Evaluate(new[] { a }, new Vec3(.5, .5, .009), true, Policy).State, Is.EqualTo(ContactState.Separated));
        }

        [Test]
        public void RayImpactRejectsInfinitePlaneHitOutsidePatch()
        {
            FinitePatch patch = Square("front", 0);
            Assert.That(FiniteContactEvaluator.TryRayImpact(patch, new Vec3(.5, .5, 1), new Vec3(0, 0, -1), out Vec3 hit), Is.True);
            Assert.That(hit.Z, Is.EqualTo(0).Within(1e-9));
            Assert.That(FiniteContactEvaluator.TryRayImpact(patch, new Vec3(2, .5, 1), new Vec3(0, 0, -1), out _), Is.False);
        }

        [TestCase(ProcessMode.Fusion, NozzleState.Welding, true)]
        [TestCase(ProcessMode.Wobble, NozzleState.Welding, true)]
        [TestCase(ProcessMode.Pulsed, NozzleState.Welding, true)]
        [TestCase(ProcessMode.PreWeldCleaning, NozzleState.Cleaning, true)]
        [TestCase(ProcessMode.PostWeldCleaning, NozzleState.Cleaning, true)]
        [TestCase(ProcessMode.Fusion, NozzleState.Cleaning, false)]
        [TestCase(ProcessMode.PostWeldCleaning, NozzleState.Welding, false)]
        public void NozzleCompatibilityIsExplicitForEveryMode(ProcessMode mode, NozzleState nozzle, bool expected)
        {
            Assert.That(ActivationReducer.Compatible(mode, nozzle), Is.EqualTo(expected));
        }

        [Test]
        public void ReducerRequiresBothVirtualContactAndMechanicalClamp()
        {
            ActivationDecision noContact = ActivationReducer.Evaluate(Valid(contact: Separated()));
            ActivationDecision noClamp = ActivationReducer.Evaluate(Valid(clamp: ClampState.Disconnected));

            Assert.That(noContact.Reasons.HasFlag(BlockReason.ContactInvalid), Is.True);
            Assert.That(noClamp.Reasons.HasFlag(BlockReason.ClampDisconnected), Is.True);
            Assert.That(noContact.Active || noClamp.Active, Is.False);
        }

        [Test]
        public void UnknownRiskAssemblyAndBindingFailClosed()
        {
            ActivationDecision result = ActivationReducer.Evaluate(Valid(risk: RiskState.Unknown, assembly: false, binding: false));
            Assert.That(result.Reasons.HasFlag(BlockReason.ReflectionUnknown), Is.True);
            Assert.That(result.Reasons.HasFlag(BlockReason.AssemblyUnconfirmed), Is.True);
            Assert.That(result.Reasons.HasFlag(BlockReason.BindingMismatch), Is.True);
            Assert.That(result.PrimaryReason, Is.EqualTo(BlockReason.ReflectionUnknown));
        }

        [Test]
        public void EveryCommonPrerequisiteHasATypedNegativeReason()
        {
            AssertReason(Valid(session: false), BlockReason.NoSession);
            AssertReason(Valid(registration: false), BlockReason.RegistrationInvalid);
            AssertReason(Valid(assembly: false), BlockReason.AssemblyUnconfirmed);
            AssertReason(Valid(binding: false), BlockReason.BindingMismatch);
            AssertReason(Valid(head: false), BlockReason.HeadInvalid);
            AssertReason(Valid(tool: false), BlockReason.ToolInvalid);
            AssertReason(Valid(system: false), BlockReason.SystemInvalid);
            AssertReason(Valid(recorder: false), BlockReason.RecorderUnavailable);
            AssertReason(Valid(menu: true), BlockReason.MenuOpen);
            AssertReason(Valid(nozzle: NozzleState.Unknown), BlockReason.NozzleUnknown);
            AssertReason(Valid(nozzle: NozzleState.Cleaning), BlockReason.NozzleMismatch);
            AssertReason(Valid(clamp: ClampState.Disconnected), BlockReason.ClampDisconnected);
            AssertReason(Valid(contact: Separated()), BlockReason.ContactInvalid);
            AssertReason(Valid(risk: RiskState.Unknown), BlockReason.ReflectionUnknown);
            AssertReason(Valid(risk: RiskState.High), BlockReason.ReflectionUnsafe);
            AssertReason(Valid(processReasons: BlockReason.ProcessPrerequisiteMissing), BlockReason.ProcessPrerequisiteMissing);
            AssertReason(Valid(release: false), BlockReason.TriggerReleaseRequired);
            AssertReason(Valid(fault: true), BlockReason.FaultLatched);
        }

        [Test]
        public void HeldTriggerCannotReactivateUntilReleaseThenExplicitArm()
        {
            Assert.That(ActivationReducer.Evaluate(Valid(trigger: true, release: false, armed: true)).Reasons.HasFlag(BlockReason.TriggerReleaseRequired), Is.True);
            Assert.That(ActivationReducer.Evaluate(Valid(trigger: false, release: true, armed: false)).State, Is.EqualTo(ActivationState.ReadyDisarmed));
            Assert.That(ActivationReducer.Evaluate(Valid(trigger: false, release: true, armed: true)).State, Is.EqualTo(ActivationState.Armed));
            Assert.That(ActivationReducer.Evaluate(Valid(trigger: true, release: true, armed: true)).State, Is.EqualTo(ActivationState.Active));
        }

        static FinitePatch Square(string id, double z) => new(id, new[]
        {
            new Vec3(0, 0, z), new Vec3(1, 0, z), new Vec3(1, 1, z), new Vec3(0, 1, z)
        }, new Vec3(0, 0, 1), 1);

        static ContactEvidence Contact() => new(ContactState.Contact, ContactReason.None, "front", default, new Vec3(0, 0, 1), .001, .5, true, true);
        static ContactEvidence Separated() => new(ContactState.Separated, ContactReason.OutsideBounds, "front", default, new Vec3(0, 0, 1), .1, 0, false, true);

        static void AssertReason(InterlockInputs inputs, BlockReason expected) =>
            Assert.That(ActivationReducer.Evaluate(inputs).Reasons.HasFlag(expected), Is.True, expected.ToString());

        static InterlockInputs Valid(
            ContactEvidence? contact = null,
            ClampState clamp = ClampState.Connected,
            RiskState risk = RiskState.Low,
            bool session = true,
            bool registration = true,
            bool assembly = true,
            bool binding = true,
            bool head = true,
            bool tool = true,
            bool system = true,
            bool recorder = true,
            bool menu = false,
            bool trigger = false,
            bool release = true,
            bool armed = true,
            bool fault = false,
            NozzleState nozzle = NozzleState.Welding,
            BlockReason processReasons = BlockReason.None) =>
            new(session, registration, assembly, binding, head, tool, system, recorder, menu, trigger, release, armed, fault,
                nozzle, clamp, ProcessMode.Fusion, contact ?? Contact(), risk, processReasons);
    }
}
