using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WeldingTrainer.Content.Spatial.Unity;

namespace WeldingTrainer.Content.Spatial.Tests
{
    public sealed class SpatialEditModeTests
    {
        [Test]
        public void SharedSchemaAndActualSourceSuiteWithUnitySerializer()
        {
            var suite = new SpatialTestCases(Path.GetFullPath(Path.Combine(Application.dataPath, "../..")), new UnitySpatialJson(), value => JsonUtility.ToJson(value));
            foreach (var test in suite.Cases())
                Assert.DoesNotThrow(() => test.Value(), test.Key);
        }

        [Test]
        public void ImportCreatesMetreMeshesAndFrozenCatalogWithoutTrainingCode()
        {
            const string path = "Assets/Trainer/Content/Spatial/Generated/supplied.spatial";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<SpatialCatalogAsset>(path);
            Assert.That(asset, Is.Not.Null);
            var meshes = asset.CopyVisualMeshes();
            Assert.That(meshes.Length, Is.EqualTo(2));
            Assert.That(meshes[0].bounds.size.x, Is.EqualTo(.3).Within(1e-6));
            Assert.That(meshes[1].bounds.max.y, Is.EqualTo(.0655).Within(1e-6));
            Assert.That(asset.Freeze().ResolveForPreview("PART-001").ReadyForScoredRegistration, Is.False);
            foreach (var mesh in meshes)
            {
                var v = mesh.vertices;
                var n = mesh.normals;
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    // Vector3.normalized deliberately zeroes small vectors; small CAD triangles are valid.
                    Assert.That(cross.magnitude, Is.GreaterThan(1e-12f));
                    Assert.That(Vector3.Dot(cross / cross.magnitude, n[t[i]]), Is.GreaterThan(.999f), "Unity winding must match outward normal");
                }
            }
        }

        [Test]
        public void StructuralCrossProductMatchesUnityPrimitiveWindingAndRotation()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
                var v = mesh.vertices;
                var t = mesh.triangles;
                var n = mesh.normals;
                for (int i = 0; i < t.Length; i += 3)
                    Assert.That(Vector3.Dot(Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized, n[t[i]]), Is.GreaterThan(.999f));
                Assert.That(Vector3.Distance(Quaternion.AngleAxis(90, Vector3.forward) * Vector3.right, Vector3.up), Is.LessThan(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }
    }
}
