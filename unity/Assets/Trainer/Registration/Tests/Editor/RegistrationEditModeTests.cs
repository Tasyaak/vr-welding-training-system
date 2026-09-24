using System.IO;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Registration.Meta;
using WeldingTrainer.Registration.Preview;

namespace WeldingTrainer.Registration.Tests
{
    public sealed class RegistrationEditModeTests
    {
        [TestCase(0)]
        [TestCase(90)]
        [TestCase(180)]
        [TestCase(270)]
        public void NativeMarkerBasisAndCornerOrderSurviveMrukAndUnityConversions(float angle)
        {
            var go = new GameObject("MRUK plane");
            try
            {
                var native = Quaternion.Euler(23, 37, angle);
                var sz = Matrix4x4.Scale(new Vector3(1, 1, -1));
                var sx = Matrix4x4.Scale(new Vector3(-1, 1, 1));
                // Independent matrix form of installed SDK FlipZRotateY180 + SetPlane FlipX.
                var sdk = sz * Matrix4x4.Rotate(native) * sx;
                Assert.That(sdk.determinant, Is.EqualTo(1).Within(1e-5));
                go.transform.SetPositionAndRotation(new Vector3(1, 2, -3), sdk.rotation);
                var marker = RegistrationMath.CenterMrukPlane(UnityRegistrationPose.Read(go.transform, "MrukPlane"), -.1, .2);
                var expected = new Vector3(1, 2, 3) + native * new Vector3(.1f, .2f, 0);
                Assert.That((marker.Position - new Vec3(expected.x, expected.y, expected.z)).Length, Is.LessThan(1e-6));
                foreach (var corner in new[]
                {
                    new Vector3(-.03f, -.02f, 0),
                    new Vector3(.03f, -.02f, 0),
                    new Vector3(.03f, .02f, 0),
                    new Vector3(-.03f, .02f, 0),
                    Vector3.forward
                }

                )
                {
                    var actual = marker.TransformPoint(new Vec3(corner.x, corner.y, corner.z));
                    var point = expected + native * corner;
                    Assert.That((actual - new Vec3(point.x, point.y, point.z)).Length, Is.LessThan(1e-6));
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RuntimePresentationPreservesAsymmetricCadPlacementAndNormals()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SpatialCatalogAsset>("Assets/Trainer/Content/Spatial/Generated/supplied.spatial");
            var binding = asset.Freeze().ResolveForPreview("PART-001");
            var cameraObject = new GameObject("Physical top view");
            var root = new GameObject("Unity fixture");
            try
            {
                var fixture = RegistrationMath.Pose("World", "Fixture", new Vec3(1, 2, 3), new Quat(0, 0, System.Math.Sin(.3), System.Math.Cos(.3)));
                UnityRegistrationPose.Write(root.transform, fixture);
                var restored = UnityRegistrationPose.Read(root.transform, "Fixture");
                Assert.That((restored.Position - fixture.Position).Length, Is.LessThan(1e-6));
                var camera = cameraObject.AddComponent<Camera>();
                camera.aspect = 1.4f;
                camera.fieldOfView = 45;
                camera.transform.position = UnityRegistrationPose.ToUnity(fixture.TransformPoint(new Vec3(0, .6, 0)));
                var target = UnityRegistrationPose.ToUnity(fixture.TransformPoint(new Vec3(0, .02, 0)));
                camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, UnityRegistrationPose.ToUnity(fixture.TransformDirection(new Vec3(0, 0, -1))));
                System.Func<Vec3, Vector3> project = p => camera.WorldToViewportPoint(root.transform.TransformPoint(UnityRegistrationPose.ToUnity(p)));
                var recess = project(binding.FixtureFromMarker.Position);
                Assert.Less(recess.x, .5f);
                Assert.Less(recess.y, .5f);
                Assert.Greater(project(new Vec3(0, .0155, -.060)).y, .5f);
                var seam = binding.WorkpieceGeometry.Seams[0];
                Assert.Greater(project(seam.Points[seam.Points.Count - 1]).x, project(seam.Points[0]).x);
                foreach (var original in asset.CopyVisualMeshes())
                {
                    var before = original.vertices;
                    var view = UnityRegistrationMesh.Create(original);
                    try
                    {
                        var vertices = view.vertices;
                        var normals = view.normals;
                        var indices = view.triangles;
                        for (int i = 0; i < vertices.Length; i++)
                            Assert.That(Vector3.Distance(vertices[i], new Vector3(before[i].x, before[i].y, -before[i].z)), Is.LessThan(1e-7));
                        for (int i = 0; i < indices.Length; i += 3)
                        {
                            var normal = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]);
                            Assert.Greater(Vector3.Dot(normal / normal.magnitude, normals[indices[i]]), .999f);
                        }

                        CollectionAssert.AreEqual(before, original.vertices);
                    }
                    finally
                    {
                        Object.DestroyImmediate(view);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void SdkBoundaryRejectsMaterialQuaternionAndScaleErrors()
        {
            Assert.Throws<System.ArgumentException>(() => UnityRegistrationPose.ToDomain(new Quaternion(0, 0, 0, 2)));
            Assert.Throws<System.ArgumentException>(() => UnityRegistrationPose.ToDomain(new Quaternion(float.NaN, 0, 0, 1)));
            var go = new GameObject("invalid scale");
            try
            {
                go.transform.localScale = new Vector3(1, 2, 1);
                Assert.Throws<System.ArgumentException>(() => UnityRegistrationPose.Read(go.transform, "Anchor"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void StandaloneSceneHasCompleteReferencesAndNoApprovedDummyProfile()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Trainer/Registration/Preview/RegistrationPreview.unity", OpenSceneMode.Additive);
            try
            {
                RegistrationPreview preview = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        Assert.Zero(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject));
                    if (root.TryGetComponent<RegistrationPreview>(out var found))
                        preview = found;
                }

                Assert.IsNotNull(preview);
                Assert.IsNotNull(preview.catalog);
                Assert.IsNotNull(preview.rig);
                Assert.IsNotNull(preview.ghostMaterial);
                Assert.IsNotNull(preview.statusText.font);
                Assert.IsFalse(preview.mruk.EnableWorldLock);
                Assert.IsFalse(preview.mruk.SceneSettings.LoadSceneOnStartup);
                Assert.IsFalse(preview.adapterQualification.IsQualified("test"));
                Assert.IsTrue(preview.rightControllerSmokeCommands);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SharedWorkflowSuiteWithUnityCodec()
        {
            var suite = new RegistrationTestCases(Path.GetFullPath(Path.Combine(Application.dataPath, "../..")), new UnitySpatialJson(), x => JsonUtility.ToJson(x));
            foreach (var test in suite.Cases())
                Assert.DoesNotThrow(() => test.Value(), test.Key);
        }

        [Test]
        public void NativeStringTerminatorIsTransportOnly()
        {
            var raw = Encoding.UTF8.GetBytes("LW1:PART-001\0");
            Assert.IsTrue(QrPayload.TryParse(MetaQrTracker.TransportPayload(raw, true), out _));
            Assert.IsFalse(QrPayload.TryParse(MetaQrTracker.TransportPayload(raw, false), out _));
            Assert.IsFalse(QrPayload.TryParse(MetaQrTracker.TransportPayload(Encoding.UTF8.GetBytes("LW1:PART-001\0\0"), true), out _));
        }

        [Test]
        public void IdenticalSdkPlaneUpdatesAreFreshButRepeatedPollingIsNot()
        {
            var boundary = new List<Vector2>
            {
                Vector2.zero,
                Vector2.one
            };
            var witness = new MrukPlaneUpdateWitness(boundary);
            for (int i = 0; i < 10; i++)
                Assert.IsFalse(witness.Consume(boundary));
            boundary.Clear();
            boundary.Add(Vector2.zero);
            boundary.Add(Vector2.one);
            Assert.IsTrue(witness.Consume(boundary));
            Assert.IsFalse(witness.Consume(boundary));
            boundary.Clear();
            Assert.IsTrue(witness.Consume(boundary));
            boundary.Clear();
            Assert.IsTrue(witness.Consume(boundary));
            Assert.IsTrue(witness.Consume(new List<Vector2>()));
            Assert.IsFalse(witness.Consume(null));
        }

        [Test]
        public void UnknownOrDifferentRuntimeTupleCannotQualify()
        {
            var profile = ScriptableObject.CreateInstance<QrAdapterQualification>();
            try
            {
                Assert.IsFalse(profile.IsQualified("test"));
                profile.runtimeTuple = "test";
                profile.frameEvidence = "synthetic";
                profile.dimensionEvidence = "synthetic";
                profile.dimensionConvention = "ExcludesQuietZone";
                Assert.IsTrue(profile.IsQualified("test"));
                Assert.IsFalse(profile.IsQualified("other OS"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
