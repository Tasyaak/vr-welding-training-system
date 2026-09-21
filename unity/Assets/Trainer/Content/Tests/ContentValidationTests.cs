using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Content.Tests
{
    public sealed class ContentValidationTests
    {
        private QuestMvpCatalogAsset _asset;

        [SetUp] public void SetUp() => _asset = SampleQuestMvpContent.CreateInMemory();
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_asset);

        [Test]
        public void SampleCatalog_ExposesRequiredMvpContent()
        {
            ContentCatalogEntry entry = _asset.Bake();
            Assert.That(entry.Validate().IsValid, Is.True);
            Assert.That(entry.Fixture.ReferencePoints.Count, Is.EqualTo(4));
            Assert.That(entry.Workpiece.Surfaces.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(entry.Workpiece.Seams.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(entry.Workpiece.Regions.Any(x => x.Purpose == RegionPurpose.PreClean), Is.True);
            Assert.That(entry.Workpiece.Regions.Any(x => x.Purpose == RegionPurpose.PostClean), Is.True);
            Assert.That(entry.Profiles.Select(x => x.Settings.Mode).Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void ApplicationResolver_RequiresExplicitSelectionAndFreezesContent()
        {
            var resolver = new ContentResolver();
            Assert.Throws<ArgumentNullException>(() => resolver.ResolveForAttempt(null));
            Assert.That(resolver.ResolveForAttempt(_asset.Bake()).ContentHashSha256, Has.Length.EqualTo(64));
        }

        [Test]
        public void UnityDomainPoseConversion_RoundTripsGoldenValues()
        {
            Vector3 position = new(0.125f, -0.25f, 0.5f);
            Quaternion rotation = Quaternion.Euler(10, 20, 30);
            RigidPose domain = UnityDomainConversion.ToDomainPose(position, rotation);
            UnityDomainConversion.ToUnityPose(domain, out Vector3 roundTripPosition, out Quaternion roundTripRotation);
            Assert.That(Vector3.Distance(position, roundTripPosition), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(rotation, roundTripRotation), Is.LessThan(1e-4f));
        }

        [Test]
        public void PlanarRectangle_PassesLayoutValidation() => Assert.DoesNotThrow(() => _asset.Bake());

        [Test]
        public void CollinearPoints_AreRejectedWithFieldReason()
        {
            for (int i = 0; i < 4; i++) _asset.referencePoints[i].positionMetres = new Vector3(i * 0.1f, 0, 0);
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("collinear or insufficiently spread"));
        }

        [Test]
        public void DuplicatePointLabels_AreRejected()
        {
            _asset.referencePoints[1].id = _asset.referencePoints[0].id;
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("duplicates stable ID"));
        }

        [Test]
        public void NonFiniteGeometry_IsRejected()
        {
            _asset.surfaces[0].boundaryMetres[0] = new Vector3(float.NaN, 0, 0);
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("must be finite"));
        }

        [Test]
        public void UnsupportedScale_IsRejectedBeforeBake()
        {
            _asset.authoredScale = new Vector3(2, 1, 1);
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("apply imported scale"));
        }

        [Test]
        public void NonUnitSurfaceNormal_IsRejected()
        {
            _asset.surfaces[0].outwardNormal = new Vector3(0, 2, 0);
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("unit vector"));
        }

        [Test]
        public void InspectorDegrees_AreConvertedToDomainRadians()
        {
            _asset.profiles[0].workAngleDegrees.minimum = 90;
            var profile = _asset.Bake().Profiles.Single(x => x.Settings.Mode == ProcessMode.Fusion);
            Assert.That(profile.WorkAngleRadians.Minimum, Is.EqualTo(Math.PI / 2).Within(1e-6));
        }

        [Test]
        public void CleaningAndWeldingUseSeparateMetricSchemasAndSharedTipGeometry()
        {
            ContentCatalogEntry entry = _asset.Bake();
            ProcessProfile fusion = entry.Profiles.Single(x => x.Settings.Mode == ProcessMode.Fusion);
            ProcessProfile cleaning = entry.Profiles.Single(x => x.Settings.Mode == ProcessMode.PreWeldCleaning);
            Assert.That(fusion.MetricSchemaId, Is.EqualTo("welding-quality-v1"));
            Assert.That(cleaning.MetricSchemaId, Is.EqualTo("cleaning-coverage-v1"));
            Assert.That(fusion.RequiredNozzleId, Is.Not.EqualTo(cleaning.RequiredNozzleId));
            Assert.That(entry.Tool.EffectiveTipFromTool.PositionMetres,
                Is.EqualTo(_asset.FreezeForAttempt().Entry.Tool.EffectiveTipFromTool.PositionMetres));
        }

        [Test]
        public void FrozenSnapshot_DoesNotChangeWhenAuthoringAssetMutates()
        {
            ContentSnapshot snapshot = _asset.FreezeForAttempt();
            string hash = snapshot.ContentHashSha256;
            Vector3d point = snapshot.Entry.Fixture.ReferencePoints[0].FixturePositionMetres;

            _asset.referencePoints[0].positionMetres = new Vector3(99, 99, 99);
            _asset.catalogId = "changed";

            Assert.That(snapshot.ContentHashSha256, Is.EqualTo(hash));
            Assert.That(snapshot.Entry.Fixture.ReferencePoints[0].FixturePositionMetres, Is.EqualTo(point));
        }

        [Test]
        public void AssistanceChange_DoesNotChangeEvaluationHash()
        {
            ContentSnapshot before = _asset.FreezeForAttempt();
            _asset.profiles[0].assistancePercent = 10;
            ContentSnapshot after = _asset.FreezeForAttempt();
            Assert.That(after.EvaluationHashSha256, Is.EqualTo(before.EvaluationHashSha256));
            Assert.That(after.ContentHashSha256, Is.Not.EqualTo(before.ContentHashSha256));
        }

        [Test]
        public void PostCleanRegionWithoutSourceSeam_IsRejected()
        {
            var post = _asset.regions.First(x => x.purpose == RegionPurpose.PostClean);
            post.sourceSeamId = "missing";
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("sourceSeamId"));
        }

        [Test]
        public void BakeErrorMustBeBelowTightestTolerance()
        {
            _asset.seams[0].bakeErrorMetres = 0.1f;
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("tightest scoring tolerance"));
        }

        [Test]
        public void SeamOutsideFiniteSurface_IsRejected()
        {
            _asset.seams[0].bakedPointsMetres[1] = new Vector3(10, 0, 10);
            var exception = Assert.Throws<ContentValidationException>(() => _asset.Bake());
            Assert.That(exception.Message, Does.Contain("outside the referenced finite surface bounds"));
        }
    }
}
