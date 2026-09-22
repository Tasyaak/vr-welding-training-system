using System;
using UnityEngine;
using UnityEngine.InputSystem;
using WeldingTrainer.Application;
using WeldingTrainer.Content;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Platform.Meta
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class RightControllerInputAdapter : MonoBehaviour, IInputSnapshotSource, IInputContextControl
    {
        [SerializeField] private QuestMvpCatalogAsset _catalog;
        [SerializeField, Range(0f, 1f)] private float _triggerPressThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] private float _triggerReleaseThreshold = 0.45f;

        private InputAction _trigger, _estop, _confirm, _menu, _axis;
        private InputAction _controllerPosition, _controllerRotation, _controllerTracked, _controllerTrackingState;
        private InputAction _headPosition, _headRotation, _headTracked, _headTrackingState;
        private InputContextGate _gate;
        private long _originGeneration = 1;
        private ToolDefinition _tool;

        public InputContext Context => _gate?.Context ?? InputContext.Suspended;
        public Vector2 NavigationAxis => _axis?.ReadValue<Vector2>() ?? Vector2.zero;
        public string ActiveControllerLayout => _trigger?.activeControl?.device?.layout ?? "unavailable";

        private void Awake()
        {
            if (_triggerReleaseThreshold >= _triggerPressThreshold)
                throw new InvalidOperationException("Trigger release threshold must be below press threshold.");
            _gate = new InputContextGate(_triggerPressThreshold, _triggerReleaseThreshold);
            if (_catalog != null) _tool = _catalog.Bake().Tool;
            CreateActions();
        }

        private void OnEnable()
        {
            EnableAll(true); Application.focusChanged += OnFocusChanged;
        }

        private void OnDisable()
        {
            Application.focusChanged -= OnFocusChanged; EnableAll(false);
            _gate.Invalidate();
        }

        private void OnDestroy()
        {
            foreach (InputAction action in AllActions()) action?.Dispose();
        }

        private void Update()
        {
            float value = Mathf.Clamp01(_trigger.ReadValue<float>());
            _gate.AdvanceTrigger(value);
        }

        public InputSnapshot Capture(double monotonicSeconds)
        {
            InputCommandEdges edges = _gate.ConsumeEdges();
            TrackedPoseSnapshot controller = ReadPose(_controllerPosition, _controllerRotation,
                _controllerTracked, _controllerTrackingState);
            TrackedPoseSnapshot head = ReadPose(_headPosition, _headRotation, _headTracked, _headTrackingState);
            TrackedPoseSnapshot tool = TrackedPoseSnapshot.Invalid, tip = TrackedPoseSnapshot.Invalid;
            if (controller.IsValid && _tool != null)
            {
                RigidPose toolPose = RigidPoseMath.Compose(controller.Pose, _tool.ToolFromController);
                RigidPose tipPose = RigidPoseMath.Compose(toolPose, _tool.EffectiveTipFromTool);
                tool = new TrackedPoseSnapshot(toolPose, true, true, true);
                tip = new TrackedPoseSnapshot(tipPose, true, true, true);
            }
            bool processAllowed = Context == InputContext.Training && !_gate.RequiresRelease;
            bool pressed = _gate.ProcessPressed;
            float value = processAllowed ? Mathf.Clamp01(_trigger.ReadValue<float>()) : 0f;
            bool available = isActiveAndEnabled && Application.isFocused && _tool != null;
            return new InputSnapshot(available, head, controller, tool, tip, value, pressed,
                edges, Context, _originGeneration, monotonicSeconds);
        }

        public void SetContext(InputContext context)
        {
            _gate.SetContext(context);
        }

        public void NotifyTrackingOriginChanged()
        {
            _originGeneration++; _gate.Invalidate();
        }

        private void OnFocusChanged(bool focused)
        {
            if (!focused) _gate.Invalidate();
        }

        private void CreateActions()
        {
            _trigger = Action("ProcessTrigger", InputActionType.Value, "<XRController>{RightHand}/trigger");
            _estop = Action("EmergencyStop", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            _confirm = Action("Confirm", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            _menu = Action("Menu", InputActionType.Button, "<XRController>{RightHand}/primary2DAxisClick");
            _axis = Action("Navigate", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis");
            _controllerPosition = Action("ControllerPosition", InputActionType.Value, "<XRController>{RightHand}/devicePosition");
            _controllerRotation = Action("ControllerRotation", InputActionType.Value, "<XRController>{RightHand}/deviceRotation");
            _controllerTracked = Action("ControllerTracked", InputActionType.Button, "<XRController>{RightHand}/isTracked");
            _controllerTrackingState = Action("ControllerTrackingState", InputActionType.Value, "<XRController>{RightHand}/trackingState");
            _headPosition = Action("HeadPosition", InputActionType.Value, "<XRHMD>/centerEyePosition");
            _headRotation = Action("HeadRotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
            _headTracked = Action("HeadTracked", InputActionType.Button, "<XRHMD>/isTracked");
            _headTrackingState = Action("HeadTrackingState", InputActionType.Value, "<XRHMD>/trackingState");
            _estop.performed += _ => _gate.Queue(InputCommandEdges.EmergencyStop);
            _confirm.performed += _ => { if (Context == InputContext.Calibration) _gate.Queue(InputCommandEdges.Confirm); };
            _menu.performed += _ => { _gate.Queue(InputCommandEdges.MenuToggle); SetContext(Context == InputContext.Menu ? InputContext.Suspended : InputContext.Menu); };
        }

        private static InputAction Action(string name, InputActionType type, string binding)
        { var action = new InputAction(name, type); action.AddBinding(binding); return action; }

        private TrackedPoseSnapshot ReadPose(InputAction position, InputAction rotation,
            InputAction tracked, InputAction trackingState)
        {
            bool device = tracked.ReadValue<float>() > 0.5f && position.activeControl?.device != null;
            int state = trackingState.ReadValue<int>();
            bool positionValid = (state & 1) != 0, rotationValid = (state & 2) != 0;
            if (!device || !positionValid || !rotationValid) return TrackedPoseSnapshot.Invalid;
            return new TrackedPoseSnapshot(UnityDomainConversion.ToDomainPose(
                position.ReadValue<Vector3>(), rotation.ReadValue<Quaternion>()), true, true, true);
        }

        private InputAction[] AllActions() => new[] { _trigger, _estop, _confirm, _menu, _axis,
            _controllerPosition, _controllerRotation, _controllerTracked, _controllerTrackingState,
            _headPosition, _headRotation, _headTracked, _headTrackingState };
        private void EnableAll(bool enabled) { foreach (InputAction action in AllActions()) if (enabled) action.Enable(); else action.Disable(); }
    }
}
