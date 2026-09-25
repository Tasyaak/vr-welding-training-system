using NUnit.Framework;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
using InputDevice = UnityEngine.InputSystem.InputDevice;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Platform.Meta.Tool.Unity;

namespace WeldingTrainer.Platform.Meta.Tool.Tests
{
    public sealed class ToolInputPlayModeTests : InputTestFixture
    {
        private static void Queue(InputDevice device, float trigger, float b, int flags, double? eventTime = null, bool isTracked = true)
        {
            using (StateEvent.From(device, out var e))
            {
                device.GetChildControl<Vector3Control>("devicePosition").WriteValueIntoEvent(new Vector3(.2f,.3f,.4f),e);
                device.GetChildControl<QuaternionControl>("deviceRotation").WriteValueIntoEvent(Quaternion.identity,e);
                device.GetChildControl<ButtonControl>("isTracked").WriteValueIntoEvent(isTracked ? 1f : 0f,e); device.GetChildControl<IntegerControl>("trackingState").WriteValueIntoEvent(flags,e);
                device.GetChildControl<AxisControl>("trigger").WriteValueIntoEvent(trigger,e); device.GetChildControl<ButtonControl>("secondaryButton").WriteValueIntoEvent(b,e);
                Assert.That(device.GetChildControl<AxisControl>("trigger").ReadValueFromEvent(e),Is.EqualTo(trigger).Within(.001),"queued analog");
                if (eventTime.HasValue) e.time = eventTime.Value;
                InputSystem.QueueEvent(e);
            }
        }
        [TestCase(true)]
        [TestCase(false)]
        public void RealOpenXrLayoutBindingsPreserveShortEdgesAndMissingRotation(bool touchPlus)
        {
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>("Haptic");
            InputSystem.RegisterLayout<UnityEngine.InputSystem.XR.PoseControl>("Pose");
            InputSystem.RegisterLayout<MetaQuestTouchPlusControllerProfile.QuestTouchPlusController>();
            InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>();
            InputDevice device=touchPlus ? InputSystem.AddDevice<MetaQuestTouchPlusControllerProfile.QuestTouchPlusController>() : (InputDevice)InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
            InputSystem.SetDeviceUsage(device,"RightHand");
            var go=new GameObject("source test"); go.SetActive(false);
            var source=go.AddComponent<RightControllerSource>(); source.trackingSpace=go.transform; source.maximumSampleAge=.25f;
            try
            {
                go.SetActive(true); if (source.Input == null) go.SendMessage("OnEnable"); go.SendMessage("OnApplicationFocus",true);
                source.SetCalibration(new ToolCalibration("unit","r",null,null,false,ToolMath.Pose("Controller","Tool",new Vec3(.1,0,0),new Quat(0,0,0,1)),ToolMath.Pose("Tool","Tip",new Vec3(0,0,-.04),new Quat(0,0,0,1))));
                Queue(device,.9f,0,3); Queue(device,0,0,3); Queue(device,0,1,3); Queue(device,0,0,3);
                InputSystem.Update();
                var s=source.Capture(); Assert.That(s.Controller.Pose.HasValue,Is.True,$"device={s.DeviceValid} failure={s.Controller.Failure} focus={s.Focused} sourceEnabled={source.isActiveAndEnabled}"); Assert.That(s.Tip.Pose.HasValue,Is.True);
                Assert.That(s.Commands.Any(x=>x.Type==ToolCommandType.TriggerPressed),Is.True,string.Join(",",s.Commands.Select(x=>x.Type.ToString())));
                Assert.That(s.Commands,Has.Some.Matches<ToolCommand>(x=>x.Type==ToolCommandType.TriggerReleased));
                Assert.That(s.Commands,Has.Some.Matches<ToolCommand>(x=>x.Type==ToolCommandType.EStopPressed));
                Queue(device,0,0,(int)InputTrackingState.Position); InputSystem.Update();
                s=source.Capture(); Assert.That(s.Controller.PositionValid,Is.True); Assert.That(s.Tool.PositionValid,Is.False); Assert.That(s.Tip.Pose.HasValue,Is.False);
                Assert.That(s.TriggerAnalog,Is.EqualTo(0),"fresh analog input is independent of missing rotation");
                Queue(device,0,0,(int)InputTrackingState.Rotation); InputSystem.Update();
                s=source.Capture(); Assert.That(s.Controller.PositionValid,Is.False); Assert.That(s.Tool.RotationValid,Is.True); Assert.That(s.Tip.Pose.HasValue,Is.False);

                Queue(device,0,0,3); InputSystem.Update(); Assert.That(source.Capture().Tip.Pose.HasValue,Is.True);
                source.SetContext(ToolInputContext.Training);
                Queue(device,0,0,3); InputSystem.Update(); source.Capture();
                Queue(device,1,0,3); InputSystem.Update(); source.Capture(); Assert.That(source.Input.ProcessRequested,Is.True);
                go.SendMessage("OnApplicationFocus",false); Assert.That(source.Capture().Tip.Pose.HasValue,Is.False);
                go.SendMessage("OnApplicationFocus",true); Assert.That(source.Capture().Tip.Pose.HasValue,Is.False);
                Queue(device,1,0,3); InputSystem.Update(); source.Capture(); Assert.That(source.Input.ProcessRequested,Is.False,"held trigger after focus recovery");
                go.SendMessage("OnApplicationPause",true); Assert.That(source.Capture().Tip.Pose.HasValue,Is.False);
                go.SendMessage("OnApplicationPause",false); Assert.That(source.Capture().Tip.Pose.HasValue,Is.False);

                currentTime += 1;
                var previousOrigin=source.OriginGeneration; source.NotifyOriginDiscontinuity();
                Assert.That(source.OriginGeneration,Is.GreaterThan(previousOrigin));
                Queue(device,0,0,3,InputState.currentTime-.5); InputSystem.Update();
                Assert.That(source.Capture().Tip.Pose.HasValue,Is.False,"queued pre-recenter state cannot restore tracking");
                Queue(device,0,0,3); InputSystem.Update(); Assert.That(source.Capture().Tip.Pose.HasValue,Is.True);
                go.transform.position=Vector3.right;
                Assert.That(source.Capture().Tip.Pose.HasValue,Is.False,"tracking origin movement invalidates previous sample");
                Queue(device,0,0,3); InputSystem.Update();
                Assert.That(source.Capture().Controller.Position.Value.x,Is.EqualTo(1.2).Within(1e-6));

                Queue(device,0,0,3,null,false); InputSystem.Update(); Assert.That(source.Capture().Tip.Pose.HasValue,Is.False,"isTracked=false with cached components");
                Queue(device,0,0,3); InputSystem.Update(); source.Capture();
                source.maximumSampleAge=.01f;
                System.Threading.Thread.Sleep(20);
                InputSystem.QueueDeltaStateEvent(device.GetChildControl<AxisControl>("trigger"),.9f); InputSystem.Update();
                Assert.That(source.Capture().Tip.Pose.HasValue,Is.False,"button-only event cannot refresh an old pose");
                source.maximumSampleAge=.25f;
                Queue(device,0,1,3); InputSystem.Update(); source.enabled=false;
                s=source.Capture(); Assert.That(s.Tip.Pose.HasValue,Is.False);
                Assert.That(s.Commands,Has.Some.Matches<ToolCommand>(x=>x.Type==ToolCommandType.EStopPressed),"disable retains pending B");
                InputSystem.RemoveDevice(device); Assert.That(source.Capture().Tip.Pose.HasValue,Is.False);
            }
            finally { go.SendMessage("OnDisable"); Object.DestroyImmediate(go); if(device.added) InputSystem.RemoveDevice(device); }
        }
    }
}
