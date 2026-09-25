using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using WeldingTrainer.Domain;
using WeldingTrainer.Presentation;

namespace WeldingTrainer.Presentation.Tests
{
    public sealed class ProgressiveBeadRendererTests
    {
        private GameObject root;
        private ProgressiveBeadRenderer renderer;
        private DirectedSpline seam;
        private Material acceptableMaterial;
        private Material poorMaterial;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("WorkpieceRoot");

            var rendererObject = new GameObject("ProgressiveBeadRenderer");
            rendererObject.transform.SetParent(root.transform, false);

            renderer = rendererObject.AddComponent<ProgressiveBeadRenderer>();

            Shader shader = Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);

            acceptableMaterial = new Material(shader);
            poorMaterial = new Material(shader);

            SetPrivateField(
                renderer,
                "acceptableMaterial",
                acceptableMaterial);

            SetPrivateField(
                renderer,
                "poorMaterial",
                poorMaterial);

            SetPrivateField(
                renderer,
                "maximumChunks",
                8);

            seam = new DirectedSpline(
                "seam",
                new[]
                {
                    new Vec3(0, 0, 0),
                    new Vec3(1, 0, 0)
                });

            renderer.Configure(seam);
        }

        [TearDown]
        public void TearDown()
        {
            if (acceptableMaterial != null)
                UnityEngine.Object.DestroyImmediate(acceptableMaterial);

            if (poorMaterial != null)
                UnityEngine.Object.DestroyImmediate(poorMaterial);

            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void OneAcceptableIntervalCreatesOneActiveChunk()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable)));

            LineRenderer[] chunks = Chunks();

            Assert.That(chunks.Length, Is.EqualTo(1));
            Assert.That(chunks[0].gameObject.activeSelf, Is.True);
        }

        [Test]
        public void AcceptableAndSeparatedPoorIntervalsCreateTwoChunks()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable),
                    Interval(
                        0.6,
                        0.8,
                        CoverageQuality.Poor)));

            LineRenderer[] chunks = Chunks();

            Assert.That(chunks.Length, Is.EqualTo(2));
            Assert.That(chunks.All(x => x.gameObject.activeSelf), Is.True);
        }

        [Test]
        public void SeparatedIntervalsDoNotBridgeGap()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable),
                    Interval(
                        0.6,
                        0.8,
                        CoverageQuality.Poor)));

            LineRenderer[] chunks = Chunks();

            Assert.That(chunks.Length, Is.EqualTo(2));

            Assert.That(
                chunks[0].GetPosition(chunks[0].positionCount - 1).x,
                Is.EqualTo(0.3f).Within(1e-5f));

            Assert.That(
                chunks[1].GetPosition(0).x,
                Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void ChunksUseLocalCoordinates()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable)));

            Assert.That(Chunks()[0].useWorldSpace, Is.False);
        }

        [Test]
        public void AcceptableIntervalUsesAcceptableMaterial()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable)));

            Assert.That(
                Chunks()[0].sharedMaterial,
                Is.SameAs(acceptableMaterial));
        }

        [Test]
        public void PoorIntervalUsesPoorMaterial()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Poor)));

            Assert.That(
                Chunks()[0].sharedMaterial,
                Is.SameAs(poorMaterial));
        }

        [Test]
        public void ReapplyingSameRevisionDoesNotCreateNewChunks()
        {
            CoverageSnapshot snapshot = Snapshot(
                revision: 1,
                Interval(
                    0.1,
                    0.3,
                    CoverageQuality.Acceptable));

            renderer.Apply(snapshot);

            LineRenderer firstChunk = Chunks()[0];
            int childCountBefore = renderer.transform.childCount;

            renderer.Apply(snapshot);

            Assert.That(
                renderer.transform.childCount,
                Is.EqualTo(childCountBefore));

            Assert.That(
                Chunks()[0],
                Is.SameAs(firstChunk));
        }

        [Test]
        public void ReducingIntervalCountDeactivatesUnusedPooledChunks()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable),
                    Interval(
                        0.6,
                        0.8,
                        CoverageQuality.Poor)));

            LineRenderer[] originalChunks = Chunks();

            Assert.That(originalChunks.Length, Is.EqualTo(2));

            renderer.Apply(
                Snapshot(
                    revision: 2,
                    Interval(
                        0.2,
                        0.4,
                        CoverageQuality.Acceptable)));

            LineRenderer[] chunks = Chunks();

            Assert.That(chunks.Length, Is.EqualTo(2));
            Assert.That(chunks[0].gameObject.activeSelf, Is.True);
            Assert.That(chunks[1].gameObject.activeSelf, Is.False);

            // Pool reused, rather than destroyed/recreated.
            Assert.That(chunks[0], Is.SameAs(originalChunks[0]));
            Assert.That(chunks[1], Is.SameAs(originalChunks[1]));
        }

        [Test]
        public void WrongSeamIdFailsFast()
        {
            var wrongSnapshot = new CoverageSnapshot(
                "other-seam",
                1,
                seam.LengthMetres,
                new[]
                {
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable)
                },
                CoverageCloseReason.None);

            Assert.Throws<ArgumentException>(
                () => renderer.Apply(wrongSnapshot));
        }

        [Test]
        public void IntervalCountAboveMaximumFailsBeforeAllocation()
        {
            SetPrivateField(
                renderer,
                "maximumChunks",
                1);

            var snapshot = Snapshot(
                revision: 1,
                Interval(
                    0.1,
                    0.3,
                    CoverageQuality.Acceptable),
                Interval(
                    0.6,
                    0.8,
                    CoverageQuality.Poor));

            Assert.Throws<InvalidOperationException>(
                () => renderer.Apply(snapshot));

            Assert.That(renderer.transform.childCount, Is.EqualTo(0));
        }
        [Test]
        public void MovingWorkpieceRootMovesBeadWithoutChangingLocalVertices()
        {
            renderer.Apply(
                Snapshot(
                    revision: 1,
                    Interval(
                        0.1,
                        0.3,
                        CoverageQuality.Acceptable)));

            LineRenderer chunk = Chunks()[0];

            Vector3 localBefore = chunk.GetPosition(0);
            Vector3 worldBefore =
                chunk.transform.TransformPoint(localBefore);

            root.transform.position = new Vector3(5f, 2f, -3f);
            root.transform.rotation =
                Quaternion.Euler(0f, 90f, 0f);

            Vector3 localAfter = chunk.GetPosition(0);
            Vector3 worldAfter =
                chunk.transform.TransformPoint(localAfter);

            Assert.That(localAfter, Is.EqualTo(localBefore));
            Assert.That(worldAfter, Is.Not.EqualTo(worldBefore));
        }

        private CoverageSnapshot Snapshot(
            long revision,
            params CoverageInterval[] intervals)
        {
            return new CoverageSnapshot(
                seam.Id,
                revision,
                seam.LengthMetres,
                intervals,
                CoverageCloseReason.None);
        }

        private static CoverageInterval Interval(
            double start,
            double end,
            CoverageQuality quality)
        {
            return new CoverageInterval(
                start,
                end,
                quality,
                visits: 1,
                segmentId: 1);
        }

        private LineRenderer[] Chunks()
        {
            return renderer
                .GetComponentsInChildren<LineRenderer>(true)
                .OrderBy(x => x.transform.GetSiblingIndex())
                .ToArray();
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            FieldInfo field = target
                .GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);

            Assert.That(
                field,
                Is.Not.Null,
                $"Expected private field '{fieldName}'.");

            field.SetValue(target, value);
        }
    }
}