using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeldingTrainer.Platform.Meta.Tool.Unity;
using WeldingTrainer.Presentation;

namespace WeldingTrainer.Integration.Editor
{
    public static class FusionMvpBuilder
    {
        public static void BuildAndroid()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Resources/DevAgentSettings.asset");
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("enabled").boolValue = false;
            serialized.FindProperty("accessToken").stringValue = "";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("../artifacts");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene }, target = BuildTarget.Android,
                locationPathName = "../artifacts/fusion-mvp.apk", options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception("Fusion APK build failed: " + report.summary.result);
            Debug.Log("FUSION_ANDROID_BUILD_PASS");
        }
        public const string Scene = "Assets/Trainer/Scenes/FusionMvp.unity";
        [MenuItem("Welding Trainer/Build Fusion MVP scene")]
        public static void Build()
        {
            EditorSceneManager.OpenScene("Assets/Trainer/Integration/RegistrationPreview.unity");
            foreach (var driver in Object.FindObjectsByType<RegistrationTrainingDemoDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(driver);
            foreach (var visuals in Object.FindObjectsByType<RegisteredAssemblyVisuals>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(visuals);
            foreach (var driver in Object.FindObjectsByType<RegisteredAssemblyPoseDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(driver);
            foreach (var text in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(text.gameObject);
            var bridge = Object.FindFirstObjectByType<QuestRegistrationBridge>();
            bridge.catalog = AssetDatabase.LoadAssetAtPath<WeldingTrainer.Content.Spatial.Unity.SpatialCatalogAsset>("Assets/Trainer/Content/Spatial/Generated/fusion-mvp.spatial");
            bridge.adapterQualification = AssetDatabase.LoadAssetAtPath<WeldingTrainer.Registration.Meta.QrAdapterQualification>("Assets/Trainer/Integration/Quest3QrB.asset");
            bridge.beginOnStart = false;
            bridge.diagnosticQrLogging = false;
            var root = new GameObject("Fusion MVP - production composition");
            root.SetActive(false);
            var right = root.AddComponent<RightControllerSource>();
            right.trackingSpace = bridge.rig.trackingSpace;
            right.calibrationJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Trainer/Content/Tool/Calibration/quest3-right-01-glued-tool-01.json");
            var view = root.AddComponent<FusionTrainingView>();
            Directory.CreateDirectory("Assets/Trainer/Presentation/Materials");
            view.metal = Material("Fusion metal", "WeldingTrainer/Fusion Metal", new Color(.3f, .34f, .38f));
            view.sparkMaterial = Material("Fusion sparks", "WeldingTrainer/Fusion Spark", Color.white);
            view.overlay = Material("Fusion overlay", "Universal Render Pipeline/Particles/Unlit", Color.white);
            var composition = root.AddComponent<FusionMvpComposition>();
            composition.registration = bridge;
            composition.right = right;
            composition.view = view;
            root.SetActive(true);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Scene);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Scene, true)
            };
            AssetDatabase.SaveAssets();
            Debug.Log("FUSION_MVP_SCENE_READY");
        }

        static Material Material(string name, string shader, Color color)
        {
            string path = "Assets/Trainer/Presentation/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = Shader.Find(shader);
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", color);
            return m;
        }
    }
}
