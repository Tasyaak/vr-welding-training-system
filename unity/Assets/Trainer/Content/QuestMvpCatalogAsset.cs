using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Content
{
    [Serializable] public sealed class ReferencePointAuthoring { public string id; public Vector3 positionMetres; }
    [Serializable] public sealed class SurfaceAuthoring
    { public string id; public List<Vector3> boundaryMetres = new(); public Vector3 outwardNormal = Vector3.up; public int approachSide = 1; }
    [Serializable] public sealed class SeamAuthoring
    { public string id; public string surfaceId; public List<Vector3> bakedPointsMetres = new(); public float bakeErrorMetres = 0.0005f; }
    [Serializable] public sealed class RegionAuthoring
    { public string id; public string surfaceId; public string sourceSeamId; public RegionPurpose purpose; public List<Vector3> boundaryMetres = new(); }
    [Serializable] public sealed class RangeAuthoring { public float minimum; public float maximum; }

    [Serializable]
    public sealed class ProcessProfileAuthoring
    {
        public string id;
        public int schemaVersion = 1;
        public ProcessMode mode;
        [Header("Shared training-model settings")]
        public Vector2 footprintMetres = new(0.01f, 0.01f);
        public RangeAuthoring speedMetresPerSecond = new() { minimum = 0.05f, maximum = 0.15f };
        public RangeAuthoring standoffMetres = new() { minimum = 0f, maximum = 0.01f };
        [Tooltip("Inspector degrees; converted to Domain radians when baked.")]
        public RangeAuthoring workAngleDegrees = new() { minimum = 35f, maximum = 55f };
        [Tooltip("Inspector degrees; converted to Domain radians when baked.")]
        public RangeAuthoring travelAngleDegrees = new() { minimum = -15f, maximum = 15f };
        public float contactEnterMetres = 0.002f;
        public float contactExitMetres = 0.004f;
        public float maximumSampleGapSeconds = 0.1f;
        [Tooltip("Inspector degrees; converted to Domain radians when baked.")]
        public float reflectionConeDegrees = 10f;
        public float reflectionTargetMarginMetres = 0.05f;
        [Range(0, 100)] public float assistancePercent = 100f;
        [Header("Used only by Wobble")]
        public float wobbleWidthMetres = 0.005f;
        public float wobbleFrequencyHertz = 4f;
        [Header("Used only by Pulsed")]
        public float pulseOnSeconds = 0.05f;
        public float pulseOffSeconds = 0.05f;
        [Header("Virtual nozzle selection (does not change rigid contact-tip geometry)")]
        public string requiredNozzleId = "welding-nozzle";
    }

    [CreateAssetMenu(fileName = "QuestMvpCatalog", menuName = "Welding Trainer/Quest MVP Catalog")]
    public sealed class QuestMvpCatalogAsset : ScriptableObject
    {
        public string catalogId = "sample-plate-catalog";
        [Header("Fixture")]
        public string fixtureId = "sample-fixture";
        public int fixtureVersion = 1;
        public Vector3 nominalDimensionsMetres = new(0.5f, 0.02f, 0.3f);
        public Vector3 workpieceFromFixturePositionMetres;
        public Vector3 workpieceFromFixtureEulerDegrees;
        [Tooltip("Imported root scale. Baking rejects any value other than (1,1,1).")]
        public Vector3 authoredScale = Vector3.one;
        public List<ReferencePointAuthoring> referencePoints = new();
        public int samplesPerPoint = 20;
        public float stabilityRadiusMetres = 0.003f;
        public float maxResidualMetres = 0.005f;
        public float minPointSeparationMetres = 0.05f;
        public float minTriangleAreaSquareMetres = 0.002f;

        [Header("Workpiece")]
        public string workpieceId = "sample-workpiece";
        public int workpieceVersion = 1;
        public List<SurfaceAuthoring> surfaces = new();
        public List<SeamAuthoring> seams = new();
        public List<RegionAuthoring> regions = new();

        [Header("Right Touch Plus tool")]
        public string toolId = "sample-right-tool";
        public int toolVersion = 1;
        public Vector3 controllerToToolPositionMetres;
        public Vector3 controllerToToolEulerDegrees;
        public Vector3 toolToEffectiveTipPositionMetres = new(0, 0, 0.12f);
        public Vector3 toolToEffectiveTipEulerDegrees;
        public Vector3 backwardToolAxis = Vector3.back;
        public Vector3 incidentBeamDirectionFromHead = Vector3.forward;

        [Header("Exactly one profile per process mode")]
        public List<ProcessProfileAuthoring> profiles = new();

        public ContentCatalogEntry Bake()
        {
            if ((authoredScale - Vector3.one).sqrMagnitude > 1e-10f)
                throw new ContentValidationException(new[] { new ValidationIssue("authoredScale", "must be exactly (1,1,1); apply imported scale before baking") });

            var fixture = new FixtureDefinition(fixtureId, fixtureVersion,
                referencePoints.Select(x => new ReferencePointDefinition(x.id, UnityDomainConversion.ToDomainVector(x.positionMetres))),
                UnityDomainConversion.ToDomainVector(nominalDimensionsMetres), ToPose(workpieceFromFixturePositionMetres, workpieceFromFixtureEulerDegrees),
                new CalibrationPolicy(samplesPerPoint, stabilityRadiusMetres, maxResidualMetres,
                    minPointSeparationMetres, minTriangleAreaSquareMetres));
            var workpiece = new WorkpieceDefinition(workpieceId, workpieceVersion,
                surfaces.Select(x => new SurfacePatch(x.id, x.boundaryMetres.Select(UnityDomainConversion.ToDomainVector), UnityDomainConversion.ToDomainVector(x.outwardNormal), x.approachSide)),
                seams.Select(x => BakeSeam(x)),
                regions.Select(x => new TargetRegion(x.id, x.surfaceId, x.sourceSeamId, x.purpose, x.boundaryMetres.Select(UnityDomainConversion.ToDomainVector))));
            var tool = new ToolDefinition(toolId, toolVersion, GripPoseConvention.RightHandTouchPlusGrip,
                ToPose(controllerToToolPositionMetres, controllerToToolEulerDegrees),
                ToPose(toolToEffectiveTipPositionMetres, toolToEffectiveTipEulerDegrees),
                UnityDomainConversion.ToDomainVector(backwardToolAxis), UnityDomainConversion.ToDomainVector(incidentBeamDirectionFromHead));
            var entry = new ContentCatalogEntry(catalogId, fixture, workpiece, tool, profiles.Select(BakeProfile));
            entry.Validate().ThrowIfInvalid();
            return entry;
        }

        public ContentSnapshot FreezeForAttempt() => ContentSnapshot.Freeze(Bake());

        private static DirectedSeam BakeSeam(SeamAuthoring seam)
        {
            var points = seam.bakedPointsMetres.Select(UnityDomainConversion.ToDomainVector).ToArray();
            var arc = new double[points.Length];
            for (int i = 1; i < points.Length; i++) arc[i] = arc[i - 1] + Vector3d.Distance(points[i - 1], points[i]);
            return new DirectedSeam(seam.id, seam.surfaceId, points, arc, seam.bakeErrorMetres);
        }

        private static ProcessProfile BakeProfile(ProcessProfileAuthoring p)
        {
            ProcessSettings settings = p.mode switch
            {
                ProcessMode.Fusion => new FusionSettings(),
                ProcessMode.Wobble => new WobbleSettings(p.wobbleWidthMetres, p.wobbleFrequencyHertz),
                ProcessMode.Pulsed => new PulsedSettings(p.pulseOnSeconds, p.pulseOffSeconds),
                ProcessMode.PreWeldCleaning => new CleaningSettings(ProcessMode.PreWeldCleaning),
                ProcessMode.PostWeldCleaning => new CleaningSettings(ProcessMode.PostWeldCleaning),
                _ => throw new ArgumentOutOfRangeException()
            };
            const double degToRad = Math.PI / 180.0;
            return new ProcessProfile(p.id, p.schemaVersion, settings, p.requiredNozzleId,
                p.footprintMetres.x, p.footprintMetres.y,
                new RangeSetting(p.speedMetresPerSecond.minimum, p.speedMetresPerSecond.maximum),
                new RangeSetting(p.standoffMetres.minimum, p.standoffMetres.maximum),
                new RangeSetting(p.workAngleDegrees.minimum * degToRad, p.workAngleDegrees.maximum * degToRad),
                new RangeSetting(p.travelAngleDegrees.minimum * degToRad, p.travelAngleDegrees.maximum * degToRad),
                p.contactEnterMetres, p.contactExitMetres, p.maximumSampleGapSeconds,
                p.reflectionConeDegrees * degToRad, p.reflectionTargetMarginMetres, p.assistancePercent);
        }

        private static RigidPose ToPose(Vector3 position, Vector3 eulerDegrees)
        {
            Quaternion q = Quaternion.Euler(eulerDegrees);
            return UnityDomainConversion.ToDomainPose(position, q);
        }
    }
}
