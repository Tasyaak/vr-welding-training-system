using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using WeldingTrainer.Content.Spatial.Unity;

namespace WeldingTrainer.Content.Spatial.Editor
{
    [ScriptedImporter(1, "spatial")]
    public sealed class SpatialCatalogImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            try
            {
                var directory = Path.GetDirectoryName(context.assetPath);
                var json = File.ReadAllText(context.assetPath);
                var codec = new UnitySpatialJson();
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                var snapshot = SpatialCatalogSnapshot.Load(json, codec, name =>
                {
                    var path = Path.Combine(directory, name).Replace('\\', '/');
                    context.DependsOnSourceAsset(path);
                    return File.ReadAllBytes(path);
                }, source => File.ReadAllBytes(Path.Combine(root, source)));
                var catalog = codec.Read<CatalogData>(json);
                var names = snapshot.ArtifactJson.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                var artifacts = names.Select(x => new TextAsset(snapshot.ArtifactJson[x]) { name = x }).ToArray();
                for (int i = 0; i < names.Length; i++)
                    context.AddObjectToAsset(names[i], artifacts[i]);
                var meshes = new List<Mesh>();
                foreach (var import in catalog.imports)
                {
                    var data = codec.Read<VisualData>(snapshot.ArtifactJson[import.visualFile]);
                    var mesh = new Mesh
                    {
                        name = import.sourceId,
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                    };
                    mesh.vertices = data.vertices.Select(v => new Vector3((float)v.x, (float)v.y, (float)v.z)).ToArray();
                    // Preserve the authored numeric axes and outward winding. Unity's Vector3.Cross
                    // and mesh normal convention use the same component formula (tested against a built-in cube).
                    var indices = (int[])data.triangles.Clone();
                    var normals = new Vector3[data.vertices.Length];
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        var a = data.vertices[indices[i]];
                        var b = data.vertices[indices[i + 1]];
                        var c = data.vertices[indices[i + 2]];
                        var n = Vec3.Cross(b - a, c - a);
                        n = n * (1 / n.Length);
                        for (int j = 0; j < 3; j++)
                            normals[indices[i + j]] = new Vector3((float)n.x, (float)n.y, (float)n.z);
                    }

                    mesh.triangles = indices;
                    mesh.normals = normals;
                    mesh.RecalculateBounds();
                    meshes.Add(mesh);
                    context.AddObjectToAsset(import.sourceId + "-visual", mesh);
                }

                var asset = ScriptableObject.CreateInstance<SpatialCatalogAsset>();
                asset.name = catalog.id;
                asset.InitializeForImport(json, names, artifacts, meshes.ToArray());
                context.AddObjectToAsset("catalog", asset);
                context.SetMainObject(asset);
            }
            catch (Exception error)
            {
                context.LogImportError("Spatial content rejected: " + error.Message);
                throw;
            }
        }
    }

    public sealed class SpatialBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => ValidateAll();
        [MenuItem("Trainer/Spatial/Validate all source and baked content")]
        public static void ValidateAll()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var paths = Directory.GetFiles(Path.Combine(Application.dataPath, "Trainer/Content/Spatial"), "*.spatial", SearchOption.AllDirectories);
            if (paths.Length == 0)
                throw new BuildFailedException("Spatial catalog missing");
            foreach (var path in paths)
            {
                try
                {
                    var snapshot = SpatialCatalogSnapshot.Load(File.ReadAllText(path), new UnitySpatialJson(), name => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path), name)), source => File.ReadAllBytes(Path.Combine(root, source)));
                    foreach (var binding in snapshot.Bindings)
                        if (!binding.ReadyForScoredRegistration)
                            Debug.LogWarning(binding.Id + " preview only: " + string.Join("; ", binding.ScoredRegistrationBlockers));
                }
                catch (Exception error)
                {
                    throw new BuildFailedException(path + ": " + error.Message);
                }
            }

            Debug.Log("Spatial catalog/source validation passed; qualification status remains separate.");
        }
    }
}
