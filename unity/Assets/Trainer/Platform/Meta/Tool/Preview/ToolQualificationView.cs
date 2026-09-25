#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Platform.Meta.Tool.Unity;

namespace WeldingTrainer.Platform.Meta.Tool.Preview
{
    // No input polling here. This standalone composition is the sole Capture consumer.
    public sealed class ToolQualificationView : MonoBehaviour
    {
        public RightControllerSource source;
        public TextMesh status;
        public Material lineMaterial;
        public string physicalAssemblyId = "REPLACE-with-glued-assembly-id";
        private ToolCalibrationFile draft;
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private readonly List<Material> materials = new List<Material>();
        private Transform origin, tip;
        private int parameter;
        private double nextStep, holdStart = -1;
        private bool saveLatched;
        private string notice = "Unqualified: align tip AND full tool frame; see runbook.";
        private static readonly string[] Names = { "Tip X mm", "Tip Y mm", "Tip Z mm", "Tool X rotation deg", "Tool Y rotation deg", "Tool Z rotation deg", "Shoulder to tip mm", "Observed tip error mm", "Observed orientation error deg", "SAVE DRAFT: hold UP 2s", "QUALIFY + SAVE: hold UP 2s" };
        private string PathName => Path.Combine(Application.persistentDataPath, "right-tool-calibration.json");
        private void Start()
        {
            draft = new ToolCalibrationFile { assemblyId = physicalAssemblyId, revision = "draft" };
            if (File.Exists(PathName))
                try
                {
                    var loaded = ToolCalibrationFile.Parse(File.ReadAllText(PathName));
                    if (loaded.assemblyId != physicalAssemblyId) throw new ArgumentException("Saved profile belongs to a different glued assembly");
                    draft = loaded; notice = "Loaded " + draft.revision;
                }
                catch (Exception e) { notice = "Calibration rejected: " + e.Message; }
            source.SetCalibration(draft.Freeze());
            source.SetContext(ToolInputContext.Registration);
            for (int i = 0; i < 10; i++)
            {
                var line = new GameObject("Tool qualification line " + i).AddComponent<LineRenderer>();
                line.transform.SetParent(transform, false); line.positionCount = 2; line.startWidth = line.endWidth = .0015f;
                var material = new Material(lineMaterial); materials.Add(material); line.sharedMaterial = material;
                material.color = i < 6 ? new[] { Color.red, Color.green, Color.blue }[i % 3] : i == 6 ? Color.cyan : Color.yellow;
                lines.Add(line);
            }
            origin = Marker("Controller origin", .006f, Color.white); tip = Marker("Effective tip", .004f, Color.yellow);
        }
        private Transform Marker(string name, float diameter, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(transform, false); go.transform.localScale = Vector3.one * diameter;
            Destroy(go.GetComponent<Collider>()); var material = new Material(lineMaterial); material.color = color; materials.Add(material); go.GetComponent<Renderer>().sharedMaterial = material; return go.transform;
        }
        private void Line(int i, Vec3 a, Vec3 b) { lines[i].SetPosition(0, UnitySpatialPose.ToUnity(a)); lines[i].SetPosition(1, UnitySpatialPose.ToUnity(b)); }
        private void Axes(int start, RigidPose pose, double size)
        {
            for (int i = 0; i < 3; i++) Line(start + i, pose.Position, pose.TransformPoint(new Vec3(i == 0 ? size : 0, i == 1 ? size : 0, i == 2 ? size : 0)));
        }
        private void LateUpdate()
        {
            if (draft == null) return;
            var s = source.Capture();
            bool estop = false;
            foreach (var c in s.Commands)
            {
                if (c.Type == ToolCommandType.EStopPressed) { estop = true; notice = "EStopPressed retained; no reset. Qualification edit stopped."; }
                if (c.Type == ToolCommandType.Submit) { parameter = (parameter + 1) % Names.Length; holdStart = -1; }
                Debug.Log($"Tool command seq={c.Sequence} t={c.MonotonicSeconds:F6} context={c.InputContextGeneration} type={c.Type}");
            }
            bool visible = s.Controller.Pose.HasValue && s.Tool.Pose.HasValue && s.Tip.Pose.HasValue && s.Focused && !s.Paused;
            foreach (var line in lines) line.enabled = visible;
            origin.gameObject.SetActive(visible); tip.gameObject.SetActive(visible);
            if (visible)
            {
                var c = s.Controller.Pose.Value; var t = s.Tool.Pose.Value; var p = s.Tip.Pose.Value;
                Axes(0, c, .08); Axes(3, t, .035);
                Line(6, p.Position, p.Position + t.TransformDirection(new Vec3(0, 0, -.06)));
                Line(7, c.Position, t.Position); Line(8, t.Position, p.Position); Line(9, c.Position, p.Position);
                origin.position = UnitySpatialPose.ToUnity(c.Position); tip.position = UnitySpatialPose.ToUnity(p.Position);
            }
            if (!estop && visible) Edit(s); else holdStart = -1;
            var q = draft.controllerToolRotation; var v = draft.controllerTipMetres;
            status.text = $"RIGHT TOOL — DEVELOPMENT ONLY\nAssembly: {draft.assemblyId}\n{(draft.qualified ? "QUALIFIED for recorded profile" : "UNQUALIFIED candidate")} / usable: {s.IsUsableAt(Time.realtimeSinceStartupAsDouble, source.OriginGeneration)}\nDevice: {s.DeviceValid} focus:{s.Focused} pause:{s.Paused}\nController P:{s.Controller.PositionValid} R:{s.Controller.RotationValid} {s.Controller.Failure}\nHead P:{s.Head.PositionValid} R:{s.Head.RotationValid} / origin:{s.OriginGeneration}\nProfile: {s.InteractionProfile ?? "unavailable"}\nTrigger: {s.TriggerAnalog?.ToString("F3") ?? "missing"} held:{s.TriggerHeld}\nRH tip mm: {v.x * 1000:F1}, {v.y * 1000:F1}, {v.z * 1000:F1}\nRH Tool q xyzw: {q.x:F4}, {q.y:F4}, {q.z:F4}, {q.w:F4}\nToolFromTip Z: {draft.toolFromTip.position.z * 1000:F1} mm\nObserved error: {draft.maxObservedTipErrorMm:F1} mm / {draft.maxObservedOrientationErrorDegrees:F1} deg\nA: select; stick LEFT/RIGHT: adjust (UP=fine)\nSelected: {Names[parameter]}\nController RGB = RH XYZ (blue opposite Unity/demo Z)\nTool RGB; cyan = incident -Z; yellow = rigid links\n{notice}";
        }
        private void Edit(ToolHeadSnapshot snapshot)
        {
            var stick = source.Navigation; double now = Time.realtimeSinceStartupAsDouble;
            if (parameter >= 9)
            {
                if (stick.y < .8f) { holdStart = -1; saveLatched = false; return; }
                if (holdStart < 0) holdStart = now;
                if (!saveLatched && now - holdStart >= 2) { saveLatched = true; Save(parameter == 10, snapshot); }
                return;
            }
            if (Math.Abs(stick.x) < .6 || now < nextStep) return;
            nextStep = now + .12;
            double step = Math.Sign(stick.x) * (stick.y > .5 ? .1 : 1);
            draft.qualified = false; draft.qualifiedUtc = null; draft.evidence = null;
            if (parameter < 3)
            {
                var p = draft.controllerTipMetres;
                if (parameter == 0) p.x += step / 1000; if (parameter == 1) p.y += step / 1000; if (parameter == 2) p.z += step / 1000;
                draft.controllerTipMetres = p;
            }
            else if (parameter < 6)
            {
                // Explicit RH rotations about CONTROLLER axes. Quaternion is saved; no hidden Euler basis fix.
                double a = step * Math.PI / 360, sin = Math.Sin(a);
                var delta = new Quat(parameter == 3 ? sin : 0, parameter == 4 ? sin : 0, parameter == 5 ? sin : 0, Math.Cos(a));
                draft.controllerToolRotation = delta * draft.controllerToolRotation;
            }
            else if (parameter == 6) draft.toolFromTip.position.z = Math.Min(0, draft.toolFromTip.position.z - step / 1000);
            else if (parameter == 7) draft.maxObservedTipErrorMm = Math.Max(0, draft.maxObservedTipErrorMm + step);
            else draft.maxObservedOrientationErrorDegrees = Math.Max(0, draft.maxObservedOrientationErrorDegrees + step);
            draft.revision = "draft"; source.SetCalibration(draft.Freeze()); notice = "Edited: qualification cleared. Save explicitly.";
        }
        private void Save(bool qualify, ToolHeadSnapshot snapshot)
        {
            if (qualify && (draft.assemblyId.StartsWith("REPLACE") || string.IsNullOrEmpty(snapshot.InteractionProfile) || draft.maxObservedTipErrorMm <= 0 || draft.maxObservedOrientationErrorDegrees <= 0))
            { notice = "Set unique assembly ID before build; record errors and active profile before qualification."; return; }
            try
            {
                draft.qualified = qualify; draft.interactionProfile = snapshot.InteractionProfile;
                draft.revision = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"); draft.qualifiedUtc = qualify ? DateTime.UtcNow.ToString("O") : null;
                draft.evidence = qualify ? "Operator confirmed tip, body axis, roll reference, multiple wrist orientations and measured error bounds per right-tool runbook." : null;
                var calibration = draft.Freeze();
                File.WriteAllText(PathName + ".tmp", JsonUtility.ToJson(draft, true));
                if (File.Exists(PathName)) File.Copy(PathName, PathName + ".previous", true);
                File.Copy(PathName + ".tmp", PathName, true); File.Delete(PathName + ".tmp");
                source.SetCalibration(calibration); notice = "Saved " + draft.revision;
                Debug.Log("Tool calibration path=" + PathName + " sha256=" + draft.Hash() + "\n" + JsonUtility.ToJson(draft, true));
            }
            catch (Exception e) { draft.qualified = false; source.SetCalibration(draft.Freeze()); notice = "Save failed: " + e.Message; }
        }
        private void OnDestroy() { foreach (var material in materials) Destroy(material); }
    }
}
#endif
