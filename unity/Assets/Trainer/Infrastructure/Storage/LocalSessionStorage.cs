using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Infrastructure
{
    /// Writes session data atomically to Application.persistentDataPath/Sessions/.
    /// Each attempt lands in Sessions/<sessionId>/<attemptId>/ as samples.csv and summary.json.
    /// summary.json is committed last; a partial file next to it marks an incomplete save.
    public sealed class LocalSessionStorage : ISessionStorage
    {
        private const int SchemaVersion = 1;
        private const int MaxSamples = 36000; // 10 Hz for one hour

        private string _sessionId;
        private WorkpieceDefinition _workpiece;
        private int _seamIndex;
        private readonly StringBuilder _csvBuffer = new();
        private int _sampleCount;
        private int _droppedSamples;

        public void BeginSession(string sessionId, WorkpieceDefinition workpiece, int seamIndex)
        {
            _sessionId = sessionId;
            _workpiece = workpiece;
            _seamIndex = seamIndex;
            _csvBuffer.Clear();
            _sampleCount = 0;
            _droppedSamples = 0;
            _csvBuffer.AppendLine(
                "elapsed_s,tool_x_m,tool_y_m,tool_z_m,seam_progress," +
                "distance_m,speed_mps,travel_angle_deg,work_angle_deg," +
                "orientation_good,trigger,welding_allowed");
        }

        public void RecordSample(ToolSample sample, EvaluationResult result, float elapsedSeconds)
        {
            if (_sampleCount >= MaxSamples)
            {
                _droppedSamples++;
                return;
            }
            _sampleCount++;
            _csvBuffer
                .Append(elapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.TipPosition.x.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.TipPosition.y.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.TipPosition.z.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.Progress.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.DistanceMetres.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.SpeedMetresPerSecond.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.TravelAngleDegrees.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.WorkAngleDegrees.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(result.OrientationGood ? '1' : '0').Append(',')
                .Append(sample.TriggerPressed ? '1' : '0').Append(',')
                .AppendLine(result.WeldingActive ? "1" : "0");
        }

        public void FinishAttempt(AttemptSummary summary)
        {
            string root = Path.Combine(Application.persistentDataPath, "Sessions");
            string dir = Path.Combine(root, summary.SessionId, summary.AttemptId);
            Directory.CreateDirectory(dir);

            string csvPath = Path.Combine(dir, "samples.csv");
            string summaryPath = Path.Combine(dir, "summary.json");
            string csvPartial = csvPath + ".partial";
            string summaryPartial = summaryPath + ".partial";

            byte[] csvBytes = new UTF8Encoding(false).GetBytes(_csvBuffer.ToString());
            string csvSha256 = ComputeSha256Hex(csvBytes);

            WriteAllBytes(csvPartial, csvBytes);

            var record = new SummaryRecord
            {
                schemaVersion = SchemaVersion,
                sessionId = summary.SessionId,
                attemptId = summary.AttemptId,
                startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                finishedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                completed = summary.Completed,
                finishReason = summary.FinishReason,
                completion = summary.Completion,
                averageErrorMetres = summary.AverageErrorMetres,
                averageSpeedMetresPerSecond = summary.AverageSpeedMetresPerSecond,
                qualityInRangePercent = summary.QualityInRangePercent,
                trackingInterruptionCount = summary.TrackingInterruptionCount,
                invalidTrackingSeconds = summary.InvalidTrackingSeconds,
                attemptElapsedSeconds = summary.AttemptElapsedSeconds,
                weldingActiveSeconds = summary.WeldingActiveSeconds,
                blockedTriggerSeconds = summary.BlockedTriggerSeconds,
                sampleCount = _sampleCount,
                droppedSampleCount = _droppedSamples,
                recordingTruncated = _droppedSamples > 0,
                csvByteLength = csvBytes.Length,
                csvSha256 = csvSha256,
                workpieceId = _workpiece?.WorkpieceId,
                seamId = _workpiece?.Seams[_seamIndex].SeamId,
            };

            WriteAllBytes(summaryPartial, new UTF8Encoding(false).GetBytes(
                JsonUtility.ToJson(record, true)));

            File.Move(csvPartial, csvPath);
            File.Move(summaryPartial, summaryPath);

            Debug.Log($"[LocalSessionStorage] Session saved to: {dir}");
        }

        private static void WriteAllBytes(string path, byte[] bytes)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        [Serializable]
        private sealed class SummaryRecord
        {
            public int schemaVersion;
            public string sessionId;
            public string attemptId;
            public string startedUtc;
            public string finishedUtc;
            public bool completed;
            public string finishReason;
            public float completion;
            public float averageErrorMetres;
            public float averageSpeedMetresPerSecond;
            public float qualityInRangePercent;
            public int trackingInterruptionCount;
            public float invalidTrackingSeconds;
            public float attemptElapsedSeconds;
            public float weldingActiveSeconds;
            public float blockedTriggerSeconds;
            public int sampleCount;
            public int droppedSampleCount;
            public bool recordingTruncated;
            public int csvByteLength;
            public string csvSha256;
            public string workpieceId;
            public string seamId;
        }
    }
}
