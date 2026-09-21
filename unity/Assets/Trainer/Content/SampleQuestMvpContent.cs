using System.Collections.Generic;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Content
{
    public static class SampleQuestMvpContent
    {
        public static QuestMvpCatalogAsset CreateInMemory()
        {
            var asset = ScriptableObject.CreateInstance<QuestMvpCatalogAsset>();
            asset.catalogId = "sample-quest-mvp-v1";
            asset.fixtureId = "fixture-flat-plate";
            asset.fixtureVersion = 1;
            asset.nominalDimensionsMetres = new Vector3(0.5f, 0.02f, 0.3f);
            asset.referencePoints = new List<ReferencePointAuthoring>
            {
                new() { id = "front-left",  positionMetres = new Vector3(-0.2f, 0, -0.1f) },
                new() { id = "front-right", positionMetres = new Vector3( 0.2f, 0, -0.1f) },
                new() { id = "back-right",  positionMetres = new Vector3( 0.2f, 0,  0.1f) },
                new() { id = "back-left",   positionMetres = new Vector3(-0.2f, 0,  0.1f) }
            };
            asset.surfaces = new List<SurfaceAuthoring>
            {
                new()
                {
                    id = "top-plate", outwardNormal = Vector3.up, approachSide = 1,
                    boundaryMetres = new List<Vector3>
                    {
                        new(-0.25f, 0, -0.15f), new(0.25f, 0, -0.15f),
                        new(0.25f, 0, 0.15f), new(-0.25f, 0, 0.15f)
                    }
                }
            };
            asset.seams = new List<SeamAuthoring>
            {
                new() { id = "seam-a", surfaceId = "top-plate", bakeErrorMetres = 0.0005f,
                    bakedPointsMetres = new List<Vector3> { new(-0.2f, 0, -0.04f), new(0, 0, -0.04f), new(0.2f, 0, -0.04f) } },
                new() { id = "seam-b", surfaceId = "top-plate", bakeErrorMetres = 0.0005f,
                    bakedPointsMetres = new List<Vector3> { new(-0.2f, 0, 0.04f), new(0, 0, 0.04f), new(0.2f, 0, 0.04f) } }
            };
            asset.regions = new List<RegionAuthoring>
            {
                new() { id = "pre-clean-a", surfaceId = "top-plate", purpose = RegionPurpose.PreClean,
                    boundaryMetres = Strip(-0.055f, -0.025f) },
                new() { id = "post-clean-a", surfaceId = "top-plate", sourceSeamId = "seam-a", purpose = RegionPurpose.PostClean,
                    boundaryMetres = Strip(-0.055f, -0.025f) },
                new() { id = "pre-clean-b", surfaceId = "top-plate", purpose = RegionPurpose.PreClean,
                    boundaryMetres = Strip(0.025f, 0.055f) },
                new() { id = "post-clean-b", surfaceId = "top-plate", sourceSeamId = "seam-b", purpose = RegionPurpose.PostClean,
                    boundaryMetres = Strip(0.025f, 0.055f) }
            };
            asset.profiles = new List<ProcessProfileAuthoring>
            {
                Profile("fusion-v1", ProcessMode.Fusion),
                Profile("wobble-v1", ProcessMode.Wobble),
                Profile("pulsed-v1", ProcessMode.Pulsed),
                Profile("pre-clean-v1", ProcessMode.PreWeldCleaning),
                Profile("post-clean-v1", ProcessMode.PostWeldCleaning)
            };
            return asset;
        }

        private static List<Vector3> Strip(float z0, float z1) => new()
        { new(-0.21f, 0, z0), new(0.21f, 0, z0), new(0.21f, 0, z1), new(-0.21f, 0, z1) };

        private static ProcessProfileAuthoring Profile(string id, ProcessMode mode) => new()
        {
            id = id, mode = mode, schemaVersion = 1,
            requiredNozzleId = mode == ProcessMode.PreWeldCleaning || mode == ProcessMode.PostWeldCleaning
                ? "cleaning-nozzle" : "welding-nozzle"
        };
    }
}
