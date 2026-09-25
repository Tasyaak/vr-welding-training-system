using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeldingTrainer.Domain;
using WeldingTrainer.Presentation;

namespace WeldingTrainer.Integration.Editor
{
    // Editor-only, explicitly synthetic input. Never included in a Quest player.
    [InitializeOnLoad]
    public static class FusionMvpRehearsal
    {
        public static void BuildAndRun()
        {
            FusionMvpBuilder.Build();
            Run();
        }
        const string Key = "FusionMvp.Rehearsal";
        static double started, lastSubmit, weldingStarted, reviewingAt;
        static bool captured;
        static double lastMotion;
        static FusionMvpRehearsal()
        {
            EditorApplication.update += Update;
        }

        [MenuItem("Welding Trainer/Rehearse Fusion MVP (synthetic, Editor only)")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(FusionMvpBuilder.Scene);
            var composition = UnityEngine.Object.FindFirstObjectByType<FusionMvpComposition>();
            composition.EditorRehearsal = true;
            composition.right.enabled = false;
            composition.registration.enabled = false;
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour.GetType().Namespace?.StartsWith("Meta.") == true || behaviour.GetType().Name.StartsWith("OVR"))
                    behaviour.enabled = false;
            foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                camera.enabled = false;
            var eye = composition.registration.rig.centerEyeAnchor;
            eye.position = new Vector3(0, 1.45f, -.02f);
            eye.rotation = Quaternion.Euler(30, 0, 0);
            var cam = eye.GetComponent<Camera>();
            cam.enabled = true;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.025f, .045f, .07f);
            cam.fieldOfView = 65;
            UnityEditor.SessionState.SetBool(Key, true);
            UnityEditor.SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 90);
            EditorApplication.EnterPlaymode();
        }

        static void Update()
        {
            if (!UnityEditor.SessionState.GetBool(Key, false))
                return;
            if (EditorApplication.timeSinceStartup > UnityEditor.SessionState.GetFloat(Key + "deadline", 0))
            {
                End(false, "Rehearsal timeout");
                return;
            }

            if (!EditorApplication.isPlaying)
                return;
            var c = UnityEngine.Object.FindFirstObjectByType<FusionMvpComposition>();
            if (c?.State == null)
                return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (started == 0 && c.State.Session == WeldingTrainer.Domain.SessionState.Ready)
            {
                started = now;
                AddReference(c);
            }

            if (c.State.Session == WeldingTrainer.Domain.SessionState.Ready && now - lastSubmit > .3)
            {
                lastSubmit = now;
                c.EditorSubmit();
            }

            if (c.State.Session == WeldingTrainer.Domain.SessionState.Running)
            {
                if (weldingStarted == 0)
                    weldingStarted = now;
                c.EditorTrigger = true;
                c.EditorArc = Math.Min(.18474568820830878, c.EditorArc + Math.Min(.025, lastMotion == 0 ? 0 : now-lastMotion) * .025);
                lastMotion=now;
                if (!captured && c.EditorArc > .09)
                {
                    captured = true;
                    Capture(c, "fusion-welding.png");
                }

                if (c.EditorArc >= .18474568820830878)
                    c.EditorFinish();
            }

            if (c.State.Session == WeldingTrainer.Domain.SessionState.Reviewing && c.Summary != null)
            {
                if (reviewingAt == 0)
                    reviewingAt = now;
                if (now - reviewingAt < 1)
                    return;
                Capture(c, "fusion-results.png");
                var s = c.Summary;
                bool passed = s.recordingComplete && s.attemptedMetres > .17 && s.activeSeconds > 5 && s.speedSampleSeconds > 5 && captured;
                File.WriteAllText("../artifacts/fusion-rehearsal-summary.json", JsonUtility.ToJson(s, true));
                End(passed, $"covered={s.attemptedMetres:F4} good={s.acceptableMetres:F4} active={s.activeSeconds:F2} saved={s.recordingComplete}");
            }

            if (c.State.Session == WeldingTrainer.Domain.SessionState.Faulted)
                End(false, "Coordinator faulted");
        }

        static void AddReference(FusionMvpComposition c)
        {
            // Only the desktop rehearsal needs opaque CAD context; Quest uses passthrough.
            foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.enabled=false;
            var cameraObject=new GameObject("Editor rehearsal camera");
            var observer=cameraObject.AddComponent<Camera>(); observer.clearFlags=CameraClearFlags.SolidColor;
            observer.backgroundColor=new Color(.025f,.045f,.07f);observer.fieldOfView=65;
            observer.transform.position=new Vector3(-.08f,1.32f,.06f);
            observer.transform.LookAt(new Vector3(0,1.02f,.65f));
            c.view.Head=observer.transform;
            var content = c.registration.catalog.Freeze().Bindings.GetEnumerator();
            content.MoveNext();
            var geometry = content.Current.WorkpieceGeometry;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var indices = new System.Collections.Generic.List<int>();
            foreach (var s in geometry.Surfaces)
            {
                int i = vertices.Count;
                foreach (var p in new[]
                {
                    s.A,
                    s.B,
                    s.C
                }

                )
                    vertices.Add(new Vector3((float)p.x, (float)p.y, (float)-p.z));
                indices.Add(i);
                indices.Add(i + 2);
                indices.Add(i + 1);
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            var part = new GameObject("EDITOR ONLY - CAD context");
            part.transform.SetParent(c.view.Workpiece, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(.22f, .29f, .36f)
            };
        }

        static void Capture(FusionMvpComposition c, string filename)
        {
            var camera = c.view.Head.GetComponent<Camera>();
            var rt = new RenderTexture(1600, 1000, 24);
            camera.targetTexture = rt;
            camera.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            Directory.CreateDirectory("../artifacts");
            File.WriteAllBytes("../artifacts/" + filename, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = old;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(rt);
        }

        static void End(bool success, string message)
        {
            UnityEditor.SessionState.SetBool(Key, false);
            Debug.Log((success ? "FUSION_REHEARSAL_PASS " : "FUSION_REHEARSAL_FAIL ") + message);
            if (UnityEngine.Application.isBatchMode)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.ExitPlaymode();
        }
    }
}
