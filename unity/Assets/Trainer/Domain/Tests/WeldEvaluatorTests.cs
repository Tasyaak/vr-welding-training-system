using NUnit.Framework;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Domain.Tests
{
    public sealed class WeldEvaluatorTests
    {
        private static WeldEvaluator MakeEvaluator(SeamTolerance tol = null)
        {
            var seam = new StraightSeam(new Vector3(0, 0, 0), new Vector3(0.5f, 0, 0));
            tol ??= new SeamTolerance
            {
                GoodDistanceMetres = 0.025f,
                MaxDistanceMetres = 0.05f,
                MinSpeedMetresPerSecond = 0.03f,
                MaxSpeedMetresPerSecond = 0.20f,
                StartProgressThreshold = 0.08f,
                CompletionThreshold = 0.97f,
                MaxProgressJump = 0.08f,
                ReverseProgressTolerance = 0.03f,
            };
            return new WeldEvaluator(seam, tol, Vector3.forward);
        }

        private static ToolSample TrackedAt(float t, float x, bool trigger = false) =>
            new(t, new Vector3(x, 0, 0), Quaternion.identity, true, trigger);

        [Test]
        public void UntrackedSampleReturnsSuspended()
        {
            var ev = MakeEvaluator();
            ev.BeginAttempt();
            var result = ev.Evaluate(ToolSample.Untracked(0f));
            Assert.AreEqual(WeldingStatus.Suspended, result.Status);
            Assert.IsFalse(result.WeldingActive);
        }

        [Test]
        public void TriggerPressedBeforeStartThresholdIsInhibited()
        {
            var ev = MakeEvaluator();
            ev.BeginAttempt();
            // First sample at x=0.25 (halfway = 50% progress, past 8% threshold)
            // but we start at x=0.25 > startProgressThreshold * 0.5m = 0.04m
            ev.Evaluate(TrackedAt(0f, 0.25f, false)); // marks _startedWithinThreshold false
            var result = ev.Evaluate(new ToolSample(0.1f, new Vector3(0.25f, 0, 0),
                Quaternion.identity, true, true));
            // Started outside threshold so weldingAllowed = false -> Inhibited
            Assert.AreEqual(WeldingStatus.Inhibited, result.Status);
        }

        [Test]
        public void TriggerPressedInGoodPositionIsActive()
        {
            var ev = MakeEvaluator();
            ev.BeginAttempt();
            // Start at beginning, good distance, with trigger
            var r0 = ev.Evaluate(TrackedAt(0f, 0f, false));
            Assert.AreEqual(WeldingStatus.Active, r0.Status);
            var r1 = ev.Evaluate(new ToolSample(0.1f, new Vector3(0.01f, 0, 0),
                Quaternion.identity, true, true));
            Assert.AreEqual(WeldingStatus.Active, r1.Status);
            Assert.IsTrue(r1.WeldingActive);
        }

        [Test]
        public void ForwardJumpIsInhibited()
        {
            var ev = MakeEvaluator();
            ev.BeginAttempt();
            ev.Evaluate(TrackedAt(0f, 0f, false)); // establishes start
            // Jump 60% of 0.5m = 0.3m forward in one step -> progressDelta = 0.6 > MaxProgressJump
            var result = ev.Evaluate(TrackedAt(0.1f, 0.3f, true));
            Assert.IsFalse(result.WeldingActive);
        }

        [Test]
        public void DistanceTooFarMakesDistanceGoodFalse()
        {
            var ev = MakeEvaluator();
            ev.BeginAttempt();
            ev.Evaluate(TrackedAt(0f, 0f));
            var result = ev.Evaluate(
                new ToolSample(0.1f, new Vector3(0.05f, 0.05f, 0), Quaternion.identity, true, false));
            Assert.IsFalse(result.DistanceGood);
        }

        [Test]
        public void IsCompleteReturnsTrueAtThreshold()
        {
            var ev = MakeEvaluator();
            Assert.IsTrue(ev.IsComplete(0.97f));
            Assert.IsFalse(ev.IsComplete(0.96f));
        }

        [Test]
        public void MetricsAccumulateCorrectly()
        {
            var ev = MakeEvaluator();
            ev.BeginAttempt();
            var metrics = new AttemptMetrics();

            var r0 = ev.Evaluate(TrackedAt(0f, 0f, false));
            metrics.RecordSample(r0, 0f);

            var r1 = ev.Evaluate(new ToolSample(1f, new Vector3(0.02f, 0, 0),
                Quaternion.identity, true, true));
            metrics.RecordSample(r1, 1f);

            Assert.AreEqual(1f, metrics.AttemptElapsedSeconds, 1e-4f);
        }
    }
}
