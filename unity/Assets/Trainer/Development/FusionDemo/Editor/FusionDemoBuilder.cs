using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace WeldingTrainer.FusionDemo.Editor
{
    public static class FusionDemoBuilder
    {
        private const string Folder = "Assets/Trainer/Development/FusionDemo/Generated";

        [MenuItem("Tools/Welding Trainer/Fusion/Create desktop presentation scene")]
        public static void CreateDesktopScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateDesktop(false);
        }

        // Entry point for reproducible command-line generation and validation.
        public static void GenerateAndCheck()
        {
            CreateDesktop(true);
            CheckCoverage();
            foreach (string shaderName in new[] { "WeldingTrainer/Fusion Metal", "WeldingTrainer/Fusion Spark" })
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    throw new InvalidOperationException("Shader failed: " + shaderName);
            }
            Debug.Log("FUSION_CHECKS_PASSED: coverage, scene references, dimensions and shader import.");
        }

        private static void CreateDesktop(bool batch)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.48f, 0.55f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.25f, 0.3f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.12f, 0.15f);
            var camera = new GameObject("Presentation Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0.15f, 1.35f, -1.05f);
            camera.transform.LookAt(new Vector3(0, 0.99f, 0));
            camera.fieldOfView = 48;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 25;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
            Light key = new GameObject("Key light", typeof(Light)).GetComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2;
            key.transform.rotation = Quaternion.Euler(45, -30, 0);
            var demo = CreateDemo();
            Material table = MaterialAsset("Table", "Universal Render Pipeline/Lit", new Color(0.065f, 0.09f, 0.13f));
            Cube("Presentation plinth", null, new Vector3(0, 0.85f, 0), new Vector3(1.25f, 0.09f, 0.4f), table);
            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/FusionPresentation.unity");
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
            if (demo.workpiece.lossyScale != Vector3.one || demo.bead == null || demo.sparks.sparkMaterial == null)
                throw new InvalidOperationException("Invalid generated demo references.");
            Debug.Log("Fusion presentation scene saved: " + path);
            if (!batch) Selection.activeGameObject = demo.gameObject;
        }

        [MenuItem("Tools/Welding Trainer/Fusion/Add to current XR scene")]
        public static void AddToXrScene()
        {
            if (UnityEngine.Object.FindAnyObjectByType<FusionWeldingDemo>() != null)
            { Debug.LogWarning("This scene already contains a Fusion demo."); return; }
            var demo = CreateDemo();
            Undo.RegisterCreatedObjectUndo(demo.gameObject, "Add Fusion demo");
            demo.controlMode = FusionWeldingDemo.ControlMode.XR;
            demo.showDesktopPanel = false;
            var tracking = GameObject.Find("TrackingSpace");
            if (tracking != null) demo.trackingOrigin = tracking.transform;
            foreach (var old in UnityEngine.Object.FindObjectsByType<WeldingTrainer.Prototype.PrototypeWeldingDemo>(FindObjectsSortMode.None))
            { Undo.RecordObject(old, "Disable previous prototype"); old.enabled = false; }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = demo.gameObject;
            Debug.Log("Fusion added. Verify Tracking Origin and position the workpiece before Play. Save as a separate scene.");
        }

        private static FusionWeldingDemo CreateDemo()
        {
            EnsureFolder(Folder);
            Material steel = MaterialAsset("Steel", "Universal Render Pipeline/Lit", new Color(0.32f, 0.37f, 0.42f));
            steel.SetFloat("_Metallic", 0.8f);
            steel.SetFloat("_Smoothness", 0.4f);
            Material beadMaterial = MaterialAsset("Bead", "WeldingTrainer/Fusion Metal", Color.gray);
            Material sparkMaterial = MaterialAsset("Sparks", "WeldingTrainer/Fusion Spark", Color.white);
            Material tipMaterial = MaterialAsset("Tool", "Universal Render Pipeline/Lit", new Color(0.12f, 0.15f, 0.18f));
            var root = new GameObject("Fusion Presentation");
            var demo = root.AddComponent<FusionWeldingDemo>();
            var piece = new GameObject("Workpiece — 1m angle").transform;
            piece.SetParent(root.transform, false);
            piece.localPosition = new Vector3(0, 0.95f, 0);
            piece.localRotation = Quaternion.Euler(0, 90, 0);
            // Inside faces x=0 and y=0; common root edge runs along local Z.
            Cube("Horizontal flange — 1000 x 100 x 6 mm", piece,
                new Vector3(0.044f, -0.003f, 0), new Vector3(0.1f, 0.006f, 1), steel);
            Cube("Vertical flange — 1000 x 100 x 6 mm", piece,
                new Vector3(-0.003f, 0.044f, 0), new Vector3(0.006f, 0.1f, 1), steel);
            var beadObject = new GameObject("Progressive fillet", typeof(MeshFilter), typeof(MeshRenderer));
            beadObject.transform.SetParent(piece, false);
            beadObject.GetComponent<MeshRenderer>().sharedMaterial = beadMaterial;
            demo.bead = beadObject.AddComponent<FusionBead>();
            var effects = new GameObject("Weld effects");
            effects.transform.SetParent(root.transform, false);
            demo.sparks = effects.AddComponent<FusionSparks>();
            demo.sparks.sparkMaterial = sparkMaterial;
            demo.workpiece = piece;
            var tool = new GameObject("Tool tip frame (+Z points at seam)").transform;
            tool.SetParent(root.transform, false);
            var nozzle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nozzle.name = "Nozzle";
            nozzle.transform.SetParent(tool, false);
            nozzle.transform.localPosition = new Vector3(0, 0, -0.035f);
            nozzle.transform.localRotation = Quaternion.Euler(90, 0, 0);
            nozzle.transform.localScale = new Vector3(0.012f, 0.035f, 0.012f);
            UnityEngine.Object.DestroyImmediate(nozzle.GetComponent<Collider>());
            nozzle.GetComponent<MeshRenderer>().sharedMaterial = tipMaterial;
            demo.toolVisual = tool;
            var hud = new GameObject("World status", typeof(TextMesh)).GetComponent<TextMesh>();
            hud.transform.SetParent(root.transform, false);
            hud.transform.localPosition = new Vector3(0, 1.22f, 0.03f);
            hud.transform.localRotation = Quaternion.identity;
            hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.GetComponent<MeshRenderer>().sharedMaterial = hud.font.material;
            hud.fontSize = 48;
            hud.characterSize = 0.006f;
            hud.anchor = TextAnchor.MiddleCenter;
            hud.alignment = TextAlignment.Center;
            hud.text = "FUSION / 1 METRE\nStart anywhere on the seam";
            demo.statusText = hud;
            return demo;
        }

        private static void Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material MaterialAsset(string name, string shaderName, Color color)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing shader " + shaderName);
            material = new Material(shader) { name = name, enableInstancing = true };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        [MenuItem("Tools/Welding Trainer/Fusion/Check coverage logic")]
        public static void CheckCoverage()
        {
            var coverage = new SeamCoverage(1, 500);
            Action<int, bool> noop = (index, fresh) => { };
            coverage.Deposit(0.5f, 0.5f, noop);
            Require(coverage.CoveredCount == 1 && !coverage[0] && !coverage[499], "Start in the middle");
            coverage.Deposit(0.5f, 0.75f, noop);
            int count = coverage.CoveredCount;
            coverage.Deposit(0.75f, 0.5f, noop);
            Require(coverage.CoveredCount == count, "Reverse/reheat does not duplicate coverage");
            coverage.Deposit(0.9f, 0.9f, noop);
            Require(!coverage[425], "Disconnected spots do not fill gaps");
            coverage.Deposit(0, 1, noop);
            Require(coverage.Fraction == 1, "Both endpoints included");
            coverage.Clear();
            Require(coverage.CoveredCount == 0 && !coverage[250], "Reset");
            Debug.Log("Fusion coverage checks passed (5).");
        }

        private static void Require(bool value, string label)
        { if (!value) throw new InvalidOperationException("Fusion check failed: " + label); }
    }
}
