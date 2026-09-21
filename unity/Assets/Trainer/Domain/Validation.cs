using System;
using System.Collections.Generic;
using System.Linq;

namespace WeldingTrainer.Domain
{
    public sealed class ValidationIssue
    {
        public string Path { get; }
        public string Message { get; }
        public ValidationIssue(string path, string message) { Path = path; Message = message; }
        public override string ToString() => $"{Path}: {Message}";
    }

    public sealed class ValidationResult
    {
        private readonly List<ValidationIssue> _issues = new();
        public IReadOnlyList<ValidationIssue> Issues => _issues;
        public bool IsValid => _issues.Count == 0;
        public void Add(string path, string message) => _issues.Add(new ValidationIssue(path, message));
        public void Merge(string prefix, ValidationResult other)
        {
            foreach (var issue in other.Issues)
                Add(string.IsNullOrEmpty(prefix) ? issue.Path : $"{prefix}.{issue.Path}", issue.Message);
        }
        public void ThrowIfInvalid()
        {
            if (!IsValid) throw new ContentValidationException(_issues);
        }
    }

    public sealed class ContentValidationException : Exception
    {
        public IReadOnlyList<ValidationIssue> Issues { get; }
        public ContentValidationException(IEnumerable<ValidationIssue> issues)
            : base(string.Join(Environment.NewLine, issues.Select(x => x.ToString())))
        {
            Issues = issues.ToArray();
        }
    }

    internal static class ValidationRules
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static bool Id(string value, string path, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(value)) { result.Add(path, "must be a non-empty stable ID"); return false; }
            return true;
        }

        public static void Finite(double value, string path, ValidationResult result)
        {
            if (!IsFinite(value)) result.Add(path, "must be finite");
        }

        public static void Positive(double value, string path, ValidationResult result)
        {
            Finite(value, path, result);
            if (IsFinite(value) && value <= 0) result.Add(path, "must be greater than zero");
        }

        public static void Unit(Vector3d value, string path, ValidationResult result, double tolerance = 1e-5)
        {
            if (!value.IsFinite) { result.Add(path, "must contain finite components"); return; }
            if (Math.Abs(value.Length - 1.0) > tolerance) result.Add(path, "must be a non-degenerate unit vector");
        }

        public static void UniqueIds<T>(IEnumerable<T> values, Func<T, string> id, string path, ValidationResult result)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (var value in values)
            {
                string current = id(value);
                if (!string.IsNullOrWhiteSpace(current) && !seen.Add(current))
                    result.Add($"{path}[{index}].id", $"duplicates stable ID '{current}'");
                index++;
            }
        }
    }
}
