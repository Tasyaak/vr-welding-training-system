using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace WeldingTrainer.Prototype
{
    /// <summary>
    /// Small, bounded recorder for the presentation prototype.
    /// Production storage will replace this class after the demo.
    /// </summary>
    public sealed class PrototypeSessionRecorder
    {
        private const int MaximumSamples = 18000;
        private readonly List<SampleRecord> _samples = new List<SampleRecord>(4096);

        private string _sessionId;
        private string _startedUtc;

        public bool IsActive { get; private set; }
        public string LastSavedDirectory { get; private set; }
        public string CurrentSessionId => _sessionId;

        public void Begin()
        {
            _samples.Clear();
            _sessionId = Guid.NewGuid().ToString("N");
            _startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            LastSavedDirectory = string.Empty;
            IsActive = true;
        }

        public void Record(
            float elapsedSeconds,
            Vector3 toolPosition,
            float seamProgress,
            float distanceMetres,
            float speedMetresPerSecond,
            float travelAngleDegrees,
            float workAngleDegrees,
            bool orientationGood,
            bool triggerPressed,
            bool weldingAllowed)
        {
            if (!IsActive || _samples.Count >= MaximumSamples)
            {
                return;
            }

            _samples.Add(new SampleRecord
            {
                elapsedSeconds = elapsedSeconds,
                toolX = toolPosition.x,
                toolY = toolPosition.y,
                toolZ = toolPosition.z,
                seamProgress = seamProgress,
                distanceMetres = distanceMetres,
                speedMetresPerSecond = speedMetresPerSecond,
                travelAngleDegrees = travelAngleDegrees,
                workAngleDegrees = workAngleDegrees,
                orientationGood = orientationGood,
                triggerPressed = triggerPressed,
                weldingAllowed = weldingAllowed
            });
        }

        public string Finish(
            bool completed,
            string reason,
            float completion,
            float averageErrorMetres,
            float averageSpeedMetresPerSecond,
            float qualityInRangePercent,
            int trackingInterruptionCount,
            float invalidTrackingSeconds,
            float attemptElapsedSeconds,
            float weldingActiveSeconds,
            float blockedTriggerSeconds)
        {
            if (!IsActive)
            {
                return LastSavedDirectory;
            }

            IsActive = false;
            string root = Path.Combine(Application.persistentDataPath, "PrototypeSessions");
            string sessionDirectory = Path.Combine(root, _sessionId);
            Directory.CreateDirectory(sessionDirectory);

            var summary = new SummaryRecord
            {
                schemaVersion = 5,
                sessionId = _sessionId,
                startedUtc = _startedUtc,
                finishedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                completed = completed,
                finishReason = reason,
                completion = completion,
                averageErrorMetres = averageErrorMetres,
                averageSpeedMetresPerSecond = averageSpeedMetresPerSecond,
                qualityInRangePercent = qualityInRangePercent,
                trackingInterruptionCount = trackingInterruptionCount,
                invalidTrackingSeconds = invalidTrackingSeconds,
                attemptElapsedSeconds = attemptElapsedSeconds,
                weldingActiveSeconds = weldingActiveSeconds,
                blockedTriggerSeconds = blockedTriggerSeconds,
                sampleCount = _samples.Count
            };

            File.WriteAllText(
                Path.Combine(sessionDirectory, "summary.json"),
                JsonUtility.ToJson(summary, true),
                Encoding.UTF8);
            WriteSamples(Path.Combine(sessionDirectory, "samples.csv"));

            LastSavedDirectory = sessionDirectory;
            Debug.Log($"Prototype session saved to: {sessionDirectory}");
            return sessionDirectory;
        }

        private void WriteSamples(string path)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine(
                "elapsed_s,tool_x_m,tool_y_m,tool_z_m,seam_progress,distance_m,speed_mps,travel_angle_deg,work_angle_deg,orientation_good,trigger,welding_allowed");

            foreach (SampleRecord sample in _samples)
            {
                writer.Write(sample.elapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.toolX.ToString("0.000000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.toolY.ToString("0.000000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.toolZ.ToString("0.000000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.seamProgress.ToString("0.000000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.distanceMetres.ToString("0.000000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.speedMetresPerSecond.ToString("0.000000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.travelAngleDegrees.ToString("0.000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.workAngleDegrees.ToString("0.000", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(sample.orientationGood ? "1" : "0");
                writer.Write(',');
                writer.Write(sample.triggerPressed ? "1" : "0");
                writer.Write(',');
                writer.WriteLine(sample.weldingAllowed ? "1" : "0");
            }
        }

        [Serializable]
        private sealed class SummaryRecord
        {
            public int schemaVersion;
            public string sessionId;
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
        }

        private struct SampleRecord
        {
            public float elapsedSeconds;
            public float toolX;
            public float toolY;
            public float toolZ;
            public float seamProgress;
            public float distanceMetres;
            public float speedMetresPerSecond;
            public float travelAngleDegrees;
            public float workAngleDegrees;
            public bool orientationGood;
            public bool triggerPressed;
            public bool weldingAllowed;
        }
    }
}
