using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeldingTrainer.Platform.Meta.Tool.Unity;
using WeldingTrainer.Platform.Meta.Tool.Preview;

namespace WeldingTrainer.Platform.Meta.Tool.Editor
{
    public static class ToolQualificationBuilder
    {
        public const string ScenePath = "Assets/Trainer/Platform/Meta/Tool/Preview/ToolQualification.unity";
        [MenuItem("Welding Trainer/Tool/Create standalone qualification scene")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            var rigObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var rig = rigObject.GetComponent<OVRCameraRig>(); rigObject.GetComponent<OVRManager>().isInsightPassthroughEnabled = true;
            foreach (var camera in rigObject.GetComponentsInChildren<Camera>(true)) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; }
            new GameObject("Passthrough").AddComponent<OVRPassthroughLayer>();
            var source = new GameObject("Authoritative right tool source").AddComponent<RightControllerSource>(); source.trackingSpace = rig.trackingSpace;
            var view = new GameObject("Tool qualification only").AddComponent<ToolQualificationView>(); view.source = source;
            view.physicalAssemblyId = "glued-right-" + System.Guid.NewGuid().ToString("N").Substring(0, 12);
            var materialPath = "Assets/Trainer/Platform/Meta/Tool/Preview/Qualification.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(material, materialPath); }
            view.lineMaterial = material;
            var text = new GameObject("Tool status").AddComponent<TextMesh>(); text.transform.SetParent(rig.centerEyeAnchor, false);
            text.transform.localPosition = new Vector3(-.34f, .26f, .85f); text.characterSize = .0045f; text.fontSize = 48; text.anchor = TextAnchor.UpperLeft;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material; view.status = text;
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        }
        [MenuItem("Welding Trainer/Tool/Build standalone Quest qualification APK")]
        public static void Build()
        {
            if (!File.Exists(ScenePath)) throw new BuildFailedException("Create qualification scene and set physicalAssemblyId first");
            Directory.CreateDirectory("../artifacts");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, target = BuildTarget.Android, locationPathName = "../artifacts/tool-qualification.apk", options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Tool qualification build failed");
        }
    }
}
