using System;
using System.IO;
using System.Text;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Integration
{
    // Small MVP adaptation of #72: local JSONL + atomic summary, explicit partial
    // attempts, no background queue/replay/retention subsystem in the presentation path.
    public sealed class FusionLocalRecording : IRecorderPort
    {
        readonly string root;
        StreamWriter writer;
        string folder;
        double flushAt;
        public bool Available { get; private set; } = true;
        public string Error { get; private set; }
        public string SavedPath { get; private set; }

        public Func<bool, FusionAttemptSummary> Summarize;
        public FusionLocalRecording(string persistentRoot)
        {
            root = Path.Combine(persistentRoot, "FusionSessions");
        }

        public void Begin(AttemptConfiguration attempt)
        {
            try
            {
                folder = Path.Combine(root, attempt.AttemptId);
                Directory.CreateDirectory(folder);
                writer = new StreamWriter(Path.Combine(folder, "samples.jsonl.partial"), false, new UTF8Encoding(false));
                Write(new Record { kind = "configuration", attemptId = attempt.AttemptId, profile = attempt.Process.Profile.Hash, seam = attempt.Process.SeamId, registrationGeneration = attempt.RegistrationGeneration });
                writer.Flush();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public void Append(ProcessEvent e) => Write(new Record { kind = "event", time = e.MonotonicSeconds, sessionId = e.SessionId, attemptId = e.AttemptId, sequence = e.Sequence, state = e.Type.ToString(), reason = e.Payload });
        public void Sample(Record record)
        {
            Write(record);
            if (record.time >= flushAt)
            {
                flushAt = record.time + 1;
                try
                {
                    writer?.Flush();
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            }
        }

        void Write(Record record)
        {
            if (!Available || writer == null)
                return;
            try
            {
                writer.WriteLine(JsonUtility.ToJson(record));
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public bool Finish(string attemptId, bool interrupted, out string error)
        {
            try
            {
                if (!Available || writer == null)
                    throw new IOException(Error ?? "No active recording");
                writer.Flush();
                writer.Dispose();
                writer = null;
                var summary = Summarize(interrupted);
                summary.recordingComplete = true;
                string pending = Path.Combine(folder, "summary.json.partial");
                File.WriteAllText(pending, JsonUtility.ToJson(summary, true));
                File.Move(Path.Combine(folder, "samples.jsonl.partial"), Path.Combine(folder, "samples.jsonl"));
                File.Move(pending, Path.Combine(folder, "summary.json"));
                SavedPath = folder;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                Fail(e);
                error = Error;
                return false;
            }
        }

        public void Teardown()
        {
            writer?.Dispose();
            writer = null;
        }

        void Fail(Exception e)
        {
            Available = false;
            Error = e.Message;
            Debug.LogError("Fusion recording: " + Error);
        }

        [Serializable]
        public sealed class Record
        {
            public string kind, sessionId, attemptId, profile, seam, contentHash, binding, calibration;
            public string state, reason, feedback;
            public double time, triggerAnalog, assistance, arcMetres, speedMps, positionMetres, travelRadians, workRadians;
            public double startArcMetres, endArcMetres;
            public long sequence, registrationGeneration, inputGeneration, originGeneration, activationEpoch;
            public bool valid, speedValid, travelValid, workValid, requested, active, acceptable, deltaValid;
            public Vector3 tipWorkpiece, headWorkpiece, incidentWorkpiece;
        }
    }
}
