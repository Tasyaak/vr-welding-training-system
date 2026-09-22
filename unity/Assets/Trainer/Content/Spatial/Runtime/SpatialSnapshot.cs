using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WeldingTrainer.Content.Spatial
{
    public sealed class SurfaceSnapshot
    {
        public string Id { get; }
        public int SourceFace { get; }
        public Vec3 A { get; }
        public Vec3 B { get; }
        public Vec3 C { get; }
        public Vec3 Normal { get; }

        internal SurfaceSnapshot(SurfaceData s)
        {
            Id = s.id;
            SourceFace = s.sourceFace;
            A = s.a;
            B = s.b;
            C = s.c;
            Normal = s.normal;
        }
    }

    public sealed class SeamSnapshot
    {
        public string Id { get; }
        public string AuthoringEvidence { get; }
        public IReadOnlyList<Vec3> Points { get; }
        public IReadOnlyList<double> ArcLengthsMetres { get; }
        public IReadOnlyList<string> SurfaceIds { get; }

        internal SeamSnapshot(SeamData s)
        {
            Id = s.id;
            AuthoringEvidence = s.authoringEvidence;
            Points = Array.AsReadOnly((Vec3[])s.points.Clone());
            ArcLengthsMetres = Array.AsReadOnly((double[])s.arcLengthsMetres.Clone());
            SurfaceIds = Array.AsReadOnly((string[])s.surfaceIds.Clone());
        }
    }

    public sealed class CleaningSnapshot
    {
        public string Id { get; }
        public string Phase { get; }
        public string AuthoringEvidence { get; }
        public IReadOnlyList<SurfaceSnapshot> Triangles { get; }
        public IReadOnlyList<string> SurfaceIds { get; }

        internal CleaningSnapshot(CleaningData s)
        {
            Id = s.id;
            Phase = s.phase;
            AuthoringEvidence = s.authoringEvidence;
            Triangles = Array.AsReadOnly(s.triangles.Select(x => new SurfaceSnapshot(x)).ToArray());
            SurfaceIds = Array.AsReadOnly((string[])s.surfaceIds.Clone());
        }
    }

    public sealed class GeometrySnapshot
    {
        public int SchemaVersion => 1;
        public string Id { get; }
        public int Revision { get; }
        public string ContentHash { get; }
        public string Frame { get; }
        public string Units => "m";
        public string SourceSha256 { get; }
        public string SemanticStatus { get; }
        public string SemanticReason { get; }
        public IReadOnlyList<SurfaceSnapshot> Surfaces { get; }
        public IReadOnlyList<SeamSnapshot> Seams { get; }
        public IReadOnlyList<CleaningSnapshot> CleaningRegions { get; }

        internal GeometrySnapshot(GeometryData g, string hash)
        {
            Id = g.id;
            Revision = g.revision;
            ContentHash = hash;
            Frame = g.frame;
            SourceSha256 = g.sourceSha256;
            SemanticStatus = g.semanticStatus;
            SemanticReason = g.semanticReason;
            Surfaces = Array.AsReadOnly(g.surfaces.Select(x => new SurfaceSnapshot(x)).ToArray());
            Seams = Array.AsReadOnly(g.seams.Select(x => new SeamSnapshot(x)).ToArray());
            CleaningRegions = Array.AsReadOnly(g.cleaningRegions.Select(x => new CleaningSnapshot(x)).ToArray());
        }

        public static GeometrySnapshot FromJson(string json, ISpatialJson codec)
        {
            var geometry = codec.Read<GeometryData>(json);
            SpatialValidation.Geometry(geometry);
            return new GeometrySnapshot(geometry, SpatialCatalogSnapshot.Sha256(Encoding.UTF8.GetBytes(json)));
        }
    }

    public sealed class MarkerMountRegionSnapshot
    {
        public string Id { get; }
        public int Revision { get; }
        public int SourceFace { get; }
        public double WidthMetres { get; }
        public double HeightMetres { get; }
        public double SurroundingTopMetres { get; }
        public RigidPose FixtureFromRegion { get; }
        public Vec3 OutwardNormal { get; }

        internal MarkerMountRegionSnapshot(MountRegionData r)
        {
            Id = r.id;
            Revision = r.revision;
            SourceFace = r.sourceFace;
            WidthMetres = r.widthMetres;
            HeightMetres = r.heightMetres;
            SurroundingTopMetres = r.surroundingTopMetres;
            FixtureFromRegion = new RigidPose(r.fixtureFromRegion);
            OutwardNormal = r.outwardNormal;
        }
    }

    public sealed class MarkerSnapshot
    {
        public string Id { get; }
        public int Revision { get; }
        public string PayloadPrefix { get; }
        public double? WidthMetres { get; }
        public double? HeightMetres { get; }
        public double? LabelThicknessMetres { get; }
        public string QuietZoneConvention { get; }
        public string InstallationEvidence { get; }

        internal MarkerSnapshot(MarkerData m)
        {
            Id = m.id;
            Revision = m.revision;
            PayloadPrefix = m.payloadPrefix;
            WidthMetres = m.dimensionsKnown ? (double? )m.widthMetres : null;
            HeightMetres = m.dimensionsKnown ? (double? )m.heightMetres : null;
            LabelThicknessMetres = m.physicalPlaneKnown ? (double? )m.labelThicknessMetres : null;
            QuietZoneConvention = m.quietZoneConvention;
            InstallationEvidence = m.installationEvidence;
        }
    }

    public sealed class ToolSetupSnapshot
    {
        public string Id { get; }
        public int Revision { get; }
        public string Evidence { get; }
        public bool Qualified { get; }
        public RigidPose? ControllerFromTool { get; }
        public RigidPose? ToolFromTip { get; }

        internal ToolSetupSnapshot(ToolSetupData t)
        {
            Id = t.id;
            Revision = t.revision;
            Evidence = t.evidence;
            Qualified = t.qualified;
            ControllerFromTool = t.qualified ? (RigidPose? )new RigidPose(t.controllerFromTool) : null;
            ToolFromTip = t.qualified ? (RigidPose? )new RigidPose(t.toolFromTip) : null;
        }
    }

    public sealed class AssemblyBindingSnapshot
    {
        public string Id { get; }
        public int Revision { get; }
        public string ContentHash { get; }
        public string PartId { get; }
        public int PartRevision { get; }
        public string FixtureId { get; }
        public int FixtureRevision { get; }
        public int MountRevision { get; }
        public string Qualification { get; }
        public string QualificationEvidence { get; }
        public RigidPose FixtureFromWorkpiece { get; }
        public RigidPose FixtureFromMarker { get; }
        public MarkerSnapshot Marker { get; }
        public MarkerMountRegionSnapshot MarkerMountRegion { get; }
        public GeometrySnapshot WorkpieceGeometry { get; }
        public GeometrySnapshot FixtureGeometry { get; }
        public string OperatorConfirmation { get; }
        public string InvalidationInstructions { get; }
        public IReadOnlyList<string> ScoredRegistrationBlockers { get; }
        public bool ReadyForScoredRegistration => ScoredRegistrationBlockers.Count == 0;

        internal AssemblyBindingSnapshot(BindingData b, MarkerData marker, MountRegionData region, GeometrySnapshot part, GeometrySnapshot fixture, string catalogHash)
        {
            Id = b.id;
            Revision = b.revision;
            ContentHash = catalogHash;
            PartId = b.partId;
            PartRevision = b.partRevision;
            FixtureId = b.fixtureId;
            FixtureRevision = b.fixtureRevision;
            MountRevision = b.mountRevision;
            Qualification = b.qualification;
            QualificationEvidence = b.qualificationEvidence;
            FixtureFromWorkpiece = new RigidPose(b.fixtureFromWorkpiece);
            FixtureFromMarker = new RigidPose(b.fixtureFromMarker);
            Marker = new MarkerSnapshot(marker);
            MarkerMountRegion = new MarkerMountRegionSnapshot(region);
            WorkpieceGeometry = part;
            FixtureGeometry = fixture;
            OperatorConfirmation = b.operatorConfirmation;
            InvalidationInstructions = b.invalidationInstructions;
            var reasons = new List<string>();
            if (b.qualification != "Qualified")
                reasons.Add("StationUnqualified: #66 installation/rebolting/error-budget evidence required");
            if (!marker.dimensionsKnown)
                reasons.Add("MarkerDimensionsUnknown: printed size and quiet-zone convention required");
            if (!marker.physicalPlaneKnown)
                reasons.Add("MarkerPlaneUnknown: measured label thickness/installation required");
            if (part.SemanticStatus != "Approved")
                reasons.Add("GeometrySemanticsMissing: " + part.SemanticReason);
            ScoredRegistrationBlockers = reasons.AsReadOnly();
        }
    }

    public sealed class SpatialCatalogSnapshot
    {
        private readonly IReadOnlyDictionary<string, AssemblyBindingSnapshot> bindings;
        public int SchemaVersion => 1;
        public string Id { get; }
        public int Revision { get; }
        public string ContentHash { get; }
        // Lossless versioned structural export for #58. Strings and all typed snapshots are immutable.
        public string CatalogJson { get; }
        public IReadOnlyDictionary<string, string> ArtifactJson { get; }
        public IReadOnlyList<ToolSetupSnapshot> ToolSetups { get; }
        public IEnumerable<AssemblyBindingSnapshot> Bindings => bindings.Values;

        private SpatialCatalogSnapshot(CatalogData c, string json, Dictionary<string, string> artifacts, Dictionary<string, GeometrySnapshot> geometry)
        {
            Id = c.id;
            Revision = c.revision;
            CatalogJson = json;
            ContentHash = Sha256(Encoding.UTF8.GetBytes(json));
            ArtifactJson = new ReadOnlyDictionary<string, string>(artifacts);
            ToolSetups = Array.AsReadOnly(c.toolSetups.Select(x => new ToolSetupSnapshot(x)).ToArray());
            var resolved = new Dictionary<string, AssemblyBindingSnapshot>(StringComparer.Ordinal);
            foreach (var b in c.bindings.Where(x => x.active))
            {
                var f = c.fixtures.Single(x => x.id == b.fixtureId);
                var p = c.workpieces.Single(x => x.id == b.partId);
                var m = c.markers.Single(x => x.id == b.markerId);
                resolved.Add(b.partId, new AssemblyBindingSnapshot(b, m, f.markerMountRegion, geometry[p.sourceId], geometry[f.sourceId], ContentHash));
            }

            bindings = new ReadOnlyDictionary<string, AssemblyBindingSnapshot>(resolved);
        }

        public AssemblyBindingSnapshot ResolveForPreview(string partId)
        {
            SpatialValidation.Id(partId, "partId");
            if (!bindings.TryGetValue(partId, out var binding))
                throw new SpatialContentException("partId: unknown or inactive part " + partId);
            return binding;
        }

        public AssemblyBindingSnapshot ResolveForScoredRegistration(string partId)
        {
            var binding = ResolveForPreview(partId);
            if (!binding.ReadyForScoredRegistration)
                throw new SpatialContentException(partId + ": " + string.Join("; ", binding.ScoredRegistrationBlockers));
            return binding;
        }

        public static string Sha256(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return string.Concat(hash.ComputeHash(bytes).Select(x => x.ToString("x2")));
        }

        public static SpatialCatalogSnapshot Load(string json, ISpatialJson codec, Func<string, byte[]> readArtifact, Func<string, byte[]> readSource = null)
        {
            var c = codec.Read<CatalogData>(json);
            SpatialValidation.Catalog(c);
            var artifacts = new Dictionary<string, string>(StringComparer.Ordinal);
            var geometry = new Dictionary<string, GeometrySnapshot>(StringComparer.Ordinal);
            foreach (var s in c.sources)
                if (readSource != null)
                    SpatialValidation.Require(Sha256(readSource(s.path)) == s.sha256, s.id, "original STEP identity mismatch");
            foreach (var input in c.bakeInputs)
                if (readSource != null)
                    SpatialValidation.Require(Sha256(readSource(input.path)) == input.sha256, input.path, "bake input identity mismatch; run pinned converter again");
            foreach (var i in c.imports)
            {
                var source = c.sources.Single(x => x.id == i.sourceId);
                byte[] geometryBytes = readArtifact(i.geometryFile), visualBytes = readArtifact(i.visualFile);
                SpatialValidation.Require(Sha256(geometryBytes) == i.geometrySha256, i.geometryFile, "geometry hash mismatch; rebake/version content");
                SpatialValidation.Require(Sha256(visualBytes) == i.visualSha256, i.visualFile, "visual hash mismatch; rebake/version content");
                var geometryJson = Encoding.UTF8.GetString(geometryBytes);
                var visualJson = Encoding.UTF8.GetString(visualBytes);
                var g = codec.Read<GeometryData>(geometryJson);
                SpatialValidation.Geometry(g);
                SpatialValidation.Require(g.sourceSha256 == source.sha256 && g.frame == i.authoredFromCadMetres.destination, g.id, "source identity/frame mismatch");
                SpatialValidation.Visual(codec.Read<VisualData>(visualJson), g);
                SpatialValidation.Require(!artifacts.ContainsKey(i.geometryFile) && !artifacts.ContainsKey(i.visualFile) && i.geometryFile != i.visualFile, i.sourceId, "artifact filenames must be unique");
                artifacts.Add(i.geometryFile, geometryJson);
                artifacts.Add(i.visualFile, visualJson);
                geometry.Add(i.sourceId, new GeometrySnapshot(g, i.geometrySha256));
            }

            foreach (var p in c.workpieces)
                SpatialValidation.Require(geometry[p.sourceId].Id == p.geometryId, p.id, "geometry ID mismatch");
            foreach (var f in c.fixtures)
            {
                var g = geometry[f.sourceId];
                SpatialValidation.Require(g.Id == f.geometryId, f.id, "geometry ID mismatch");
                // CAD face selection is explicit; verify complete area, including gaps that point probes miss.
                var r = f.markerMountRegion;
                var pose = new RigidPose(r.fixtureFromRegion);
                var support = g.Surfaces.Where(x => x.SourceFace == r.sourceFace).ToArray();
                SpatialValidation.MountRegionCoverage(support.Select(s => new SurfaceData { id = s.Id, sourceFace = s.SourceFace, a = s.A, b = s.B, c = s.C, normal = s.Normal }), r);
                foreach (var x in new[]
                {
                    -.5,
                    0,
                    .5
                }

                )
                    foreach (var y in new[]
                    {
                        -.5,
                        0,
                        .5
                    }

                    )
                    {
                        var point = pose.TransformPoint(new Vec3(x * r.widthMetres, y * r.heightMetres, 0));
                        SpatialValidation.Require(support.Any(s => Vec3.Dot(s.Normal, r.outwardNormal) > 1 - 1e-6 && SpatialValidation.Contains(new SurfaceData { a = s.A, b = s.B, c = s.C, normal = s.Normal }, point)), r.id, "mount region not supported by selected CAD face");
                    }
            }

            return new SpatialCatalogSnapshot(c, json, artifacts, geometry);
        }
    }
}
