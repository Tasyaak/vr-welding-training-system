using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class FusionMvpTests
    {
        [Test]
        public void FusionDoesNotBridgeStoppedIntervalsOrCompleteFromEndTouch()
        {
            var seam = new DirectedSpline("seam", new[] { new Vec3(0, 0, 0), new Vec3(.1, 0, 0) });
            var profile = new FusionProfile("fusion", 1, "hash", "seam", .004, .004, .015, .035, .4, .4, .003, .002, .1, .02, .002, .006);
            var kernel = new FusionProcessKernel("attempt", seam, profile);
            var on = new ActivationDecision(ActivationState.Active, BlockReason.None, true);
            var off = new ActivationDecision(ActivationState.Armed, BlockReason.None, false);
            kernel.Update(new FusionInput(0, Path(seam, 0), on, 1, 1));
            var first = kernel.Update(new FusionInput(.05, Path(seam, .001), on, 1, 1));
            Assert.That(first.Coverage.AttemptedLengthMetres, Is.EqualTo(.001).Within(1e-9));
            kernel.Update(new FusionInput(.1, Path(seam, .002), off, 1, 1));
            var last = kernel.Update(new FusionInput(.15, Path(seam, .1), on, 1, 1));
            Assert.That(last.Complete, Is.False);
            Assert.That(last.Coverage.AttemptedLengthMetres, Is.EqualTo(.001).Within(1e-9));
            Assert.That(last.Delta.Valid, Is.False);
        }

        [Test]
        public void AssistanceDoesNotConfuseIndependentInputAndRegistrationGenerations()
        {
            var engine = new SemanticFeedbackEngine();
            var input = new FeedbackInput(12, 3, 1, .4, true, true, BlockReason.None, true, false, false, false, false, false, false, false, false);
            var zero = engine.Evaluate(input, 0);
            var full = engine.Evaluate(input, 100);
            Assert.That(zero.MetricsEligible, Is.True);
            Assert.That(full.MetricsEligible, Is.True);
            Assert.That(zero.VisualStrength, Is.Zero);
            Assert.That(full.VisualStrength, Is.EqualTo(1));
            var stop = new FeedbackInput(12, 3, 2, .4, true, true, BlockReason.EmergencyStop, true, false, false, false, false, false, false, false, false);
            Assert.That(engine.Evaluate(stop, 0).PrimaryCue, Is.EqualTo(FeedbackCue.EmergencyStop));
            Assert.That(engine.Evaluate(stop, 0).VisualStrength, Is.EqualTo(1));
        }

        [Test]
        public void StatisticsUseElapsedActiveTimeAndKeepInvalidIntervals()
        {
            var seam = new DirectedSpline("seam", new[] { new Vec3(0, 0, 0), new Vec3(.1, 0, 0) });
            var c = new CoverageSnapshot("seam", 1, .1, new[] { new CoverageInterval(0, .02, CoverageQuality.Acceptable, 1, 1) }, CoverageCloseReason.None);
            var result = new FusionResult(true, true, false, FusionQualityFlags.None, FusionBlockReason.None, default, c, default);
            var stats = new FusionAttemptStatistics();
            stats.Add(0, Path(seam, 0), result, true, .1);
            stats.Add(.05, Path(seam, .001), result, true, .1);
            var off = new FusionResult(false, false, false, FusionQualityFlags.None, FusionBlockReason.InvalidEvaluation, default, c, default);
            stats.Add(.25, null, off, true, .1);
            var s = stats.Finish(false);
            Assert.That(s.activeSeconds, Is.EqualTo(.05).Within(1e-9));
            Assert.That(s.invalidSeconds, Is.EqualTo(.2).Within(1e-9));
            Assert.That(s.blockedTriggerSeconds, Is.EqualTo(.2).Within(1e-9));
            Assert.That(s.attemptedMetres, Is.EqualTo(.02));
            Assert.That(s.completed, Is.False);
        }

        static PathMetrics Path(DirectedSpline seam, double arc) => new("session", "attempt", "content", seam.Id, true, seam.Project(new Vec3(arc, 0, 0)), default, 0, 0, 0, true, .025, .025, SpeedClass.SpeedCorrect, true, 0, true, 0, MotionFlags.Forward);
    }
}
