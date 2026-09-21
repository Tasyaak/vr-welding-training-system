using System;
using System.Collections.Generic;
using System.Linq;

namespace WeldingTrainer.Domain
{
    public sealed class SurfacePatch
    {
        private readonly Vector3d[] _boundary;
        public string Id { get; }
        public IReadOnlyList<Vector3d> BoundaryMetres => Array.AsReadOnly(_boundary);
        public Vector3d OutwardNormal { get; }
        public int ApproachSide { get; }
        public SurfacePatch(string id, IEnumerable<Vector3d> boundary, Vector3d normal, int approachSide)
        { Id = id; _boundary = boundary?.ToArray() ?? Array.Empty<Vector3d>(); OutwardNormal = normal; ApproachSide = approachSide; }
    }

    public sealed class DirectedSeam
    {
        private readonly Vector3d[] _points;
        private readonly double[] _arcLengths;
        public string Id { get; }
        public string SurfaceId { get; }
        public IReadOnlyList<Vector3d> PointsMetres => Array.AsReadOnly(_points);
        public IReadOnlyList<double> ArcLengthsMetres => Array.AsReadOnly(_arcLengths);
        public double BakeErrorMetres { get; }
        public DirectedSeam(string id, string surfaceId, IEnumerable<Vector3d> points,
            IEnumerable<double> arcLengths, double bakeError)
        { Id = id; SurfaceId = surfaceId; _points = points?.ToArray() ?? Array.Empty<Vector3d>();
          _arcLengths = arcLengths?.ToArray() ?? Array.Empty<double>(); BakeErrorMetres = bakeError; }
    }

    public enum RegionPurpose { PreClean, PostClean }

    public sealed class TargetRegion
    {
        private readonly Vector3d[] _boundary;
        public string Id { get; }
        public string SurfaceId { get; }
        public string SourceSeamId { get; }
        public RegionPurpose Purpose { get; }
        public IReadOnlyList<Vector3d> BoundaryMetres => Array.AsReadOnly(_boundary);
        public TargetRegion(string id, string surfaceId, string sourceSeamId, RegionPurpose purpose,
            IEnumerable<Vector3d> boundary)
        { Id = id; SurfaceId = surfaceId; SourceSeamId = sourceSeamId; Purpose = purpose;
          _boundary = boundary?.ToArray() ?? Array.Empty<Vector3d>(); }
    }

    public sealed class WorkpieceDefinition
    {
        private readonly SurfacePatch[] _surfaces;
        private readonly DirectedSeam[] _seams;
        private readonly TargetRegion[] _regions;
        public string Id { get; }
        public int Version { get; }
        public IReadOnlyList<SurfacePatch> Surfaces => Array.AsReadOnly(_surfaces);
        public IReadOnlyList<DirectedSeam> Seams => Array.AsReadOnly(_seams);
        public IReadOnlyList<TargetRegion> Regions => Array.AsReadOnly(_regions);

        public WorkpieceDefinition(string id, int version, IEnumerable<SurfacePatch> surfaces,
            IEnumerable<DirectedSeam> seams, IEnumerable<TargetRegion> regions)
        { Id = id; Version = version; _surfaces = surfaces?.ToArray() ?? Array.Empty<SurfacePatch>();
          _seams = seams?.ToArray() ?? Array.Empty<DirectedSeam>();
          _regions = regions?.ToArray() ?? Array.Empty<TargetRegion>(); }

        public ValidationResult Validate(double tightestScoringToleranceMetres)
        {
            var result = new ValidationResult();
            ValidationRules.Id(Id, "id", result);
            if (Version < 1) result.Add("version", "must be at least 1");
            if (_surfaces.Length == 0) result.Add("surfaces", "must contain at least one finite surface");
            if (_seams.Length < 2) result.Add("seams", "MVP sample content must contain at least two directed seams");
            ValidationRules.UniqueIds(_surfaces, x => x.Id, "surfaces", result);
            ValidationRules.UniqueIds(_seams, x => x.Id, "seams", result);
            ValidationRules.UniqueIds(_regions, x => x.Id, "regions", result);
            var surfaceIds = new HashSet<string>(_surfaces.Select(x => x.Id), StringComparer.Ordinal);
            var seamIds = new HashSet<string>(_seams.Select(x => x.Id), StringComparer.Ordinal);

            for (int i = 0; i < _surfaces.Length; i++)
            {
                var s = _surfaces[i];
                ValidationRules.Id(s.Id, $"surfaces[{i}].id", result);
                if (s.BoundaryMetres.Count < 3) result.Add($"surfaces[{i}].boundaryMetres", "must contain at least three finite vertices");
                for (int p = 0; p < s.BoundaryMetres.Count; p++)
                    if (!s.BoundaryMetres[p].IsFinite) result.Add($"surfaces[{i}].boundaryMetres[{p}]", "must be finite");
                ValidationRules.Unit(s.OutwardNormal, $"surfaces[{i}].outwardNormal", result);
                if (s.ApproachSide != -1 && s.ApproachSide != 1)
                    result.Add($"surfaces[{i}].approachSide", "must be -1 or +1");
                if (s.BoundaryMetres.Count >= 3 && s.BoundaryMetres.All(x => x.IsFinite) && s.OutwardNormal.IsFinite)
                {
                    Vector3d origin = s.BoundaryMetres[0];
                    for (int p = 1; p < s.BoundaryMetres.Count; p++)
                        if (Math.Abs(Vector3d.Dot(s.BoundaryMetres[p] - origin, s.OutwardNormal)) > 1e-5)
                            result.Add($"surfaces[{i}].boundaryMetres[{p}]", "must lie in the authored surface plane");
                    double twiceArea = 0;
                    for (int p = 0; p < s.BoundaryMetres.Count; p++)
                        twiceArea += Vector3d.Dot(Vector3d.Cross(s.BoundaryMetres[p],
                            s.BoundaryMetres[(p + 1) % s.BoundaryMetres.Count]), s.OutwardNormal);
                    if (Math.Abs(twiceArea) <= 1e-10)
                        result.Add($"surfaces[{i}].boundaryMetres", "must define a non-degenerate finite polygon");
                }
            }

            for (int i = 0; i < _seams.Length; i++)
            {
                var seam = _seams[i];
                ValidationRules.Id(seam.Id, $"seams[{i}].id", result);
                if (!surfaceIds.Contains(seam.SurfaceId)) result.Add($"seams[{i}].surfaceId", "references an unknown finite surface");
                if (seam.PointsMetres.Count < 2) result.Add($"seams[{i}].pointsMetres", "must contain at least two baked points");
                if (seam.ArcLengthsMetres.Count != seam.PointsMetres.Count)
                    result.Add($"seams[{i}].arcLengthsMetres", "must match baked point count");
                for (int p = 0; p < seam.PointsMetres.Count; p++)
                {
                    if (!seam.PointsMetres[p].IsFinite) result.Add($"seams[{i}].pointsMetres[{p}]", "must be finite");
                    if (p > 0 && Vector3d.Distance(seam.PointsMetres[p - 1], seam.PointsMetres[p]) <= 1e-9)
                        result.Add($"seams[{i}].pointsMetres[{p}]", "creates a zero-length tangent");
                    if (p > 0 && p < seam.ArcLengthsMetres.Count && seam.ArcLengthsMetres[p] <= seam.ArcLengthsMetres[p - 1])
                        result.Add($"seams[{i}].arcLengthsMetres[{p}]", "must be strictly increasing");
                }
                ValidationRules.Positive(seam.BakeErrorMetres, $"seams[{i}].bakeErrorMetres", result);
                if (ValidationRules.IsFinite(seam.BakeErrorMetres) && seam.BakeErrorMetres >= tightestScoringToleranceMetres)
                    result.Add($"seams[{i}].bakeErrorMetres", "must be below the tightest scoring tolerance");
                SurfacePatch owningSurface = _surfaces.FirstOrDefault(x => x.Id == seam.SurfaceId);
                if (owningSurface != null)
                    for (int p = 0; p < seam.PointsMetres.Count; p++)
                        if (!PointInsideConvexPatch(seam.PointsMetres[p], owningSurface, 1e-5))
                            result.Add($"seams[{i}].pointsMetres[{p}]", "lies outside the referenced finite surface bounds");
            }

            for (int i = 0; i < _regions.Length; i++)
            {
                var region = _regions[i];
                ValidationRules.Id(region.Id, $"regions[{i}].id", result);
                if (!surfaceIds.Contains(region.SurfaceId)) result.Add($"regions[{i}].surfaceId", "references an unknown finite surface");
                if (region.BoundaryMetres.Count < 3) result.Add($"regions[{i}].boundaryMetres", "must contain at least three vertices");
                for (int p = 0; p < region.BoundaryMetres.Count; p++)
                    if (!region.BoundaryMetres[p].IsFinite) result.Add($"regions[{i}].boundaryMetres[{p}]", "must be finite");
                if (region.Purpose == RegionPurpose.PostClean && !seamIds.Contains(region.SourceSeamId))
                    result.Add($"regions[{i}].sourceSeamId", "post-clean region must reference an existing source seam");
            }
            return result;
        }

        private static bool PointInsideConvexPatch(Vector3d point, SurfacePatch patch, double tolerance)
        {
            if (patch.BoundaryMetres.Count < 3) return false;
            Vector3d origin = patch.BoundaryMetres[0];
            if (Math.Abs(Vector3d.Dot(point - origin, patch.OutwardNormal)) > tolerance) return false;
            double sign = 0;
            for (int i = 0; i < patch.BoundaryMetres.Count; i++)
            {
                Vector3d a = patch.BoundaryMetres[i];
                Vector3d b = patch.BoundaryMetres[(i + 1) % patch.BoundaryMetres.Count];
                double edgeSign = Vector3d.Dot(Vector3d.Cross(b - a, point - a), patch.OutwardNormal);
                if (Math.Abs(edgeSign) <= tolerance) continue;
                if (sign == 0) sign = Math.Sign(edgeSign);
                else if (Math.Sign(edgeSign) != sign) return false;
            }
            return true;
        }
    }
}
