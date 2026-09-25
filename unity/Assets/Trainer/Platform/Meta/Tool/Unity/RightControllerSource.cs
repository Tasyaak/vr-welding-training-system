using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using InputDevice = UnityEngine.InputSystem.InputDevice;

namespace WeldingTrainer.Platform.Meta.Tool.Unity
{
    // Static access to the runtime's actual profile, not an inference from enabled project features.
    public sealed class RightInteractionProfile : OpenXRFeature
    {
        public static string Read() => PathToString(GetCurrentInteractionProfile("/user/hand/right"));
    }

    [DisallowMultipleComponent]
    public sealed class RightControllerSource : MonoBehaviour
    {
        public Transform trackingSpace;
        public TextAsset calibrationJson;
        [Range(.01f, .25f)] public float maximumSampleAge = .1f;
        [Range(.01f, 1)] public float triggerPress = .65f;
        [Range(0, .99f)] public float triggerRelease = .45f;
        public ToolCalibration Calibration { get; private set; }
        public ToolInputBuffer Input { get; private set; }
        public ToolHeadSnapshot LastSnapshot { get; private set; }
        public Vector2 Navigation { get; private set; }
        public long OriginGeneration { get; private set; }
        public string SessionId { get; private set; }
        private static RightControllerSource owner;
        private InputActionMap map;
        private InputAction position, rotation, tracked, flags, trigger, secondary, primary, menu, navigation;
        private InputAction headPosition, headRotation, headTracked, headFlags;
        private readonly Dictionary<int, double> updates = new Dictionary<int, double>();
        private readonly Dictionary<InputControl, double> poseUpdates = new Dictionary<InputControl, double>();
        private readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        private bool focused, paused, healthy;
        private long sequence;
        private Vector3 originPosition, originScale;
        private Quaternion originRotation;
        private double inputEpoch;
        private double Now => Time.realtimeSinceStartupAsDouble;

        private InputAction Action(string name, string path) => map.AddAction(name, InputActionType.PassThrough, path);
        private void OnEnable()
        {
            if (owner && owner != this) { Debug.LogError("Only one RightControllerSource is allowed", this); enabled = false; return; }
            owner = this; SessionId = SessionId ?? Guid.NewGuid().ToString("N"); focused = Application.isFocused; paused = false;
            Input = Input ?? new ToolInputBuffer(triggerPress, triggerRelease);
            if (calibrationJson) Calibration = ToolCalibrationFile.Parse(calibrationJson.text).Freeze();
            map = new InputActionMap("RightPhysicalTool");
            const string right = "<XRController>{RightHand}/";
            position = Action("GripPosition", right + "devicePosition"); rotation = Action("GripRotation", right + "deviceRotation");
            tracked = Action("GripTracked", right + "isTracked"); flags = Action("GripValidity", right + "trackingState");
            trigger = Action("ProcessAnalog", right + "trigger"); secondary = Action("GlobalEStop", right + "secondaryButton");
            primary = Action("Submit", right + "primaryButton"); menu = Action("Menu", right + "thumbstickClicked"); navigation = Action("Navigation", right + "thumbstick");
            headPosition = Action("HeadPosition", "<XRHMD>/centerEyePosition"); headRotation = Action("HeadRotation", "<XRHMD>/centerEyeRotation");
            headTracked = Action("HeadTracked", "<XRHMD>/isTracked"); headFlags = Action("HeadValidity", "<XRHMD>/trackingState");
            trigger.performed += c => { if (Owns(c.control.device) && CurrentInputTime(c.time)) Input.ObserveTrigger(c.ReadValue<float>(), Now); };
            secondary.performed += Buttons; primary.performed += Buttons; menu.performed += Buttons;
            map.Enable();
            InputSystem.onEvent += OnEvent;
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onAfterUpdate += AfterInput;
            RememberOrigin(); RefreshSubsystems(); Discontinuity();
        }

        private static InputDevice Device(InputAction action) => action != null && action.enabled && action.controls.Count == 1 && action.controls[0].device.added && action.controls[0].device.enabled ? action.controls[0].device : null;
        // Read the uniquely resolved control even if its action has not performed (e.g. an
        // unchanged initial pose component). Freshness and explicit tracking flags gate its use.
        private static T Read<T>(InputAction action) where T : struct => ((InputControl<T>)action.controls[0]).ReadValue();
        private bool Owns(InputDevice device) => device != null && device == Device(position);
        private bool SameDevice(InputDevice device, params InputAction[] actions)
        {
            if (device == null) return false;
            foreach (var action in actions) if (Device(action) != device) return false;
            return true;
        }
        private void Buttons(InputAction.CallbackContext c)
        {
            if (Owns(c.control.device) && SameDevice(c.control.device, secondary, primary, menu))
                Input.ObserveButtons(Read<float>(secondary) > .5f, CurrentInputTime(c.time) && Read<float>(primary) > .5f, CurrentInputTime(c.time) && Read<float>(menu) > .5f, Now);
        }
        // Input event time is used only to reject queued pre-lifecycle/stale events.
        // It is not exposed as the hardware pose timestamp.
        private bool CurrentInputTime(double time) => ToolMath.Finite(time) && time >= inputEpoch && InputState.currentTime - time <= maximumSampleAge;
        private void OnEvent(InputEventPtr e, InputDevice d)
        {
            if (e.type != StateEvent.Type && e.type != DeltaStateEvent.Type) return;
            if (!CurrentInputTime(e.time)) return;
            updates[d.deviceId] = Now;
            Witness<Vector3>(position, e, d); Witness<Quaternion>(rotation, e, d); Witness<float>(tracked, e, d); Witness<int>(flags, e, d);
            Witness<Vector3>(headPosition, e, d); Witness<Quaternion>(headRotation, e, d); Witness<float>(headTracked, e, d); Witness<int>(headFlags, e, d);
        }
        private void Witness<T>(InputAction action, InputEventPtr e, InputDevice device) where T : struct
        {
            if (Device(action) == device && action.controls[0] is InputControl<T> control && control.ReadValueFromEvent(e, out _)) poseUpdates[control] = Now;
        }
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected || change == InputDeviceChange.Disabled || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Added)
                Discontinuity(); // Conservative; a new device can also introduce ambiguous bindings.
        }
        private void RefreshSubsystems()
        {
            var current = new List<XRInputSubsystem>(); SubsystemManager.GetSubsystems(current);
            foreach (var subsystem in current)
                if (!subsystems.Contains(subsystem)) { subsystems.Add(subsystem); subsystem.trackingOriginUpdated += OriginUpdated; }
        }
        private void OriginUpdated(XRInputSubsystem subsystem) => Discontinuity();
        private void RememberOrigin()
        {
            if (!trackingSpace) return;
            originPosition = trackingSpace.position; originRotation = trackingSpace.rotation; originScale = trackingSpace.lossyScale;
        }
        public void NotifyOriginDiscontinuity() => Discontinuity(); // #58 shares this notification with registration.
        private void Discontinuity()
        {
            OriginGeneration++; inputEpoch = InputState.currentTime; updates.Clear(); poseUpdates.Clear(); healthy = false; Input?.Invalidate(Now); LastSnapshot = null;
        }
        public void SetCalibration(ToolCalibration calibration) { Calibration = calibration; Input?.Invalidate(Now); LastSnapshot = null; }
        public void SetContext(ToolInputContext context) => Input.SetContext(context, Now);
        private void OnApplicationFocus(bool value) { focused = value; Discontinuity(); }
        private void OnApplicationPause(bool value) { paused = value; Discontinuity(); }
        private void AfterInput()
        {
            // Capture is caller-owned; this callback only witnesses the latest processed update.
            if (!focused || paused) { updates.Clear(); poseUpdates.Clear(); }
        }
        private bool Fresh(InputDevice device) => device != null && updates.TryGetValue(device.deviceId, out var time) && Now >= time && Now - time <= maximumSampleAge;
        private bool PoseFresh(InputAction action) => Device(action) != null && poseUpdates.TryGetValue(action.controls[0], out var time) && Now >= time && Now - time <= maximumSampleAge;

        private MeasuredPose ReadPose(string frame, InputAction p, InputAction r, InputAction t, InputAction f, RigidPose worldFromTracking)
        {
            var device = Device(p);
            if (!SameDevice(device, r, t, f)) return MeasuredPose.Invalid(frame, PoseFailure.DeviceUnavailable);
            if (!PoseFresh(t) || !PoseFresh(f)) return MeasuredPose.Invalid(frame, PoseFailure.Stale);
            if (!(Read<float>(t) >= .5f)) return MeasuredPose.Invalid(frame, PoseFailure.NotTracked);
            var state = (InputTrackingState)Read<int>(f);
            Vec3? point = null; Quat? orientation = null;
            PoseFailure failure = PoseFailure.None;
            try
            {
                if ((state & InputTrackingState.Position) != 0 && PoseFresh(p)) point = worldFromTracking.TransformPoint(UnitySpatialPose.ToDomain(Read<Vector3>(p)));
                else failure |= PoseFailure.PositionMissing;
            }
            catch (SpatialContentException) { failure |= PoseFailure.PositionMissing; }
            try
            {
                if ((state & InputTrackingState.Rotation) != 0 && PoseFresh(r)) orientation = worldFromTracking.Rotation * UnitySpatialPose.ToDomain(Read<Quaternion>(r));
                else failure |= PoseFailure.RotationMissing;
            }
            catch (ArgumentException) { failure |= PoseFailure.RotationMissing; }
            return new MeasuredPose(frame, point, orientation, failure);
        }

        // One application capture consumes pending commands. LastSnapshot is safe for diagnostics.
        public ToolHeadSnapshot Capture()
        {
            if (!isActiveAndEnabled || map == null)
                return LastSnapshot = new ToolHeadSnapshot(SessionId, ++sequence, Now, OriginGeneration, false, focused, paused, null,
                    MeasuredPose.Invalid("Controller", PoseFailure.DeviceUnavailable), MeasuredPose.Invalid("Head", PoseFailure.DeviceUnavailable), Calibration, Input ?? new ToolInputBuffer());
            RefreshSubsystems();
            if (trackingSpace && (trackingSpace.position != originPosition || trackingSpace.rotation != originRotation || trackingSpace.lossyScale != originScale)) { RememberOrigin(); Discontinuity(); }
            var controller = MeasuredPose.Invalid("Controller", PoseFailure.Lifecycle);
            var head = MeasuredPose.Invalid("Head", PoseFailure.Lifecycle);
            var device = Device(position);
            bool available = SameDevice(device, rotation, tracked, flags, trigger, secondary, primary, menu, navigation);
            if (focused && !paused && trackingSpace)
                try
                {
                    var worldFromTracking = UnitySpatialPose.Read(trackingSpace, "Tracking");
                    controller = ReadPose("Controller", position, rotation, tracked, flags, worldFromTracking);
                    head = ReadPose("Head", headPosition, headRotation, headTracked, headFlags, worldFromTracking);
                }
                catch (ArgumentException) { }
                catch (SpatialContentException) { }
            bool valid = available && controller.Pose.HasValue && focused && !paused;
            if (valid != healthy) Input.Invalidate(Now);
            healthy = valid;
            // Also account for controls already held when this source was enabled. Callbacks
            // retain short pulses; this fresh-state read does not manufacture release edges.
            if (available && Fresh(device)) Input.ObserveButtons(Read<float>(secondary) > .5f, Read<float>(primary) > .5f, Read<float>(menu) > .5f, Now);
            // Input availability and pose validity are independent. Tracking loss must not
            // erase a fresh analog measurement, but recovery still requires trigger release.
            if (available && Fresh(device) && focused && !paused) Input.ObserveTrigger(Read<float>(trigger), Now);
            else if (Input.Analog.HasValue) Input.Invalidate(Now);
            Navigation = available && focused && !paused && Fresh(device) ? Read<Vector2>(navigation) : Vector2.zero;
            string profile = null;
            if (Application.platform == RuntimePlatform.Android && available) profile = RightInteractionProfile.Read();
            LastSnapshot = new ToolHeadSnapshot(SessionId, ++sequence, Now, OriginGeneration, available, focused, paused, profile, controller, head, Calibration, Input);
            return LastSnapshot;
        }

        private void OnDisable()
        {
            if (owner != this) return;
            Discontinuity(); InputSystem.onEvent -= OnEvent; InputSystem.onDeviceChange -= OnDeviceChange; InputSystem.onAfterUpdate -= AfterInput;
            foreach (var s in subsystems) s.trackingOriginUpdated -= OriginUpdated;
            subsystems.Clear(); map?.Dispose(); map = null; owner = null;
        }
    }
}
