using System;
using System.Collections.Generic;
using System.Linq;

namespace WeldingTrainer.Domain
{
    public sealed class ReferencePointDefinition
    {
        public string Id { get; }
        public Vector3d FixturePositionMetres { get; }
        public ReferencePointDefinition(string id, Vector3d position) { Id = id; FixturePositionMetres = position; }
    }

    public sealed class CalibrationPolicy
    {
        public int SamplesPerPoint { get; }
        public double StabilityRadiusMetres { get; }
        public double MaxResidualMetres { get; }
        public double MinPointSeparationMetres { get; }
        public double MinTriangleAreaSquareMetres { get; }

        public CalibrationPolicy(int samplesPerPoint, double stabilityRadius, double maxResidual,
            double minPointSeparation, double minTriangleArea)
        {
            SamplesPerPoint = samplesPerPoint;
            StabilityRadiusMetres = stabilityRadius;
            MaxResidualMetres = maxResidual;
            MinPointSeparationMetres = minPointSeparation;
            MinTriangleAreaSquareMetres = minTriangleArea;
        }
    }

    public sealed class FixtureDefinition
    {
        private readonly ReferencePointDefinition[] _points;
        public string Id { get; }
        public int Version { get; }
        public IReadOnlyList<ReferencePointDefinition> ReferencePoints => Array.AsReadOnly(_points);
        public Vector3d NominalDimensionsMetres { get; }
        public RigidPose WorkpieceFromFixture { get; }
        public CalibrationPolicy Calibration { get; }

        public FixtureDefinition(string id, int version, IEnumerable<ReferencePointDefinition> points,
            Vector3d nominalDimensions, RigidPose workpieceFromFixture, CalibrationPolicy calibration)
        {
            Id = id; Version = version; _points = points?.ToArray() ?? Array.Empty<ReferencePointDefinition>();
            NominalDimensionsMetres = nominalDimensions; WorkpieceFromFixture = workpieceFromFixture;
            Calibration = calibration;
        }

        public ValidationResult Validate()
        {
            var result = new ValidationResult();
            ValidationRules.Id(Id, "id", result);
            if (Version < 1) result.Add("version", "must be at least 1");
            if (_points.Length != 4) result.Add("referencePoints", "must contain exactly four ordered points");
            ValidationRules.UniqueIds(_points, x => x.Id, "referencePoints", result);
            for (int i = 0; i < _points.Length; i++)
            {
                ValidationRules.Id(_points[i].Id, $"referencePoints[{i}].id", result);
                if (!_points[i].FixturePositionMetres.IsFinite)
                    result.Add($"referencePoints[{i}].positionMetres", "must contain finite components");
            }
            if (!NominalDimensionsMetres.IsFinite || NominalDimensionsMetres.X <= 0 ||
                NominalDimensionsMetres.Y <= 0 || NominalDimensionsMetres.Z <= 0)
                result.Add("nominalDimensionsMetres", "must be finite and positive on every axis");
            if (!WorkpieceFromFixture.PositionMetres.IsFinite || !WorkpieceFromFixture.Rotation.IsFinite ||
                Math.Abs(WorkpieceFromFixture.Rotation.Length - 1) > 1e-5)
                result.Add("workpieceFromFixture", "must be a finite rigid pose with a unit quaternion");
            if (Calibration == null) result.Add("calibration", "is required");
            else
            {
                if (Calibration.SamplesPerPoint < 1) result.Add("calibration.samplesPerPoint", "must be at least 1");
                ValidationRules.Positive(Calibration.StabilityRadiusMetres, "calibration.stabilityRadiusMetres", result);
                ValidationRules.Positive(Calibration.MaxResidualMetres, "calibration.maxResidualMetres", result);
                ValidationRules.Positive(Calibration.MinPointSeparationMetres, "calibration.minPointSeparationMetres", result);
                ValidationRules.Positive(Calibration.MinTriangleAreaSquareMetres, "calibration.minTriangleAreaSquareMetres", result);
                ValidateLayout(result);
            }
            return result;
        }

        private void ValidateLayout(ValidationResult result)
        {
            if (_points.Length != 4 || _points.Any(x => !x.FixturePositionMetres.IsFinite)) return;
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (Vector3d.Distance(_points[i].FixturePositionMetres, _points[j].FixturePositionMetres)
                        < Calibration.MinPointSeparationMetres)
                        result.Add("referencePoints", $"points {i} and {j} are duplicate or insufficiently separated");

            double maxArea = 0;
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    for (int k = j + 1; k < 4; k++)
                    {
                        Vector3d a = _points[j].FixturePositionMetres - _points[i].FixturePositionMetres;
                        Vector3d b = _points[k].FixturePositionMetres - _points[i].FixturePositionMetres;
                        maxArea = Math.Max(maxArea, Vector3d.Cross(a, b).Length * 0.5);
                    }
            if (maxArea < Calibration.MinTriangleAreaSquareMetres)
                result.Add("referencePoints", "layout is collinear or insufficiently spread");
        }
    }
}
