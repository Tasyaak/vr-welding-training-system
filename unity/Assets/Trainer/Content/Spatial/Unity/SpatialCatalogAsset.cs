using System;
using System.Text;
using UnityEngine;

namespace WeldingTrainer.Content.Spatial.Unity
{
    public sealed class UnitySpatialJson : ISpatialJson
    {
        public T Read<T>(string json) => JsonUtility.FromJson<T>(json);
    }

    // Imported subassets contain only baked local metre content; no STEP or B dependencies.
    public sealed class SpatialCatalogAsset : ScriptableObject
    {
        [SerializeField, TextArea]
        private string catalogJson;
        [SerializeField]
        private string[] artifactNames;
        [SerializeField]
        private TextAsset[] artifacts;
        [SerializeField]
        private Mesh[] visualMeshes;
        public Mesh[] CopyVisualMeshes() => (Mesh[])visualMeshes.Clone();
        public SpatialCatalogSnapshot Freeze()
        {
            return SpatialCatalogSnapshot.Load(catalogJson, new UnitySpatialJson(), name =>
            {
                int index = Array.IndexOf(artifactNames, name);
                if (index < 0)
                    throw new SpatialContentException("Missing imported artifact: " + name);
                return artifacts[index].bytes;
            });
        }
#if UNITY_EDITOR
        public void InitializeForImport(string json,string[] names,TextAsset[] content,Mesh[] meshes)
        { catalogJson=json; artifactNames=(string[])names.Clone(); artifacts=(TextAsset[])content.Clone(); visualMeshes=(Mesh[])meshes.Clone(); }
#endif
    }
}
