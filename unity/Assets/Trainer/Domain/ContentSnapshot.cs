using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WeldingTrainer.Domain
{
    public sealed class ContentCatalogEntry
    {
        public string Id { get; }
        public FixtureDefinition Fixture { get; }
        public WorkpieceDefinition Workpiece { get; }
        public ToolDefinition Tool { get; }
        private readonly ProcessProfile[] _profiles;
        public IReadOnlyList<ProcessProfile> Profiles => Array.AsReadOnly(_profiles);
        public ContentCatalogEntry(string id, FixtureDefinition fixture, WorkpieceDefinition workpiece,
            ToolDefinition tool, IEnumerable<ProcessProfile> profiles)
        { Id = id; Fixture = fixture; Workpiece = workpiece; Tool = tool;
          _profiles = profiles?.ToArray() ?? Array.Empty<ProcessProfile>(); }

        public ValidationResult Validate()
        {
            var result = new ValidationResult();
            ValidationRules.Id(Id, "id", result);
            if (Fixture == null) result.Add("fixture", "is required"); else result.Merge("fixture", Fixture.Validate());
            if (Tool == null) result.Add("tool", "is required"); else result.Merge("tool", Tool.Validate());
            if (_profiles.Length != 5 || Enum.GetValues(typeof(ProcessMode)).Cast<ProcessMode>()
                .Any(mode => _profiles.Count(p => p?.Settings?.Mode == mode) != 1))
                result.Add("profiles", "must contain exactly one profile for each of the five process modes");
            ValidationRules.UniqueIds(_profiles.Where(x => x != null), x => x.Id, "profiles", result);
            for (int i = 0; i < _profiles.Length; i++)
                if (_profiles[i] == null) result.Add($"profiles[{i}]", "is required");
                else result.Merge($"profiles[{i}]", _profiles[i].Validate());
            double tightest = _profiles.Where(x => x != null)
                .SelectMany(x => new[] { x.FootprintWidthMetres, x.ContactEnterMetres })
                .Where(x => ValidationRules.IsFinite(x) && x > 0).DefaultIfEmpty(0.001).Min();
            if (Workpiece == null) result.Add("workpiece", "is required");
            else result.Merge("workpiece", Workpiece.Validate(tightest));
            return result;
        }
    }

    /// Deep immutable definitions plus deterministic hashes frozen at attempt start.
    public sealed class ContentSnapshot
    {
        public ContentCatalogEntry Entry { get; }
        public string ContentHashSha256 { get; }
        public string EvaluationHashSha256 { get; }
        private ContentSnapshot(ContentCatalogEntry entry, string contentHash, string evaluationHash)
        { Entry = entry; ContentHashSha256 = contentHash; EvaluationHashSha256 = evaluationHash; }

        public static ContentSnapshot Freeze(ContentCatalogEntry source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.Validate().ThrowIfInvalid();
            ContentCatalogEntry copy = DeepCopy(source);
            string evaluation = Canonical(copy, includeAssistance: false);
            string complete = Canonical(copy, includeAssistance: true);
            return new ContentSnapshot(copy, Sha256(complete), Sha256(evaluation));
        }

        private static ContentCatalogEntry DeepCopy(ContentCatalogEntry e) => new(e.Id,
            new FixtureDefinition(e.Fixture.Id, e.Fixture.Version,
                e.Fixture.ReferencePoints.Select(p => new ReferencePointDefinition(p.Id, p.FixturePositionMetres)),
                e.Fixture.NominalDimensionsMetres, e.Fixture.WorkpieceFromFixture,
                new CalibrationPolicy(e.Fixture.Calibration.SamplesPerPoint,
                    e.Fixture.Calibration.StabilityRadiusMetres, e.Fixture.Calibration.MaxResidualMetres,
                    e.Fixture.Calibration.MinPointSeparationMetres, e.Fixture.Calibration.MinTriangleAreaSquareMetres)),
            new WorkpieceDefinition(e.Workpiece.Id, e.Workpiece.Version,
                e.Workpiece.Surfaces.Select(s => new SurfacePatch(s.Id, s.BoundaryMetres, s.OutwardNormal, s.ApproachSide)),
                e.Workpiece.Seams.Select(s => new DirectedSeam(s.Id, s.SurfaceId, s.PointsMetres, s.ArcLengthsMetres, s.BakeErrorMetres)),
                e.Workpiece.Regions.Select(r => new TargetRegion(r.Id, r.SurfaceId, r.SourceSeamId, r.Purpose, r.BoundaryMetres))),
            new ToolDefinition(e.Tool.Id, e.Tool.Version, e.Tool.GripPose, e.Tool.ToolFromController,
                e.Tool.EffectiveTipFromTool, e.Tool.BackwardToolAxis, e.Tool.IncidentBeamDirectionFromHead),
            e.Profiles.Select(CopyProfile));

        private static ProcessProfile CopyProfile(ProcessProfile p) => new(p.Id, p.SchemaVersion, CopySettings(p.Settings), p.RequiredNozzleId,
            p.FootprintWidthMetres, p.FootprintLengthMetres,
            new RangeSetting(p.SpeedMetresPerSecond.Minimum, p.SpeedMetresPerSecond.Maximum),
            new RangeSetting(p.StandoffMetres.Minimum, p.StandoffMetres.Maximum),
            new RangeSetting(p.WorkAngleRadians.Minimum, p.WorkAngleRadians.Maximum),
            new RangeSetting(p.TravelAngleRadians.Minimum, p.TravelAngleRadians.Maximum),
            p.ContactEnterMetres, p.ContactExitMetres, p.MaximumSampleGapSeconds,
            p.ReflectionConeRadians, p.ReflectionTargetMarginMetres, p.AssistancePercent);

        private static ProcessSettings CopySettings(ProcessSettings s) => s switch
        {
            FusionSettings => new FusionSettings(),
            WobbleSettings w => new WobbleSettings(w.WidthMetres, w.FrequencyHertz),
            PulsedSettings p => new PulsedSettings(p.OnSeconds, p.OffSeconds),
            CleaningSettings c => new CleaningSettings(c.Mode),
            _ => throw new NotSupportedException($"Unsupported settings {s?.GetType().Name}")
        };

        private static string Canonical(ContentCatalogEntry e, bool includeAssistance)
        {
            var b = new StringBuilder();
            void S(string v) => b.Append(v?.Length ?? -1).Append(':').Append(v).Append('|');
            void N(double v) => b.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            void V(Vector3d v) { N(v.X); N(v.Y); N(v.Z); }
            void Q(Quaterniond q) { N(q.X); N(q.Y); N(q.Z); N(q.W); }
            void P(RigidPose p) { V(p.PositionMetres); Q(p.Rotation); }
            S(e.Id); S(e.Fixture.Id); N(e.Fixture.Version); V(e.Fixture.NominalDimensionsMetres);
            foreach (var p in e.Fixture.ReferencePoints) { S(p.Id); V(p.FixturePositionMetres); }
            P(e.Fixture.WorkpieceFromFixture);
            N(e.Fixture.Calibration.SamplesPerPoint); N(e.Fixture.Calibration.StabilityRadiusMetres);
            N(e.Fixture.Calibration.MaxResidualMetres); N(e.Fixture.Calibration.MinPointSeparationMetres);
            N(e.Fixture.Calibration.MinTriangleAreaSquareMetres);
            S(e.Workpiece.Id); N(e.Workpiece.Version);
            foreach (var s in e.Workpiece.Surfaces) { S(s.Id); V(s.OutwardNormal); N(s.ApproachSide); foreach (var v in s.BoundaryMetres) V(v); }
            foreach (var s in e.Workpiece.Seams) { S(s.Id); S(s.SurfaceId); N(s.BakeErrorMetres); foreach (var v in s.PointsMetres) V(v); foreach (var a in s.ArcLengthsMetres) N(a); }
            foreach (var r in e.Workpiece.Regions) { S(r.Id); S(r.SurfaceId); S(r.SourceSeamId); N((int)r.Purpose); foreach (var v in r.BoundaryMetres) V(v); }
            S(e.Tool.Id); N(e.Tool.Version); N((int)e.Tool.GripPose); P(e.Tool.ToolFromController);
            P(e.Tool.EffectiveTipFromTool); V(e.Tool.BackwardToolAxis); V(e.Tool.IncidentBeamDirectionFromHead);
            foreach (var p in e.Profiles.OrderBy(x => x.Settings.Mode))
            {
                S(p.Id); N(p.SchemaVersion); N((int)p.Settings.Mode); S(p.RequiredNozzleId); S(p.MetricSchemaId);
                N(p.FootprintWidthMetres); N(p.FootprintLengthMetres);
                N(p.SpeedMetresPerSecond.Minimum); N(p.SpeedMetresPerSecond.Maximum);
                N(p.StandoffMetres.Minimum); N(p.StandoffMetres.Maximum);
                N(p.WorkAngleRadians.Minimum); N(p.WorkAngleRadians.Maximum);
                N(p.TravelAngleRadians.Minimum); N(p.TravelAngleRadians.Maximum);
                N(p.ContactEnterMetres); N(p.ContactExitMetres); N(p.MaximumSampleGapSeconds);
                N(p.ReflectionConeRadians); N(p.ReflectionTargetMarginMetres);
                if (p.Settings is WobbleSettings w) { N(w.WidthMetres); N(w.FrequencyHertz); }
                if (p.Settings is PulsedSettings pulse) { N(pulse.OnSeconds); N(pulse.OffSeconds); }
                if (includeAssistance) N(p.AssistancePercent);
            }
            return b.ToString();
        }

        private static string Sha256(string value)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var b = new StringBuilder(hash.Length * 2);
            foreach (byte x in hash) b.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return b.ToString();
        }
    }
}
