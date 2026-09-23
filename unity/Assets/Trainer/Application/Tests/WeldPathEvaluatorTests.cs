using System;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class WeldPathEvaluatorTests
    {
        private static readonly Vec3 Up = new(0d, 1d, 0d);
        private static readonly Vec3 Down = new(0d, -1d, 0d);
        private static readonly PathEvaluationContext Context =
            new("session-1", "attempt-1", "content-v1", 7, 3);

        private static PathEvaluationPolicy Policy() =>
            new(
                minimumSpeedMps: 0.05,
                maximumSpeedMps: 0.15,
                filterTimeConstantSeconds: 0.10,
                maximumSampleGapSeconds: 0.50,
                maximumArcJumpMetres: 0.25,
                reverseToleranceMetres: 0.005,
                minimumMotionMetres: 0.0001,
                projectionSearchRadiusMetres: 0.50);

        [Test]
        public void StraightProjectionReportsSeamLocalAndTotalError()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(0.1, new Vec3(0.25, 0.01, 0.02)));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.Projection.Progress, Is.EqualTo(0.25).Within(1e-12));
            Assert.That(result.TangentialErrorMetres, Is.EqualTo(0d).Within(1e-12));
            Assert.That(result.NormalErrorMetres, Is.EqualTo(0.01).Within(1e-12));
            Assert.That(result.LateralErrorMetres, Is.EqualTo(0.02).Within(1e-12));
            Assert.That(
                result.TotalErrorMetres,
                Is.EqualTo(Math.Sqrt(0.01 * 0.01 + 0.02 * 0.02)).Within(1e-12));
        }

        [Test]
        public void EndpointProjectionReportsSignedTangentialError()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(0d, new Vec3(-0.02, 0d, 0d)));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.Projection.Progress, Is.EqualTo(0d).Within(1e-12));
            Assert.That(result.TangentialErrorMetres, Is.EqualTo(-0.02).Within(1e-12));
        }

        [Test]
        public void CurvedPolylineProjectsOntoLaterSegment()
        {
            DirectedSpline spline = new(
                "curve",
                new[]
                {
                    new Vec3(0d, 0d, 0d),
                    new Vec3(1d, 0d, 0d),
                    new Vec3(1d, 0d, 1d)
                });
            WeldPathEvaluator evaluator = new(spline, Policy(), Context);

            PathMetrics result = evaluator.Evaluate(
                Sample(0.1, new Vec3(1d, 0.01, 0.5)));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.Projection.SegmentIndex, Is.EqualTo(1));
            Assert.That(result.Projection.ArcLengthMetres, Is.EqualTo(1.5).Within(1e-12));
            Assert.That(result.Projection.Progress, Is.EqualTo(0.75).Within(1e-12));
        }

        [Test]
        public void SharedPolylineVertexIsNotAmbiguous()
        {
            DirectedSpline spline = new(
                "corner",
                new[]
                {
                    new Vec3(0d, 0d, 0d),
                    new Vec3(1d, 0d, 0d),
                    new Vec3(1d, 0d, 1d)
                });

            SeamProjection projection = spline.Project(new Vec3(1d, 0d, 0d));

            Assert.That(projection.Valid, Is.True);
            Assert.That(projection.Ambiguous, Is.False);
            Assert.That(projection.ArcLengthMetres, Is.EqualTo(1d).Within(1e-12));
        }

        [Test]
        public void SelfIntersectionWithoutPriorArcIsAmbiguous()
        {
            DirectedSpline spline = CrossingSpline();
            WeldPathEvaluator evaluator = new(spline, Policy(), Context);

            PathMetrics result = evaluator.Evaluate(
                Sample(0d, new Vec3(0d, 0d, 0d)));

            Assert.That(result.Valid, Is.False);
            Assert.That(result.Projection.Valid, Is.True);
            Assert.That(result.Projection.Ambiguous, Is.True);
            Assert.That(
                result.Flags.HasFlag(MotionFlags.AmbiguousProjection),
                Is.True);
        }

        [Test]
        public void PriorArcStabilizesProjectionAtSelfIntersection()
        {
            DirectedSpline spline = CrossingSpline();
            double firstCrossingArc = Math.Sqrt(2d);

            SeamProjection projection = spline.Project(
                new Vec3(0d, 0d, 0d),
                firstCrossingArc,
                0.2);

            Assert.That(projection.Valid, Is.True);
            Assert.That(projection.Ambiguous, Is.False);
            Assert.That(
                projection.ArcLengthMetres,
                Is.EqualTo(firstCrossingArc).Within(1e-12));
        }

        [TestCase(0.01, SpeedClass.TooSlow)]
        [TestCase(0.10, SpeedClass.SpeedCorrect)]
        [TestCase(0.20, SpeedClass.TooFast)]
        public void SignedActualTimeSpeedProducesExpectedClass(
            double speedMps,
            SpeedClass expectedClass)
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));

            PathMetrics result = evaluator.Evaluate(
                Sample(0.1, new Vec3(speedMps * 0.1, 0d, 0d)));

            Assert.That(result.SpeedValid, Is.True);
            Assert.That(result.SignedSpeedMps, Is.EqualTo(speedMps).Within(1e-12));
            Assert.That(result.FilteredSpeedMps, Is.EqualTo(speedMps).Within(1e-12));
            Assert.That(result.SpeedClass, Is.EqualTo(expectedClass));
            // Assert.That(result.Flags.HasFlag(MotionFlags.Forward), Is.True);
        }

        [Test]
        public void ForwardMotionIsExplicit()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            evaluator.Evaluate(
                Sample(0d, new Vec3(0d, 0d, 0d)));

            PathMetrics result = evaluator.Evaluate(
                Sample(0.1, new Vec3(0.01, 0d, 0d)));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.SpeedValid, Is.True);

            Assert.That(
                result.Flags.HasFlag(MotionFlags.Forward),
                Is.True);

            Assert.That(
                result.Flags.HasFlag(MotionFlags.Reverse),
                Is.False);
        }

        [Test]
        public void ExponentialFilterUsesActualElapsedTime()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));

            PathMetrics first = evaluator.Evaluate(
                Sample(0.1, new Vec3(0.01, 0d, 0d)));
            PathMetrics second = evaluator.Evaluate(
                Sample(0.3, new Vec3(0.05, 0d, 0d)));

            double alpha = 1d - Math.Exp(-0.2 / 0.1);
            double expectedFiltered = 0.1 + alpha * (0.2 - 0.1);

            Assert.That(first.FilteredSpeedMps, Is.EqualTo(0.1).Within(1e-12));
            Assert.That(second.SignedSpeedMps, Is.EqualTo(0.2).Within(1e-12));
            Assert.That(
                second.FilteredSpeedMps,
                Is.EqualTo(expectedFiltered).Within(1e-12));
        }

        [Test]
        public void ReverseMotionIsExplicitAndSpeedRemainsSigned()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(0d, new Vec3(0.40, 0d, 0d)));

            PathMetrics result = evaluator.Evaluate(
                Sample(0.1, new Vec3(0.39, 0d, 0d)));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.SpeedValid, Is.True);
            Assert.That(result.SignedSpeedMps, Is.EqualTo(-0.1).Within(1e-12));
            Assert.That(result.Flags.HasFlag(MotionFlags.Reverse), Is.True);
            Assert.That(result.Projection.Progress, Is.LessThan(0.40));
        }

        [Test]
        public void LargeJumpIsInvalidSkippedRegionAndDoesNotBridgeFilterHistory()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));

            PathMetrics jump = evaluator.Evaluate(
                Sample(0.1, new Vec3(0.50, 0d, 0d)));

            Assert.That(jump.Valid, Is.False);
            Assert.That(jump.SpeedValid, Is.False);
            Assert.That(jump.SpeedClass, Is.EqualTo(SpeedClass.Invalid));
            Assert.That(jump.Flags.HasFlag(MotionFlags.Discontinuity), Is.True);
            Assert.That(jump.Flags.HasFlag(MotionFlags.SkippedRegion), Is.True);
            Assert.That(jump.FilteredSpeedMps, Is.EqualTo(0d));

            PathMetrics recovered = evaluator.Evaluate(
                Sample(0.2, new Vec3(0.51, 0d, 0d)));

            Assert.That(recovered.Valid, Is.True);
            Assert.That(recovered.SpeedValid, Is.True);
            Assert.That(recovered.SignedSpeedMps, Is.EqualTo(0.1).Within(1e-12));
            Assert.That(recovered.FilteredSpeedMps, Is.EqualTo(0.1).Within(1e-12));
            Assert.That(recovered.SpeedClass, Is.EqualTo(SpeedClass.SpeedCorrect));
        }

        [Test]
        public void TrackingGapResetsDerivativeHistory()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));

            PathMetrics gap = evaluator.Evaluate(
                Sample(1d, new Vec3(0.10, 0d, 0d)));

            Assert.That(gap.Valid, Is.False);
            Assert.That(gap.SpeedValid, Is.False);
            Assert.That(gap.Flags.HasFlag(MotionFlags.TrackingGap), Is.True);

            PathMetrics recovered = evaluator.Evaluate(
                Sample(1.1, new Vec3(0.11, 0d, 0d)));

            Assert.That(recovered.Valid, Is.True);
            Assert.That(recovered.SpeedValid, Is.True);
            Assert.That(recovered.SignedSpeedMps, Is.EqualTo(0.1).Within(1e-12));
        }

        [Test]
        public void ExplicitResetPreventsDerivativeBridgeAcrossLifecycleInterruption()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));
            evaluator.Evaluate(Sample(0.1, new Vec3(0.01, 0d, 0d)));

            evaluator.Reset();
            PathMetrics afterReset = evaluator.Evaluate(
                Sample(5d, new Vec3(0.50, 0d, 0d)));

            Assert.That(afterReset.Valid, Is.True);
            Assert.That(afterReset.SpeedValid, Is.False);
            Assert.That(afterReset.SpeedClass, Is.EqualTo(SpeedClass.Invalid));
        }

        [Test]
        public void NonMonotonicTimeIsInvalidAndResetsHistory()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(1d, new Vec3(0d, 0d, 0d)));

            PathMetrics invalid = evaluator.Evaluate(
                Sample(1d, new Vec3(0.01, 0d, 0d)));

            Assert.That(invalid.Valid, Is.False);
            Assert.That(invalid.SpeedValid, Is.False);
            Assert.That(
                invalid.Flags.HasFlag(MotionFlags.NonMonotonicTime),
                Is.True);

            PathMetrics recovered = evaluator.Evaluate(
                Sample(1.1, new Vec3(0.02, 0d, 0d)));

            Assert.That(recovered.SpeedValid, Is.True);
            Assert.That(recovered.SignedSpeedMps, Is.EqualTo(0.1).Within(1e-12));
        }

        [Test]
        public void UnavailableInputCannotProduceValidMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    available: false));

            AssertInvalidFailClosed(result, MotionFlags.InputUnavailable);
        }

        [Test]
        public void InvalidTrackingCannotProduceValidMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    trackingValid: false));

            AssertInvalidFailClosed(result, MotionFlags.TrackingGap);
        }

        [Test]
        public void RegistrationGenerationMismatchCannotProduceValidMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    registrationGeneration: 8));

            AssertInvalidFailClosed(result, MotionFlags.GenerationMismatch);
        }

        [Test]
        public void OriginGenerationMismatchCannotProduceValidMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    originGeneration: 4));

            AssertInvalidFailClosed(result, MotionFlags.GenerationMismatch);
        }

        [Test]
        public void SessionAttemptOrContentMismatchCannotProduceValidMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    attemptId: "different-attempt"));

            AssertInvalidFailClosed(result, MotionFlags.ContextMismatch);
        }

        [Test]
        public void NonFiniteTimestampCannotProduceValidMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(double.NaN, new Vec3(0.1, 0d, 0d)));

            AssertInvalidFailClosed(result, MotionFlags.InvalidInput);
        }

        [Test]
        public void NonFinitePositionCannotProduceValidMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(0d, new Vec3(double.PositiveInfinity, 0d, 0d)));

            AssertInvalidFailClosed(result, MotionFlags.InvalidInput);
        }

        [Test]
        public void InvalidSurfaceNormalCannotProduceSeamLocalMetrics()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    surfaceNormal: new Vec3(0d, 0d, 0d)));

            Assert.That(result.Valid, Is.False);
            Assert.That(
                result.Flags.HasFlag(MotionFlags.InvalidSurfaceFrame),
                Is.True);
        }

        [Test]
        public void TravelAngleIsInvalidAtRest()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            evaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));

            PathMetrics result = evaluator.Evaluate(
                Sample(0.1, new Vec3(0d, 0d, 0d)));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.TravelAngleValid, Is.False);
        }

        [Test]
        public void TravelAngleUsesDocumentedSignedRightHandConvention()
        {
            WeldPathEvaluator positiveEvaluator = StraightEvaluator();
            positiveEvaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));
            PathMetrics positive = positiveEvaluator.Evaluate(
                Sample(0.1, new Vec3(0.01, 0d, -0.01)));

            WeldPathEvaluator negativeEvaluator = StraightEvaluator();
            negativeEvaluator.Evaluate(Sample(0d, new Vec3(0d, 0d, 0d)));
            PathMetrics negative = negativeEvaluator.Evaluate(
                Sample(0.1, new Vec3(0.01, 0d, 0.01)));

            Assert.That(positive.TravelAngleValid, Is.True);
            Assert.That(
                positive.TravelAngleRadians,
                Is.EqualTo(Math.PI / 4d).Within(1e-12));

            Assert.That(negative.TravelAngleValid, Is.True);
            Assert.That(
                negative.TravelAngleRadians,
                Is.EqualTo(-Math.PI / 4d).Within(1e-12));
        }

        [Test]
        public void WorkAngleUsesAcuteUnsignedSurfaceNormalConvention()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();
            Vec3 fortyFiveDegrees = new(0d, -1d, 1d);

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    toolForward: fortyFiveDegrees));

            Assert.That(result.WorkAngleValid, Is.True);
            Assert.That(
                result.WorkAngleRadians,
                Is.EqualTo(Math.PI / 4d).Within(1e-12));
        }

        [Test]
        public void ZeroToolForwardMakesOnlyWorkAngleInvalid()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(
                    0d,
                    new Vec3(0.1, 0d, 0d),
                    toolForward: new Vec3(0d, 0d, 0d)));

            Assert.That(result.Valid, Is.True);
            Assert.That(result.WorkAngleValid, Is.False);
        }

        [Test]
        public void ResultPreservesVersionedEvaluationIdentity()
        {
            WeldPathEvaluator evaluator = StraightEvaluator();

            PathMetrics result = evaluator.Evaluate(
                Sample(0d, new Vec3(0.1, 0d, 0d)));

            Assert.That(PathSample.SchemaVersion, Is.EqualTo(1));
            Assert.That(PathMetrics.SchemaVersion, Is.EqualTo(1));
            Assert.That(result.SessionId, Is.EqualTo(Context.SessionId));
            Assert.That(result.AttemptId, Is.EqualTo(Context.AttemptId));
            Assert.That(result.ContentHash, Is.EqualTo(Context.ContentHash));
            Assert.That(result.SeamId, Is.EqualTo("straight"));
        }

        [Test]
        public void EvaluatorMetricsExposeNoActivationAuthority()
        {
            Assert.That(typeof(PathMetrics).GetProperty("Permission"), Is.Null);
            Assert.That(typeof(PathMetrics).GetProperty("Activation"), Is.Null);
            Assert.That(typeof(WeldPathEvaluator).GetMethod("Arm"), Is.Null);
        }


        [Test]
        public void DefaultEvaluationContextIsRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new WeldPathEvaluator(
                    new DirectedSpline(
                        "straight",
                        new[]
                        {
                            new Vec3(0d, 0d, 0d),
                            new Vec3(1d, 0d, 0d)
                        }),
                    Policy(),
                    default));
        }

        [Test]
        public void DegenerateOrNonFiniteSplineIsRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new DirectedSpline(
                    "degenerate",
                    new[]
                    {
                        new Vec3(0d, 0d, 0d),
                        new Vec3(0d, 0d, 0d)
                    }));

            Assert.Throws<ArgumentException>(() =>
                new DirectedSpline(
                    "nonfinite",
                    new[]
                    {
                        new Vec3(0d, 0d, 0d),
                        new Vec3(double.NaN, 0d, 0d)
                    }));
        }

        [Test]
        public void PolicyRejectsNonFiniteOrInvalidThresholds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PathEvaluationPolicy(
                    double.NaN,
                    0.15,
                    0.1,
                    0.5,
                    0.25,
                    0.005,
                    0.0001,
                    0.5));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PathEvaluationPolicy(
                    0.05,
                    double.PositiveInfinity,
                    0.1,
                    0.5,
                    0.25,
                    0.005,
                    0.0001,
                    0.5));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PathEvaluationPolicy(
                    0.05,
                    0.15,
                    0.1,
                    0.5,
                    0.25,
                    0.005,
                    0.0001,
                    0.10));
        }

        private static WeldPathEvaluator StraightEvaluator() =>
            new(
                new DirectedSpline(
                    "straight",
                    new[]
                    {
                        new Vec3(0d, 0d, 0d),
                        new Vec3(1d, 0d, 0d)
                    }),
                Policy(),
                Context);

        private static DirectedSpline CrossingSpline() =>
            new(
                "crossing",
                new[]
                {
                    new Vec3(-1d, 0d, -1d),
                    new Vec3(1d, 0d, 1d),
                    new Vec3(-1d, 0d, 1d),
                    new Vec3(1d, 0d, -1d)
                });

        private static PathSample Sample(
            double time,
            Vec3 tip,
            Vec3? toolForward = null,
            Vec3? surfaceNormal = null,
            bool available = true,
            bool trackingValid = true,
            long registrationGeneration = 7,
            long originGeneration = 3,
            string sessionId = "session-1",
            string attemptId = "attempt-1",
            string contentHash = "content-v1") =>
            new(
                sessionId,
                attemptId,
                contentHash,
                time,
                tip,
                toolForward ?? Down,
                surfaceNormal ?? Up,
                available,
                trackingValid,
                registrationGeneration,
                originGeneration);

        private static void AssertInvalidFailClosed(
            PathMetrics result,
            MotionFlags expectedFlag)
        {
            Assert.That(result.Valid, Is.False);
            Assert.That(result.SpeedValid, Is.False);
            Assert.That(result.SpeedClass, Is.EqualTo(SpeedClass.Invalid));
            Assert.That(result.Flags.HasFlag(expectedFlag), Is.True);
        }
    }
}
