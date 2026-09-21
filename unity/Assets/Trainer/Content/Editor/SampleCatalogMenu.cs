using UnityEditor;
using UnityEngine;

namespace WeldingTrainer.Content.Editor
{
    public static class SampleCatalogMenu
    {
        private const string AssetPath = "Assets/Trainer/Content/Workpieces/SampleQuestMvpCatalog.asset";

        [MenuItem("Tools/Welding Trainer/Create or Replace Sample Quest MVP Catalog")]
        public static void Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<QuestMvpCatalogAsset>(AssetPath);
            if (existing != null) AssetDatabase.DeleteAsset(AssetPath);
            var asset = SampleQuestMvpContent.CreateInMemory();
            asset.name = "SampleQuestMvpCatalog";
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            Debug.Log($"Created validated sample catalog at {AssetPath}; hash {asset.FreezeForAttempt().ContentHashSha256}");
        }

        [MenuItem("Tools/Welding Trainer/Validate Selected Quest MVP Catalog")]
        public static void ValidateSelected()
        {
            if (Selection.activeObject is not QuestMvpCatalogAsset asset)
            {
                Debug.LogError("Select a QuestMvpCatalogAsset first.");
                return;
            }
            var snapshot = asset.FreezeForAttempt();
            Debug.Log($"Catalog valid. Content hash: {snapshot.ContentHashSha256}; evaluation hash: {snapshot.EvaluationHashSha256}");
        }
    }
}
