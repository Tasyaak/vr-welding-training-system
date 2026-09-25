using NUnit.Framework;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Platform.Meta.Tool.Unity;

namespace WeldingTrainer.Platform.Meta.Tool.Tests
{
    public sealed class ToolEditModeTests
    {
        [Test] public void ContractCases() { foreach(var test in ToolTestCases.Cases()) Assert.DoesNotThrow(()=>test.Value(),test.Key); }
        [Test] public void AsymmetricBasisConversionCommutesWithComposition()
        {
            var p = new Vector3(.31f,-.27f,.83f); var q = Quaternion.AngleAxis(67,new Vector3(1,2,3).normalized); var local = new Vector3(-.04f,.08f,.12f);
            var pose=ToolMath.Pose("World","Controller",UnitySpatialPose.ToDomain(p),UnitySpatialPose.ToDomain(q));
            var expected=UnitySpatialPose.ToDomain(p+q*local); var actual=pose.TransformPoint(UnitySpatialPose.ToDomain(local));
            Assert.That((actual-expected).Length,Is.LessThan(1e-6));
            Assert.That(pose.Position.z,Is.EqualTo(-.83).Within(1e-6)); Assert.That(pose.Rotation.x,Is.LessThan(0)); Assert.That(pose.Rotation.y,Is.LessThan(0)); Assert.That(pose.Rotation.z,Is.GreaterThan(0));
            Assert.That(Vector3.Distance(UnitySpatialPose.ToUnity(actual),p+q*local),Is.LessThan(1e-6));
        }
        [Test] public void CalibrationRoundTripPreservesBothTransformsAndRejectsExcessError()
        {
            var file = new ToolCalibrationFile { assemblyId="unit-1", revision="r1", controllerTipMetres=new Vec3(.1,.2,.3), controllerToolRotation=new Quat(0,0,System.Math.Sqrt(.5),System.Math.Sqrt(.5)) };
            var original=file.Freeze(); var copy=ToolCalibrationFile.Parse(JsonUtility.ToJson(file)).Freeze();
            Assert.That((original.ControllerFromTool.Position-copy.ControllerFromTool.Position).Length,Is.LessThan(1e-10));
            Assert.That((copy.ControllerFromTool*copy.ToolFromTip).Position.x,Is.EqualTo(.1).Within(1e-10));
            file.qualified=true; file.interactionProfile="test-profile"; file.qualifiedUtc="2026-09-24T00:00:00Z"; file.evidence="measured multi-pose";
            file.maxObservedTipErrorMm=2; file.maxObservedOrientationErrorDegrees=2;
            Assert.That(file.Freeze().Qualified,Is.True);
            file.maxObservedTipErrorMm=4; Assert.Throws<System.ArgumentException>(()=>file.Freeze());
            file.maxObservedTipErrorMm=0; Assert.Throws<System.ArgumentException>(()=>file.Freeze());
        }
        [Test] public void StandaloneSceneHasOneSourceAndCompleteQualificationReferences()
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Trainer/Platform/Meta/Tool/Preview/ToolQualification.unity",UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var components=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MonoBehaviour>(true)).Where(x=>x).ToArray();
                var sources=components.OfType<RightControllerSource>().ToArray();
                Assert.That(sources.Length,Is.EqualTo(1)); Assert.That(sources[0].trackingSpace,Is.Not.Null);
                Assert.That(sources[0].calibrationJson,Is.Null,"no invented approved calibration");
                var view=components.Single(x=>x.GetType().Name=="ToolQualificationView");
                var serialized=new UnityEditor.SerializedObject(view);
                foreach(var field in new[]{"source","status","lineMaterial"}) Assert.That(serialized.FindProperty(field).objectReferenceValue,Is.Not.Null,field);
                Assert.That(serialized.FindProperty("physicalAssemblyId").stringValue,Does.StartWith("glued-right-"));
                Assert.That(components.Any(x=>x.GetType().Name.Contains("Demo") || x.GetType().Name=="RegistrationPreview"),Is.False);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true); }
        }
    }
}
