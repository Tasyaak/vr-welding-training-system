using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace WeldingTrainer.Content.Spatial
{
    public sealed class SpatialContentException : Exception
    {
        public SpatialContentException(string message) : base(message)
        {
        }
    }

    public static class SpatialValidation
    {
        public const double LengthTolerance = 1e-7;
        public static void Require(bool condition, string path, string message)
        {
            if (!condition)
                throw new SpatialContentException(path + ": " + message);
        }

        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static void Number(double v, string path) => Require(Finite(v), path, "must be finite");
        public static void Positive(double v, string path) => Require(Finite(v) && v > 0, path, "must be finite and positive");
        public static void Vector(Vec3 v, string path)
        {
            Number(v.x, path + ".x");
            Number(v.y, path + ".y");
            Number(v.z, path + ".z");
        }

        public static void Unit(Vec3 v, string path)
        {
            Vector(v, path);
            Require(Math.Abs(v.Length - 1) < 1e-6, path, "expected unit normal");
        }

        public static void Id(string id, string path) => Require(id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\z"), path, "expected 1..64 ASCII identifier characters");
        public static void Hash(string hash, string path) => Require(hash != null && Regex.IsMatch(hash, @"\A[0-9a-f]{64}\z"), path, "expected lowercase SHA-256 content identity");
        public static void Text(string text, string path) => Require(!string.IsNullOrWhiteSpace(text), path, "required evidence or explanation missing");
        public static void Pose(PoseData pose, string destination, string source, string path)
        {
            Require(pose != null, path, "missing rigid pose");
            Id(pose.destination, path + ".destination");
            Id(pose.source, path + ".source");
            Require(pose.destination == destination && pose.source == source, path, "wrong transform direction/frames");
            Vector(pose.position, path + ".position");
            var q = pose.rotation;
            Require(Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) && Math.Abs(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w - 1) < 1e-8, path, "rotation must be normalized, finite quaternion xyzw");
            Require(pose.scale == 1, path, "rigid scale must be exactly 1; conversion belongs only in CAD bake");
        }

        internal static Dictionary<string, T> Index<T>(T[] items, Func<T, string> id, string path)
            where T : class
        {
            Require(items != null, path, "array missing");
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                Require(item != null, path, "null record");
                var key = id(item);
                Id(key, path);
                Require(!result.ContainsKey(key), path, "duplicate ID " + key);
                result.Add(key, item);
            }

            return result;
        }

        private static void Revision(int value, string path) => Require(value > 0, path, "revision must be positive");
        public static void Catalog(CatalogData c)
        {
            Require(c != null, "catalog", "missing");
            Require(c.schemaVersion == 1, "catalog.schemaVersion", "unsupported schema");
            Id(c.id, "catalog.id");
            Revision(c.revision, "catalog.revision");
            Require(c.units == "m", "catalog.units", "expected metres");
            var sources = Index(c.sources, x => x.id, "sources");
            var fixtures = Index(c.fixtures, x => x.id, "fixtures");
            var parts = Index(c.workpieces, x => x.id, "workpieces");
            var markers = Index(c.markers, x => x.id, "markers");
            Index(c.bindings, x => x.id, "bindings");
            Index(c.toolSetups, x => x.id, "toolSetups");
            Require(sources.Count > 0 && fixtures.Count > 0 && parts.Count > 0 && c.bindings.Length > 0, "catalog", "empty catalog");
            foreach (var s in c.sources)
            {
                Revision(s.revision, s.id);
                Hash(s.sha256, s.id);
                Require(s.units == "mm", s.id, "source units must be mm");
                SafePath(s.path, s.id);
            }

            Require(c.bakeInputs != null && c.bakeInputs.Length > 0, "bakeInputs", "reproducibility inputs missing");
            var inputPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in c.bakeInputs)
            {
                Require(input != null, "bakeInputs", "null input");
                SafePath(input.path, "bakeInputs");
                Hash(input.sha256, input.path);
                Require(inputPaths.Add(input.path), input.path, "duplicate bake input");
            }

            Require(c.imports != null && c.imports.Length == sources.Count, "imports", "one import required per source");
            var imports = Index(c.imports, x => x.sourceId, "imports");
            foreach (var i in c.imports)
            {
                Require(sources.ContainsKey(i.sourceId), i.sourceId, "unknown source");
                Text(i.converter, i.sourceId);
                Text(i.converterVersion, i.sourceId);
                Require(i.millimetresToMetres == .001, i.sourceId, "mm to m must occur exactly once with factor 0.001");
                // v1 fixes axes; rotations/translations remain explicit in the following rigid pose.
                Require(i.axisMap == "x,y,z" && i.pivot == "shared CAD origin", i.sourceId, "unsupported axis/pivot policy");
                Pose(i.authoredFromCadMetres, i.authoredFromCadMetres?.destination, "CadMetres", i.sourceId);
                Positive(i.chordToleranceMetres, i.sourceId);
                Positive(i.angularToleranceRadians, i.sourceId);
                SafeFile(i.geometryFile, i.sourceId);
                SafeFile(i.visualFile, i.sourceId);
                Hash(i.geometrySha256, i.sourceId);
                Hash(i.visualSha256, i.sourceId);
            }

            foreach (var f in c.fixtures)
            {
                Revision(f.revision, f.id);
                Id(f.sourceId, f.id + ".sourceId");
                Require(sources.ContainsKey(f.sourceId), f.id, "unknown source");
                Id(f.geometryId, f.id);
                Require(imports[f.sourceId].authoredFromCadMetres.destination == "Fixture", f.id, "import frame mismatch");
                var r = f.markerMountRegion;
                Require(r != null, f.id, "marker mount region missing");
                Id(r.id, f.id);
                Revision(r.revision, r.id);
                Positive(r.widthMetres, r.id);
                Positive(r.heightMetres, r.id);
                Number(r.surroundingTopMetres, r.id);
                Pose(r.fixtureFromRegion, "Fixture", "MarkerMountRegion", r.id);
                Unit(r.outwardNormal, r.id);
                Require((new RigidPose(r.fixtureFromRegion).TransformDirection(new Vec3(0, 0, 1)) - r.outwardNormal).Length < 1e-6, r.id, "region +Z must equal outward normal");
                Require(r.sourceFace >= 0, r.id, "source face missing");
            }

            foreach (var p in c.workpieces)
            {
                Revision(p.revision, p.id);
                Id(p.sourceId, p.id + ".sourceId");
                Id(p.geometryId, p.id);
                Require(sources.ContainsKey(p.sourceId), p.id, "unknown source");
                Require(imports[p.sourceId].authoredFromCadMetres.destination == "Workpiece", p.id, "import frame mismatch");
            }

            foreach (var m in c.markers)
            {
                Revision(m.revision, m.id);
                Require(m.payloadPrefix == "LW1:", m.id, "unsupported QR identity schema");
                Number(m.widthMetres, m.id);
                Number(m.heightMetres, m.id);
                Number(m.labelThicknessMetres, m.id);
                if (m.dimensionsKnown)
                {
                    Positive(m.widthMetres, m.id);
                    Positive(m.heightMetres, m.id);
                    Require(m.quietZoneConvention == "IncludesQuietZone" || m.quietZoneConvention == "ExcludesQuietZone", m.id, "print convention unknown");
                }
                else
                    Require(m.widthMetres == 0 && m.heightMetres == 0 && m.quietZoneConvention == "Unqualified", m.id, "unknown dimensions require explicit zero sentinel and Unqualified convention");
                if (m.physicalPlaneKnown)
                {
                    Require(m.labelThicknessMetres >= 0, m.id, "negative label thickness");
                    Text(m.installationEvidence, m.id);
                }
                else
                    Require(m.labelThicknessMetres == 0 && string.IsNullOrEmpty(m.installationEvidence), m.id, "unknown plane must not claim physical evidence/thickness");
            }

            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in c.bindings)
            {
                Revision(b.revision, b.id);
                Revision(b.mountRevision, b.id);
                Require(parts.ContainsKey(b.partId ?? "") && fixtures.ContainsKey(b.fixtureId ?? "") && markers.ContainsKey(b.markerId ?? ""), b.id, "unknown part/fixture/marker");
                var p = parts[b.partId];
                var f = fixtures[b.fixtureId];
                var m = markers[b.markerId];
                var r = f.markerMountRegion;
                Require(b.partRevision == p.revision && b.fixtureRevision == f.revision && b.markerRevision == m.revision && b.mountRegionId == r.id && b.mountRegionRevision == r.revision, b.id, "stale referenced revision or region");
                Require(!b.active || active.Add(b.partId), b.id, "duplicate active binding for part " + b.partId);
                Pose(b.fixtureFromWorkpiece, "Fixture", "Workpiece", b.id + ".fixtureFromWorkpiece");
                Pose(b.fixtureFromMarker, "Fixture", "Marker", b.id + ".fixtureFromMarker");
                Text(b.operatorConfirmation, b.id);
                Text(b.invalidationInstructions, b.id);
                Require(b.qualification == "Unqualified" || b.qualification == "Qualified", b.id, "unknown qualification status");
                if (b.qualification == "Qualified")
                {
                    Require(m.dimensionsKnown && m.physicalPlaneKnown, b.id, "qualified binding requires measured marker dimensions/plane");
                    Text(b.qualificationEvidence, b.id);
                }
                else
                    Require(string.IsNullOrEmpty(b.qualificationEvidence), b.id, "unqualified binding cannot claim approval evidence");
                var local = new RigidPose(r.fixtureFromRegion).Inverse() * new RigidPose(b.fixtureFromMarker);
                Require((local.TransformDirection(new Vec3(0, 0, 1)) - new Vec3(0, 0, 1)).Length < 1e-6, b.id, "marker plane normal differs from mount region");
                if (m.physicalPlaneKnown)
                    Require(Math.Abs(local.Position.z - m.labelThicknessMetres) < LengthTolerance, b.id, "marker plane must include qualified label thickness");
                else
                    Require(local.Position.Length < LengthTolerance && Math.Abs(local.Rotation.w) > 1 - 1e-8, b.id, "unqualified marker must use nominal region center and axes");
                if (m.dimensionsKnown)
                    foreach (var x in new[]
                    {
                        -.5,
                        .5
                    }

                    )
                        foreach (var y in new[]
                        {
                            -.5,
                            .5
                        }

                        )
                        {
                            var corner = local.TransformPoint(new Vec3(x * m.widthMetres, y * m.heightMetres, 0));
                            Require(Math.Abs(corner.x) <= r.widthMetres / 2 + LengthTolerance && Math.Abs(corner.y) <= r.heightMetres / 2 + LengthTolerance, b.id, "printed marker extends beyond mount region");
                        }
            }

            foreach (var t in c.toolSetups)
            {
                Revision(t.revision, t.id);
                Pose(t.controllerFromTool, "Controller", "Tool", t.id);
                Pose(t.toolFromTip, "Tool", "Tip", t.id);
                Text(t.note, t.id);
                if (t.qualified)
                    Text(t.evidence, t.id);
            }
        }

        public static void SafeFile(string path, string context) => Require(path != null && Regex.IsMatch(path, @"\A[A-Za-z0-9_.-]+\z") && !path.Contains(".."), context, "expected local artifact filename");
        public static void SafePath(string path, string context) => Require(path != null && !path.Contains("..") && !path.Contains("\\") && !path.Contains(":") && !path.StartsWith("/") && path.Length > 0, context, "expected repository-relative source path");
        public static void Triangle(SurfaceData t, string path)
        {
            Require(t != null, path, "missing triangle");
            Id(t.id, path);
            Vector(t.a, path);
            Vector(t.b, path);
            Vector(t.c, path);
            Unit(t.normal, path);
            var cross = Vec3.Cross(t.b - t.a, t.c - t.a);
            Require(cross.Length > 1e-12, path, "degenerate finite surface");
            Require(Vec3.Dot(cross * (1 / cross.Length), t.normal) > 1 - 1e-6, path, "normal inverted or inconsistent with outward winding");
        }

        public static bool Contains(SurfaceData s, Vec3 p)
        {
            if (Math.Abs(Vec3.Dot(p - s.a, s.normal)) > LengthTolerance)
                return false;
            var v0 = s.b - s.a;
            var v1 = s.c - s.a;
            var v2 = p - s.a;
            var d00 = Vec3.Dot(v0, v0);
            var d01 = Vec3.Dot(v0, v1);
            var d11 = Vec3.Dot(v1, v1);
            var d20 = Vec3.Dot(v2, v0);
            var d21 = Vec3.Dot(v2, v1);
            var denominator = d00 * d11 - d01 * d01;
            var v = (d11 * d20 - d01 * d21) / denominator;
            var w = (d00 * d21 - d01 * d20) / denominator;
            return v >= -1e-6 && w >= -1e-6 && v + w <= 1 + 1e-6;
        }

        public static void MountRegionCoverage(IEnumerable<SurfaceData> surfaces, MountRegionData region)
        {
            var inverse = new RigidPose(region.fixtureFromRegion).Inverse();
            double area = 0;
            foreach (var surface in surfaces.Where(s => s.sourceFace == region.sourceFace))
            {
                if (Vec3.Dot(surface.normal, region.outwardNormal) < 1 - 1e-6)
                    continue;
                var polygon = new List<Vec3>
                {
                    inverse.TransformPoint(surface.a),
                    inverse.TransformPoint(surface.b),
                    inverse.TransformPoint(surface.c)
                };
                if (polygon.Any(p => Math.Abs(p.z) > LengthTolerance))
                    continue;
                // Clip each convex CAD triangle to the finite rectangle, then sum covered area.
                // Corner/center probes alone would miss an off-center hole or narrow gap.
                foreach (var boundary in new[]
                {
                    0,
                    1,
                    2,
                    3
                }

                )
                {
                    var input = polygon;
                    polygon = new List<Vec3>();
                    if (input.Count == 0)
                        break;
                    Func<Vec3, double> distance = p => boundary == 0 ? p.x + region.widthMetres / 2 : boundary == 1 ? region.widthMetres / 2 - p.x : boundary == 2 ? p.y + region.heightMetres / 2 : region.heightMetres / 2 - p.y;
                    var previous = input[input.Count - 1];
                    double before = distance(previous);
                    foreach (var current in input)
                    {
                        double after = distance(current);
                        if ((after >= 0) != (before >= 0))
                            polygon.Add(previous + (current - previous) * (before / (before - after)));
                        if (after >= 0)
                            polygon.Add(current);
                        previous = current;
                        before = after;
                    }
                }

                double twice = 0;
                for (int i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i];
                    var b = polygon[(i + 1) % polygon.Count];
                    twice += a.x * b.y - a.y * b.x;
                }

                area += Math.Abs(twice) / 2;
            }

            double expected = region.widthMetres * region.heightMetres;
            Require(Math.Abs(area - expected) <= Math.Max(1e-12, expected * 1e-6), region.id, "mount region not supported over its complete area by selected CAD face");
        }

        public static void Geometry(GeometryData g)
        {
            Require(g != null, "geometry", "missing");
            Require(g.schemaVersion == 1, g.id, "unsupported schema");
            Id(g.id, "geometry.id");
            Revision(g.revision, g.id);
            Require(g.units == "m" && (g.frame == "Fixture" || g.frame == "Workpiece"), g.id, "geometry must be in named authored frame metres");
            Hash(g.sourceSha256, g.id);
            var surfaces = Index(g.surfaces, x => x.id, g.id + ".surfaces");
            Require(surfaces.Count > 0, g.id, "no finite surfaces");
            foreach (var s in g.surfaces)
                Triangle(s, s.id);
            Index(g.seams, x => x.id, g.id + ".seams");
            Index(g.cleaningRegions, x => x.id, g.id + ".cleaningRegions");
            if (g.closedSolid)
                ClosedSolid(g);
            Require(g.semanticStatus == "Approved" || g.semanticStatus == "MissingAuthoritativeSelection", g.id, "unknown semantic status");
            if (g.semanticStatus == "Approved")
                Require(g.seams.Length > 0 && g.cleaningRegions.Any(x => x.phase == "PreWeld") && g.cleaningRegions.Any(x => x.phase == "PostWeld"), g.id, "approved geometry requires seams and pre/post masks");
            else
                Text(g.semanticReason, g.id);
            foreach (var s in g.seams)
            {
                Text(s.authoringEvidence, s.id);
                Require(s.points != null && s.points.Length >= 2, s.id, "directed seam needs at least two points");
                Require(s.arcLengthsMetres != null && s.arcLengthsMetres.Length == s.points.Length && s.surfaceIds != null && s.surfaceIds.Length == s.points.Length - 1, s.id, "seam samples/arc lengths/support count mismatch");
                Require(s.arcLengthsMetres[0] == 0, s.id, "arc length must start at zero");
                double arc = 0;
                for (int i = 0; i < s.points.Length; i++)
                {
                    Vector(s.points[i], s.id);
                    Number(s.arcLengthsMetres[i], s.id);
                    if (i == 0)
                        continue;
                    var delta = s.points[i] - s.points[i - 1];
                    Require(delta.Length > LengthTolerance, s.id, "zero length seam segment");
                    arc += delta.Length;
                    Require(Math.Abs(s.arcLengthsMetres[i] - arc) < LengthTolerance, s.id, "arc length inconsistent with directed bake");
                    Require(surfaces.ContainsKey(s.surfaceIds[i - 1] ?? ""), s.id, "unknown supporting surface");
                    var surface = surfaces[s.surfaceIds[i - 1]];
                    Require(Contains(surface, s.points[i - 1]) && Contains(surface, s.points[i]), s.id, "seam segment leaves finite surface");
                    Require(Vec3.Cross(surface.normal, delta * (1 / delta.Length)).Length > 1e-6, s.id, "degenerate seam frame");
                }
            }

            foreach (var mask in g.cleaningRegions)
            {
                Text(mask.authoringEvidence, mask.id);
                Require(mask.phase == "PreWeld" || mask.phase == "PostWeld", mask.id, "invalid cleaning phase");
                Require(mask.triangles != null && mask.triangles.Length > 0 && mask.surfaceIds != null && mask.surfaceIds.Length == mask.triangles.Length, mask.id, "mask support/triangle count mismatch");
                Index(mask.triangles, x => x.id, mask.id);
                for (int i = 0; i < mask.triangles.Length; i++)
                {
                    var t = mask.triangles[i];
                    Triangle(t, mask.id);
                    Require(surfaces.ContainsKey(mask.surfaceIds[i] ?? ""), mask.id, "unknown mask support");
                    var support = surfaces[mask.surfaceIds[i]];
                    Require(Contains(support, t.a) && Contains(support, t.b) && Contains(support, t.c) && Vec3.Dot(t.normal, support.normal) > 1 - 1e-6, mask.id, "cleaning mask leaves finite surface or has inverted normal");
                }
            }
        }

        private static string Key(Vec3 p) => string.Join(",", new[] { p.x, p.y, p.z }.Select(x => Math.Round(x, 8).ToString("F8", CultureInfo.InvariantCulture)));
        private static void ClosedSolid(GeometryData g)
        {
            var edges = new Dictionary<string, (int count, int direction)>();
            double volume6 = 0;
            foreach (var s in g.surfaces)
            {
                volume6 += Vec3.Dot(s.a, Vec3.Cross(s.b, s.c));
                var points = new[]
                {
                    s.a,
                    s.b,
                    s.c
                };
                for (int i = 0; i < 3; i++)
                {
                    var a = Key(points[i]);
                    var b = Key(points[(i + 1) % 3]);
                    int sign = string.CompareOrdinal(a, b) < 0 ? 1 : -1;
                    string key = sign == 1 ? a + "/" + b : b + "/" + a;
                    edges.TryGetValue(key, out var old);
                    edges[key] = (old.count + 1, old.direction + sign);
                }
            }

            Require(edges.Values.All(x => x.count == 2 && x.direction == 0), g.id, "solid has open/nonmanifold or inconsistently oriented edges");
            Require(volume6 > 1e-12, g.id, "solid winding is inward or volume degenerate");
        }

        public static void Visual(VisualData v, GeometryData g)
        {
            Require(v != null && v.schemaVersion == 1 && v.frame == g.frame && v.units == "m", g.id, "visual metadata mismatch");
            Require(v.vertices != null && v.triangles != null && v.triangles.Length == g.surfaces.Length * 3, g.id, "visual triangle count mismatch");
            foreach (var p in v.vertices)
                Vector(p, g.id);
            for (int i = 0; i < v.triangles.Length; i++)
                Require(v.triangles[i] >= 0 && v.triangles[i] < v.vertices.Length, g.id, "visual index out of bounds");
            for (int i = 0; i < g.surfaces.Length; i++)
            {
                var s = g.surfaces[i];
                var expected = new[]
                {
                    s.a,
                    s.b,
                    s.c
                };
                for (int j = 0; j < 3; j++)
                    Require((v.vertices[v.triangles[i * 3 + j]] - expected[j]).Length < LengthTolerance, g.id, "visual/proxy coordinate or winding mismatch");
            }
        }
    }
}
