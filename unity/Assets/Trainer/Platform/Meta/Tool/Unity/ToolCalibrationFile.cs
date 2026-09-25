using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Platform.Meta.Tool.Unity
{
    [Serializable]
    public sealed class ToolCalibrationFile
    {
        public int schemaVersion = 1;
        public string assemblyId, revision, interactionProfile, evidence, qualifiedUtc;
        public bool qualified;
        public string convention = "RH-Sz/grip/metres/xyzw; Tool+Z=body; incident=-Z";
        public string cadSha256 = "3ef01a87a06bcc5366b3dc441b3ede4fc51ae2aee63d9536b30f24510b8442a6";
        public Vec3 controllerTipMetres;
        public Quat controllerToolRotation = new Quat(0, 0, 0, 1);
        // Nominal shoulder-to-cap distance, NOT a measured effective contact point.
        public PoseData toolFromTip = new PoseData { destination = "Tool", source = "Tip", scale = 1, position = new Vec3(0, 0, -.04), rotation = new Quat(0, 0, 0, 1) };
        public double maxObservedTipErrorMm, maxObservedOrientationErrorDegrees;
        // Qualification gates, not claims about Quest accuracy; select for the exercise before measuring.
        public double acceptedTipErrorMm = 3, acceptedOrientationErrorDegrees = 3;
        public ToolCalibration Freeze()
        {
            if (schemaVersion != 1 || convention != "RH-Sz/grip/metres/xyzw; Tool+Z=body; incident=-Z") throw new ArgumentException("Unsupported calibration convention");
            if (qualified && (string.IsNullOrWhiteSpace(qualifiedUtc) || !ToolMath.Finite(maxObservedTipErrorMm) || maxObservedTipErrorMm <= 0 || !ToolMath.Finite(maxObservedOrientationErrorDegrees) || maxObservedOrientationErrorDegrees <= 0)) throw new ArgumentException("Record nonzero observed qualification error bounds");
            if (!ToolMath.Finite(acceptedTipErrorMm) || acceptedTipErrorMm <= 0 || !ToolMath.Finite(acceptedOrientationErrorDegrees) || acceptedOrientationErrorDegrees <= 0) throw new ArgumentException("Explicit positive acceptance tolerances required");
            if (qualified && (maxObservedTipErrorMm > acceptedTipErrorMm || maxObservedOrientationErrorDegrees > acceptedOrientationErrorDegrees)) throw new ArgumentException("Observed errors exceed this profile's acceptance tolerances");
            var tip = new RigidPose(toolFromTip);
            var tool = ToolCalibration.FromTipAndOrientation(controllerTipMetres, controllerToolRotation, tip);
            return new ToolCalibration(assemblyId, revision + ":" + Hash(), interactionProfile, evidence, qualified, tool, tip);
        }
        public string Hash()
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(this)))).Replace("-", "").ToLowerInvariant();
        }
        public static ToolCalibrationFile Parse(string json)
        {
            var file = JsonUtility.FromJson<ToolCalibrationFile>(json);
            if (file == null) throw new ArgumentException("Missing calibration");
            file.Freeze(); return file;
        }
    }
}
