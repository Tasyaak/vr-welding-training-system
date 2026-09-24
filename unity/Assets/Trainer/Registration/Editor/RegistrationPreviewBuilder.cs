using System;
using System.IO;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Registration.Meta;
using WeldingTrainer.Registration.Preview;

namespace WeldingTrainer.Registration.Editor
{
    public sealed class RegistrationPreviewBuilder : IPreprocessBuildWithReport
    {
        public const string ScenePath = "Assets/Trainer/Registration/Preview/RegistrationPreview.unity";
        private const string Folder = "Assets/Trainer/Registration/Preview/";
        public int callbackOrder => 0;

        [MenuItem("Welding Trainer/Registration/Open standalone preview")]
        public static void Open()
        {
            EditorSceneManager.OpenScene(ScenePath);
        }

        // Explicit authoring command. Does not change Bootstrap or the production build scene list.
        public static void Create()
        {
            ValidateConfiguration();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            var rigObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var rig = rigObject.GetComponent<OVRCameraRig>();
            var manager = rigObject.GetComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            foreach (var camera in rigObject.GetComponentsInChildren<Camera>(true))
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
            }

            new GameObject("Passthrough").AddComponent<OVRPassthroughLayer>();
            var mruk = new GameObject("Registration MRUK").AddComponent<MRUK>();
            mruk.EnableWorldLock = false;
            mruk.SceneSettings = new MRUK.MRUKSettings
            {
                LoadSceneOnStartup = false
            };
            var root = new GameObject("Standalone registration").AddComponent<RegistrationPreview>();
            root.rig = rig;
            root.mruk = mruk;
            root.catalog = AssetDatabase.LoadAssetAtPath<SpatialCatalogAsset>("Assets/Trainer/Content/Spatial/Generated/supplied.spatial");
            var profile = AssetDatabase.LoadAssetAtPath<QrAdapterQualification>(Folder + "UnqualifiedAdapter.asset");
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<QrAdapterQualification>();
                AssetDatabase.CreateAsset(profile, Folder + "UnqualifiedAdapter.asset");
            }

            root.adapterQualification = profile;
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Ghost.mat");
            if (!material)
            {
                material = new Material(Shader.Find("WeldingTrainer/RegistrationGhost"));
                AssetDatabase.CreateAsset(material, Folder + "Ghost.mat");
            }

            root.ghostMaterial = material;
            var text = new GameObject("Registration status").AddComponent<TextMesh>();
            text.transform.SetParent(rig.centerEyeAnchor, false);
            text.transform.localPosition = new Vector3(-.28f, .2f, .8f);
            text.characterSize = .008f;
            text.fontSize = 48;
            text.anchor = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.text = "Registration starts on device. Right A: confirm/retry; B: cancel.";
            root.statusText = text;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        public static void CreateAndBuildAndroid()
        {
            Create();
            BuildAndroid();
        }

        public static void BuildAndroid()
        {
            ValidateConfiguration();
            Directory.CreateDirectory("../artifacts");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, target = BuildTarget.Android, locationPathName = "../artifacts/registration-preview.apk", options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Standalone registration APK build failed: " + report.summary.result);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorBuildSettings.scenes.Any(x => x.enabled && x.path == ScenePath))
                ValidateConfiguration();
        }

        private static void ValidateConfiguration()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config.anchorSupport != OVRProjectConfig.AnchorSupport.Enabled || config.sceneSupport != OVRProjectConfig.FeatureSupport.Required)
                throw new BuildFailedException("Registration needs Meta Anchor Support Enabled and Scene Support Required.");
            var manifest = File.ReadAllText("Assets/Plugins/Android/AndroidManifest.xml");
            foreach (var permission in new[]
            {
                "com.oculus.permission.USE_SCENE",
                "com.oculus.permission.USE_ANCHOR_API"
            }

            )
                if (!manifest.Contains(permission))
                    throw new BuildFailedException("Registration manifest missing " + permission);
        }
    }
}
