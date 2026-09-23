using System;

namespace WeldingTrainer.Content.Spatial
{
    // Mutable authoring/wire records only. Validated snapshots never expose these instances.
    [Serializable]
    public struct Vec3
    {
        public double x, y, z;
        public Vec3(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public double Length => Math.Sqrt(Dot(this, this));

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3 operator *(Vec3 a, double b) => new Vec3(a.x * b, a.y * b, a.z * b);
        public static double Dot(Vec3 a, Vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }

    [Serializable]
    public struct Quat
    {
        public double x, y, z, w;
        public Quat(double x, double y, double z, double w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public Quat Conjugate => new Quat(-x, -y, -z, w);

        public Vec3 Rotate(Vec3 v)
        {
            var q = new Vec3(x, y, z);
            return v + Vec3.Cross(q, v) * (2 * w) + Vec3.Cross(q, Vec3.Cross(q, v)) * 2;
        }

        public static Quat operator *(Quat a, Quat b) => new Quat(a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x, a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
    }

    [Serializable]
    public sealed class PoseData
    {
        public string destination, source;
        public Vec3 position;
        public Quat rotation;
        public double scale;
    }

    [Serializable]
    public sealed class SourceData
    {
        public string id, path, sha256, units;
        public int revision;
    }

    [Serializable]
    public sealed class BakeInputData
    {
        public string path, sha256;
    }

    [Serializable]
    public sealed class ImportData
    {
        public string sourceId, converter, converterVersion, axisMap, pivot, geometryFile, geometrySha256, visualFile, visualSha256;
        public double millimetresToMetres, chordToleranceMetres, angularToleranceRadians;
        public PoseData authoredFromCadMetres;
    }

    [Serializable]
    public sealed class MountRegionData
    {
        public string id;
        public int revision, sourceFace;
        public double widthMetres, heightMetres, surroundingTopMetres;
        public PoseData fixtureFromRegion;
        public Vec3 outwardNormal;
    }

    [Serializable]
    public sealed class FixtureData
    {
        public string id, sourceId, geometryId;
        public int revision;
        public MountRegionData markerMountRegion;
    }

    [Serializable]
    public sealed class WorkpieceData
    {
        public string id, sourceId, geometryId;
        public int revision;
    }

    [Serializable]
    public sealed class PrintCandidateData
    {
        public int schemaVersion, revision;
        public string id, payload, symbolQuietZoneConvention, artworkProvenance, specificationEvidence;
        public double labelWidthMetres, labelHeightMetres, labelThicknessMetres;
        public double symbolWidthMetres, symbolHeightMetres;
        public bool symbolCentered;
    }

    [Serializable]
    public sealed class MarkerData
    {
        public string id, payloadPrefix, quietZoneConvention, installationEvidence;
        public int revision;
        public bool dimensionsKnown, physicalPlaneKnown;
        public double widthMetres, heightMetres, labelThicknessMetres;
        // Alternatives for ONE mounting location, not additional active markers.
        public PrintCandidateData[] printCandidates;
        public string selectedPrintCandidateId;
    }

    [Serializable]
    public sealed class BindingData
    {
        public string id, partId, fixtureId, markerId, mountRegionId, qualification, qualificationEvidence, operatorConfirmation, invalidationInstructions;
        public int revision, partRevision, fixtureRevision, markerRevision, mountRevision, mountRegionRevision;
        public bool active;
        public PoseData fixtureFromWorkpiece, fixtureFromMarker;
    }

    [Serializable]
    public sealed class ToolSetupData
    {
        public string id, evidence, note;
        public int revision;
        public bool qualified;
        public PoseData controllerFromTool, toolFromTip;
    }

    [Serializable]
    public sealed class CatalogData
    {
        public int schemaVersion, revision;
        public string id, units;
        public SourceData[] sources;
        public BakeInputData[] bakeInputs;
        public ImportData[] imports;
        public FixtureData[] fixtures;
        public WorkpieceData[] workpieces;
        public MarkerData[] markers;
        public BindingData[] bindings;
        public ToolSetupData[] toolSetups;
    }

    [Serializable]
    public sealed class SurfaceData
    {
        public string id;
        public int sourceFace;
        public Vec3 a, b, c, normal;
    }

    [Serializable]
    public sealed class SeamData
    {
        public string id, authoringEvidence;
        // One supporting finite triangle per segment. Split at triangle boundaries.
        public string[] surfaceIds;
        // Optional second finite support per segment for an explicitly authored joint.
        public string[] adjacentSurfaceIds;
        public Vec3[] points;
        public double[] arcLengthsMetres;
    }

    [Serializable]
    public sealed class CleaningData
    {
        public string id, phase, authoringEvidence;
        // Triangulated mask: each triangle is contained in its named finite surface.
        public SurfaceData[] triangles;
        public string[] surfaceIds;
    }

    [Serializable]
    public sealed class GeometryData
    {
        public int schemaVersion, revision;
        public string id, frame, units, sourceSha256, semanticStatus, semanticReason;
        public bool closedSolid;
        public SurfaceData[] surfaces;
        public SeamData[] seams;
        public CleaningData[] cleaningRegions;
    }

    [Serializable]
    public sealed class VisualData
    {
        public int schemaVersion;
        public string frame, units;
        public Vec3[] vertices;
        public int[] triangles;
    }

    public interface ISpatialJson
    {
        T Read<T>(string json);
    }
}
