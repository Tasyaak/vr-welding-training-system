using System;
using System.Collections.Generic;
using System.Linq;

namespace WeldingTrainer.Domain
{
    /// <summary>
    /// Directed, piecewise-linear seam in Workpiece-local metres.
    /// Arc length increases from Points[0] toward the last point.
    /// </summary>
    public sealed class DirectedSpline
    {
        private const double DegenerateSegmentToleranceMetres = 1e-9;
        private const double DistanceTieToleranceMetres = 1e-9;
        private const double ArcTieToleranceMetres = 1e-9;

        private readonly Vec3[] _points;
        private readonly double[] _arcLength;
        private readonly IReadOnlyList<Vec3> _readOnlyPoints;

        public string Id { get; }
        public IReadOnlyList<Vec3> Points => _readOnlyPoints;
        public double LengthMetres => _arcLength[_arcLength.Length - 1];

        public DirectedSpline(string id, IEnumerable<Vec3> points)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Spline ID is required.", nameof(id));

            _points = points?.ToArray() ?? Array.Empty<Vec3>();
            if (_points.Length < 2)
                throw new ArgumentException("Spline requires at least two points.", nameof(points));
            if (_points.Any(point => !point.Finite))
                throw new ArgumentException("Spline points must be finite.", nameof(points));

            Id = id;
            _arcLength = new double[_points.Length];

            for (int i = 1; i < _points.Length; i++)
            {
                double segmentLength = (_points[i] - _points[i - 1]).Length;
                if (!double.IsFinite(segmentLength) ||
                    segmentLength <= DegenerateSegmentToleranceMetres)
                {
                    throw new ArgumentException(
                        "Spline contains a degenerate or non-finite segment.",
                        nameof(points));
                }

                _arcLength[i] = _arcLength[i - 1] + segmentLength;
            }

            _readOnlyPoints = Array.AsReadOnly(_points);
        }

        /// <summary>
        /// Projects a Workpiece-local point onto the directed spline.
        /// If priorArcLengthMetres is supplied, only candidates within searchRadiusMetres
        /// of that prior arc position are eligible. Equal-distance candidates at the same
        /// geometric polyline vertex are treated as one location; equal-distance candidates
        /// at distinct arc positions are reported as ambiguous unless prior arc resolves them.
        /// </summary>
        public SeamProjection Project(
            Vec3 pointWorkpieceMetres,
            double? priorArcLengthMetres = null,
            double searchRadiusMetres = 0.25)
        {
            if (!pointWorkpieceMetres.Finite)
                return default;
            if (!double.IsFinite(searchRadiusMetres) || searchRadiusMetres <= 0d)
                return default;
            if (priorArcLengthMetres.HasValue &&
                !double.IsFinite(priorArcLengthMetres.Value))
            {
                return default;
            }

            SeamProjection best = default;
            double bestDistance = double.PositiveInfinity;
            bool ambiguous = false;

            for (int i = 0; i < _points.Length - 1; i++)
            {
                Vec3 start = _points[i];
                Vec3 segment = _points[i + 1] - start;
                double segmentLengthSquared = segment.LengthSquared;
                double segmentLength = Math.Sqrt(segmentLengthSquared);

                double t = Clamp01(
                    Vec3.Dot(pointWorkpieceMetres - start, segment) /
                    segmentLengthSquared);

                double candidateArc = _arcLength[i] + t * segmentLength;
                if (priorArcLengthMetres.HasValue &&
                    Math.Abs(candidateArc - priorArcLengthMetres.Value) >
                    searchRadiusMetres)
                {
                    continue;
                }

                Vec3 candidatePosition = start + segment * t;
                double candidateDistance =
                    (pointWorkpieceMetres - candidatePosition).Length;

                if (candidateDistance < bestDistance - DistanceTieToleranceMetres)
                {
                    bestDistance = candidateDistance;
                    ambiguous = false;
                    best = new SeamProjection(
                        true,
                        false,
                        i,
                        candidateArc,
                        candidateArc / LengthMetres,
                        candidatePosition,
                        segment / segmentLength);
                    continue;
                }

                if (Math.Abs(candidateDistance - bestDistance) >
                    DistanceTieToleranceMetres)
                {
                    continue;
                }

                // Adjacent segments meeting at a common vertex may produce the same
                // closest point and the same arc length. That is not a self-intersection.
                if (Math.Abs(candidateArc - best.ArcLengthMetres) <=
                    ArcTieToleranceMetres)
                {
                    continue;
                }

                if (priorArcLengthMetres.HasValue)
                {
                    double currentPriorDistance =
                        Math.Abs(candidateArc - priorArcLengthMetres.Value);
                    double bestPriorDistance =
                        Math.Abs(best.ArcLengthMetres - priorArcLengthMetres.Value);

                    if (currentPriorDistance <
                        bestPriorDistance - ArcTieToleranceMetres)
                    {
                        ambiguous = false;
                        best = new SeamProjection(
                            true,
                            false,
                            i,
                            candidateArc,
                            candidateArc / LengthMetres,
                            candidatePosition,
                            segment / segmentLength);
                    }
                    else if (Math.Abs(currentPriorDistance - bestPriorDistance) <=
                             ArcTieToleranceMetres)
                    {
                        ambiguous = true;
                    }
                }
                else
                {
                    ambiguous = true;
                }
            }

            if (double.IsPositiveInfinity(bestDistance))
                return default;

            return new SeamProjection(
                best.Valid,
                ambiguous,
                best.SegmentIndex,
                best.ArcLengthMetres,
                best.Progress,
                best.PositionWorkpieceMetres,
                best.TangentWorkpiece);
        }

        private static double Clamp01(double value) =>
            Math.Max(0d, Math.Min(1d, value));
    }

    public readonly struct SeamProjection
    {
        public readonly bool Valid;
        public readonly bool Ambiguous;
        public readonly int SegmentIndex;
        public readonly double ArcLengthMetres;
        public readonly double Progress;
        public readonly Vec3 PositionWorkpieceMetres;
        public readonly Vec3 TangentWorkpiece;

        public SeamProjection(
            bool valid,
            bool ambiguous,
            int segmentIndex,
            double arcLengthMetres,
            double progress,
            Vec3 positionWorkpieceMetres,
            Vec3 tangentWorkpiece)
        {
            Valid = valid;
            Ambiguous = ambiguous;
            SegmentIndex = segmentIndex;
            ArcLengthMetres = arcLengthMetres;
            Progress = progress;
            PositionWorkpieceMetres = positionWorkpieceMetres;
            TangentWorkpiece = tangentWorkpiece;
        }
    }

    public enum SpeedClass
    {
        Invalid,
        TooSlow,
        SpeedCorrect,
        TooFast
    }

    [Flags]
    public enum MotionFlags
    {
        None = 0,
        Forward = 1 << 0,
        Reverse = 1 << 1,
        Discontinuity = 1 << 2,
        SkippedRegion = 1 << 3,
        TrackingGap = 1 << 4,
        NonMonotonicTime = 1 << 5,
        AmbiguousProjection = 1 << 6,
        GenerationMismatch = 1 << 7,
        InputUnavailable = 1 << 8,
        InvalidInput = 1 << 9,
        ContextMismatch = 1 << 10,
        InvalidSurfaceFrame = 1 << 11,
        ProjectionUnavailable = 1 << 12
    }

    /// <summary>
    /// Frozen identity/generation context for one evaluation stream.
    /// </summary>
    public readonly struct PathEvaluationContext
    {
        public readonly string SessionId;
        public readonly string AttemptId;
        public readonly string ContentHash;
        public readonly long RegistrationGeneration;
        public readonly long OriginGeneration;

        public bool Valid =>
            !string.IsNullOrWhiteSpace(SessionId) &&
            !string.IsNullOrWhiteSpace(AttemptId) &&
            !string.IsNullOrWhiteSpace(ContentHash) &&
            RegistrationGeneration >= 0 &&
            OriginGeneration >= 0;

        public PathEvaluationContext(
            string sessionId,
            string attemptId,
            string contentHash,
            long registrationGeneration,
            long originGeneration)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(attemptId))
                throw new ArgumentException("Attempt ID is required.", nameof(attemptId));
            if (string.IsNullOrWhiteSpace(contentHash))
                throw new ArgumentException("Content hash is required.", nameof(contentHash));
            if (registrationGeneration < 0)
                throw new ArgumentOutOfRangeException(nameof(registrationGeneration));
            if (originGeneration < 0)
                throw new ArgumentOutOfRangeException(nameof(originGeneration));

            SessionId = sessionId;
            AttemptId = attemptId;
            ContentHash = contentHash;
            RegistrationGeneration = registrationGeneration;
            OriginGeneration = originGeneration;
        }
    }

    /// <summary>
    /// Immutable schema-v1 evaluator input. Tip, tool-forward and surface-normal
    /// values are already expressed in Workpiece-local coordinates. Mapping from
    /// Unity/world/QR data into this contract belongs to the #58 integration adapter.
    /// </summary>
    public readonly struct PathSample
    {
        public const int SchemaVersion = 1;

        public readonly string SessionId;
        public readonly string AttemptId;
        public readonly string ContentHash;
        public readonly double MonotonicSeconds;
        public readonly Vec3 TipWorkpieceMetres;
        public readonly Vec3 ToolForwardWorkpiece;
        public readonly Vec3 SurfaceNormalWorkpiece;
        public readonly bool Available;
        public readonly bool TrackingValid;
        public readonly long RegistrationGeneration;
        public readonly long OriginGeneration;

        public PathSample(
            string sessionId,
            string attemptId,
            string contentHash,
            double monotonicSeconds,
            Vec3 tipWorkpieceMetres,
            Vec3 toolForwardWorkpiece,
            Vec3 surfaceNormalWorkpiece,
            bool available,
            bool trackingValid,
            long registrationGeneration,
            long originGeneration)
        {
            SessionId = sessionId;
            AttemptId = attemptId;
            ContentHash = contentHash;
            MonotonicSeconds = monotonicSeconds;
            TipWorkpieceMetres = tipWorkpieceMetres;
            ToolForwardWorkpiece = toolForwardWorkpiece;
            SurfaceNormalWorkpiece = surfaceNormalWorkpiece;
            Available = available;
            TrackingValid = trackingValid;
            RegistrationGeneration = registrationGeneration;
            OriginGeneration = originGeneration;
        }
    }

    public sealed class PathMetrics
    {
        public const int SchemaVersion = 1;

        public string SessionId { get; }
        public string AttemptId { get; }
        public string ContentHash { get; }
        public string SeamId { get; }

        /// <summary>
        /// True when the current sample can be used as a geometrically continuous
        /// path metric sample. Speed and travel-angle validity remain separate.
        /// </summary>
        public bool Valid { get; }

        public SeamProjection Projection { get; }
        public Vec3 ErrorVectorWorkpieceMetres { get; }
        public double TangentialErrorMetres { get; }
        public double LateralErrorMetres { get; }
        public double NormalErrorMetres { get; }
        public double TotalErrorMetres { get; }

        public bool SpeedValid { get; }
        public double SignedSpeedMps { get; }
        public double FilteredSpeedMps { get; }
        public SpeedClass SpeedClass { get; }

        public bool TravelAngleValid { get; }
        public double TravelAngleRadians { get; }
        public bool WorkAngleValid { get; }
        public double WorkAngleRadians { get; }
        public MotionFlags Flags { get; }

        public PathMetrics(
            string sessionId,
            string attemptId,
            string contentHash,
            string seamId,
            bool valid,
            SeamProjection projection,
            Vec3 errorVectorWorkpieceMetres,
            double tangentialErrorMetres,
            double lateralErrorMetres,
            double normalErrorMetres,
            bool speedValid,
            double signedSpeedMps,
            double filteredSpeedMps,
            SpeedClass speedClass,
            bool travelAngleValid,
            double travelAngleRadians,
            bool workAngleValid,
            double workAngleRadians,
            MotionFlags flags)
        {
            SessionId = sessionId;
            AttemptId = attemptId;
            ContentHash = contentHash;
            SeamId = seamId;
            Valid = valid;
            Projection = projection;
            ErrorVectorWorkpieceMetres = errorVectorWorkpieceMetres;
            TangentialErrorMetres = tangentialErrorMetres;
            LateralErrorMetres = lateralErrorMetres;
            NormalErrorMetres = normalErrorMetres;
            TotalErrorMetres = errorVectorWorkpieceMetres.Length;
            SpeedValid = speedValid;
            SignedSpeedMps = signedSpeedMps;
            FilteredSpeedMps = filteredSpeedMps;
            SpeedClass = speedClass;
            TravelAngleValid = travelAngleValid;
            TravelAngleRadians = travelAngleRadians;
            WorkAngleValid = workAngleValid;
            WorkAngleRadians = workAngleRadians;
            Flags = flags;
        }
    }

    public sealed class PathEvaluationPolicy
    {
        public double MinimumSpeedMps { get; }
        public double MaximumSpeedMps { get; }
        public double FilterTimeConstantSeconds { get; }
        public double MaximumSampleGapSeconds { get; }
        public double MaximumArcJumpMetres { get; }
        public double ReverseToleranceMetres { get; }
        public double MinimumMotionMetres { get; }
        public double ProjectionSearchRadiusMetres { get; }

        public PathEvaluationPolicy(
            double minimumSpeedMps,
            double maximumSpeedMps,
            double filterTimeConstantSeconds,
            double maximumSampleGapSeconds,
            double maximumArcJumpMetres,
            double reverseToleranceMetres,
            double minimumMotionMetres,
            double projectionSearchRadiusMetres)
        {
            if (!double.IsFinite(minimumSpeedMps) || minimumSpeedMps < 0d)
                throw new ArgumentOutOfRangeException(nameof(minimumSpeedMps));
            if (!double.IsFinite(maximumSpeedMps) ||
                maximumSpeedMps <= minimumSpeedMps)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSpeedMps));
            }
            if (!double.IsFinite(filterTimeConstantSeconds) ||
                filterTimeConstantSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(filterTimeConstantSeconds));
            }
            if (!double.IsFinite(maximumSampleGapSeconds) ||
                maximumSampleGapSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSampleGapSeconds));
            }
            if (!double.IsFinite(maximumArcJumpMetres) ||
                maximumArcJumpMetres <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumArcJumpMetres));
            }
            if (!double.IsFinite(reverseToleranceMetres) ||
                reverseToleranceMetres < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(reverseToleranceMetres));
            }
            if (!double.IsFinite(minimumMotionMetres) || minimumMotionMetres <= 0d)
                throw new ArgumentOutOfRangeException(nameof(minimumMotionMetres));
            if (!double.IsFinite(projectionSearchRadiusMetres) ||
                projectionSearchRadiusMetres < maximumArcJumpMetres)
            {
                throw new ArgumentOutOfRangeException(nameof(projectionSearchRadiusMetres));
            }

            MinimumSpeedMps = minimumSpeedMps;
            MaximumSpeedMps = maximumSpeedMps;
            FilterTimeConstantSeconds = filterTimeConstantSeconds;
            MaximumSampleGapSeconds = maximumSampleGapSeconds;
            MaximumArcJumpMetres = maximumArcJumpMetres;
            ReverseToleranceMetres = reverseToleranceMetres;
            MinimumMotionMetres = minimumMotionMetres;
            ProjectionSearchRadiusMetres = projectionSearchRadiusMetres;
        }
    }

    /// <summary>
    /// Pure deterministic weld-path evaluator. It reports metrics only; it does not
    /// grant process permission, write coverage, drive rendering or control hardware.
    /// </summary>
    public sealed class WeldPathEvaluator
    {
        private const double UnitVectorTolerance = 1e-9;

        private readonly DirectedSpline _spline;
        private readonly PathEvaluationPolicy _policy;
        private readonly PathEvaluationContext _context;

        private bool _hasPrevious;
        private PathSample _previousSample;
        private SeamProjection _previousProjection;
        private bool _hasFilteredSpeed;
        private double _filteredSpeedMps;

        public WeldPathEvaluator(
            DirectedSpline spline,
            PathEvaluationPolicy policy,
            PathEvaluationContext context)
        {
            _spline = spline ?? throw new ArgumentNullException(nameof(spline));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (!context.Valid)
                throw new ArgumentException("Evaluation context is invalid.", nameof(context));
            _context = context;
        }

        public void Reset()
        {
            _hasPrevious = false;
            _previousSample = default;
            _previousProjection = default;
            _hasFilteredSpeed = false;
            _filteredSpeedMps = 0d;
        }

        public PathMetrics Evaluate(PathSample sample)
        {
            MotionFlags flags = MotionFlags.None;

            if (!sample.Available)
            {
                Reset();
                return Invalid(sample, MotionFlags.InputUnavailable);
            }

            if (!sample.TrackingValid)
            {
                Reset();
                return Invalid(sample, MotionFlags.TrackingGap);
            }

            if (!MatchesContext(sample))
            {
                Reset();
                return Invalid(sample, MotionFlags.ContextMismatch);
            }

            if (sample.RegistrationGeneration != _context.RegistrationGeneration ||
                sample.OriginGeneration != _context.OriginGeneration)
            {
                Reset();
                return Invalid(sample, MotionFlags.GenerationMismatch);
            }

            if (!double.IsFinite(sample.MonotonicSeconds) ||
                !sample.TipWorkpieceMetres.Finite ||
                !sample.ToolForwardWorkpiece.Finite ||
                !sample.SurfaceNormalWorkpiece.Finite)
            {
                Reset();
                return Invalid(sample, MotionFlags.InvalidInput);
            }

            double? priorArcLength =
                _hasPrevious ? _previousProjection.ArcLengthMetres : null;

            SeamProjection projection = _spline.Project(
                sample.TipWorkpieceMetres,
                priorArcLength,
                _policy.ProjectionSearchRadiusMetres);

            if (!projection.Valid)
            {
                MotionFlags projectionFlags = MotionFlags.ProjectionUnavailable;
                if (_hasPrevious)
                {
                    projectionFlags |=
                        MotionFlags.Discontinuity | MotionFlags.SkippedRegion;
                }

                Reset();
                return Invalid(sample, projectionFlags);
            }

            if (projection.Ambiguous)
            {
                Reset();
                return Invalid(
                    sample,
                    MotionFlags.AmbiguousProjection,
                    projection);
            }

            Vec3 normal = sample.SurfaceNormalWorkpiece.Normalized;
            if (!IsUnitLike(normal))
            {
                Reset();
                return Invalid(
                    sample,
                    MotionFlags.InvalidSurfaceFrame,
                    projection);
            }

            // +lateral follows tangent x outward-normal. Together with tangent and
            // normal this gives a documented, deterministic seam-local sign convention.
            Vec3 lateralAxis =
                Vec3.Cross(projection.TangentWorkpiece, normal).Normalized;
            if (!IsUnitLike(lateralAxis))
            {
                Reset();
                return Invalid(
                    sample,
                    MotionFlags.InvalidSurfaceFrame,
                    projection);
            }

            Vec3 error = sample.TipWorkpieceMetres - projection.PositionWorkpieceMetres;
            double tangentialError = Vec3.Dot(error, projection.TangentWorkpiece);
            double lateralError = Vec3.Dot(error, lateralAxis);
            double normalError = Vec3.Dot(error, normal);

            Vec3 toolForward = sample.ToolForwardWorkpiece.Normalized;
            bool workAngleValid = IsUnitLike(toolForward);
            double workAngle = workAngleValid
                ? Math.Acos(ClampUnit(Math.Abs(Vec3.Dot(toolForward, normal))))
                : 0d;

            if (!_hasPrevious)
            {
                SetBaseline(sample, projection, resetFilter: true);
                return Metrics(
                    sample,
                    valid: true,
                    projection,
                    error,
                    tangentialError,
                    lateralError,
                    normalError,
                    speedValid: false,
                    signedSpeedMps: 0d,
                    filteredSpeedMps: 0d,
                    speedClass: SpeedClass.Invalid,
                    travelAngleValid: false,
                    travelAngleRadians: 0d,
                    workAngleValid,
                    workAngle,
                    flags);
            }

            double deltaTime =
                sample.MonotonicSeconds - _previousSample.MonotonicSeconds;

            if (deltaTime <= 0d)
            {
                flags |= MotionFlags.NonMonotonicTime;
                SetBaseline(sample, projection, resetFilter: true);
                return Metrics(
                    sample,
                    valid: false,
                    projection,
                    error,
                    tangentialError,
                    lateralError,
                    normalError,
                    speedValid: false,
                    signedSpeedMps: 0d,
                    filteredSpeedMps: 0d,
                    speedClass: SpeedClass.Invalid,
                    travelAngleValid: false,
                    travelAngleRadians: 0d,
                    workAngleValid,
                    workAngle,
                    flags);
            }

            if (deltaTime > _policy.MaximumSampleGapSeconds)
            {
                flags |= MotionFlags.TrackingGap;
                SetBaseline(sample, projection, resetFilter: true);
                return Metrics(
                    sample,
                    valid: false,
                    projection,
                    error,
                    tangentialError,
                    lateralError,
                    normalError,
                    speedValid: false,
                    signedSpeedMps: 0d,
                    filteredSpeedMps: 0d,
                    speedClass: SpeedClass.Invalid,
                    travelAngleValid: false,
                    travelAngleRadians: 0d,
                    workAngleValid,
                    workAngle,
                    flags);
            }

            double deltaArc =
                projection.ArcLengthMetres - _previousProjection.ArcLengthMetres;

            if (deltaArc > _policy.ReverseToleranceMetres)
                flags |= MotionFlags.Forward;
            else if (deltaArc < -_policy.ReverseToleranceMetres)
                flags |= MotionFlags.Reverse;

            if (Math.Abs(deltaArc) > _policy.MaximumArcJumpMetres)
            {
                flags |= MotionFlags.Discontinuity | MotionFlags.SkippedRegion;

                // The jump is not allowed to contaminate derivative/filter history.
                // The current sample becomes the new baseline for the next sample.
                SetBaseline(sample, projection, resetFilter: true);
                return Metrics(
                    sample,
                    valid: false,
                    projection,
                    error,
                    tangentialError,
                    lateralError,
                    normalError,
                    speedValid: false,
                    signedSpeedMps: 0d,
                    filteredSpeedMps: 0d,
                    speedClass: SpeedClass.Invalid,
                    travelAngleValid: false,
                    travelAngleRadians: 0d,
                    workAngleValid,
                    workAngle,
                    flags);
            }

            double signedSpeed = deltaArc / deltaTime;

            if (!_hasFilteredSpeed)
            {
                _filteredSpeedMps = signedSpeed;
                _hasFilteredSpeed = true;
            }
            else
            {
                double alpha = 1d - Math.Exp(
                    -deltaTime / _policy.FilterTimeConstantSeconds);
                _filteredSpeedMps +=
                    alpha * (signedSpeed - _filteredSpeedMps);
            }

            Vec3 motion =
                sample.TipWorkpieceMetres - _previousSample.TipWorkpieceMetres;

            bool travelAngleValid =
                motion.Finite && motion.Length >= _policy.MinimumMotionMetres;
            double travelAngle = travelAngleValid
                ? SignedAngleAroundNormal(
                    projection.TangentWorkpiece,
                    motion.Normalized,
                    normal)
                : 0d;

            SpeedClass speedClass = ClassifySpeed(_filteredSpeedMps);

            SetBaseline(sample, projection, resetFilter: false);

            return Metrics(
                sample,
                valid: true,
                projection,
                error,
                tangentialError,
                lateralError,
                normalError,
                speedValid: true,
                signedSpeedMps: signedSpeed,
                filteredSpeedMps: _filteredSpeedMps,
                speedClass,
                travelAngleValid,
                travelAngle,
                workAngleValid,
                workAngle,
                flags);
        }

        private bool MatchesContext(PathSample sample) =>
            string.Equals(sample.SessionId, _context.SessionId, StringComparison.Ordinal) &&
            string.Equals(sample.AttemptId, _context.AttemptId, StringComparison.Ordinal) &&
            string.Equals(sample.ContentHash, _context.ContentHash, StringComparison.Ordinal);

        private SpeedClass ClassifySpeed(double filteredSpeedMps)
        {
            double magnitude = Math.Abs(filteredSpeedMps);
            if (magnitude < _policy.MinimumSpeedMps)
                return SpeedClass.TooSlow;
            if (magnitude > _policy.MaximumSpeedMps)
                return SpeedClass.TooFast;
            return SpeedClass.SpeedCorrect;
        }

        private void SetBaseline(
            PathSample sample,
            SeamProjection projection,
            bool resetFilter)
        {
            _previousSample = sample;
            _previousProjection = projection;
            _hasPrevious = true;

            if (resetFilter)
            {
                _hasFilteredSpeed = false;
                _filteredSpeedMps = 0d;
            }
        }

        private PathMetrics Invalid(
            PathSample sample,
            MotionFlags flags,
            SeamProjection projection = default)
        {
            return new PathMetrics(
                sample.SessionId,
                sample.AttemptId,
                sample.ContentHash,
                _spline.Id,
                false,
                projection,
                default,
                0d,
                0d,
                0d,
                false,
                0d,
                0d,
                SpeedClass.Invalid,
                false,
                0d,
                false,
                0d,
                flags);
        }

        private PathMetrics Metrics(
            PathSample sample,
            bool valid,
            SeamProjection projection,
            Vec3 error,
            double tangentialError,
            double lateralError,
            double normalError,
            bool speedValid,
            double signedSpeedMps,
            double filteredSpeedMps,
            SpeedClass speedClass,
            bool travelAngleValid,
            double travelAngleRadians,
            bool workAngleValid,
            double workAngleRadians,
            MotionFlags flags)
        {
            return new PathMetrics(
                sample.SessionId,
                sample.AttemptId,
                sample.ContentHash,
                _spline.Id,
                valid,
                projection,
                error,
                tangentialError,
                lateralError,
                normalError,
                speedValid,
                signedSpeedMps,
                filteredSpeedMps,
                speedClass,
                travelAngleValid,
                travelAngleRadians,
                workAngleValid,
                workAngleRadians,
                flags);
        }

        private static bool IsUnitLike(Vec3 value) =>
            value.Finite && Math.Abs(value.Length - 1d) <= UnitVectorTolerance;

        private static double ClampUnit(double value) =>
            Math.Max(-1d, Math.Min(1d, value));

        private static double SignedAngleAroundNormal(
            Vec3 from,
            Vec3 to,
            Vec3 normal)
        {
            return Math.Atan2(
                Vec3.Dot(Vec3.Cross(from, to), normal),
                ClampUnit(Vec3.Dot(from, to)));
        }
    }
}
