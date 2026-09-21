using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using Keyboard = UnityEngine.InputSystem.Keyboard;
#endif

namespace WeldingTrainer.FusionDemo
{
    [DefaultExecutionOrder(100)]
    public sealed class FusionWeldingDemo : MonoBehaviour
    {
        public enum ControlMode { Desktop, XR, ExternalTip }
        [Header("Required scene references")]
        public Transform workpiece;
        public FusionBead bead;
        public FusionSparks sparks;
        public Transform toolVisual;
        public TextMesh statusText;
        [Header("Input")]
        public ControlMode controlMode = ControlMode.Desktop;
        public Transform trackingOrigin;
        public Transform externalTip;
        public Vector3 controllerTipOffset = new Vector3(0, 0, 0.12f);
        [Tooltip("ExternalTip mode: drive these through your own input adapter.")]
        public bool externalTrigger;
        public bool externalTrackingValid;
        [Header("Presentation tolerances, metres")]
        [Range(0.003f, 0.05f)] public float maximumDistance = 0.025f;
        [Range(0.005f, 0.1f)] public float maximumBridgeDistance = 0.025f;
        [Range(0.02f, 0.25f)] public float maximumSampleGap = 0.1f;
        public bool showDesktopPanel = true;
        private InputDevice rightHand;
        private Vector3 desktopTip = new Vector3(0.007f, 0.007f, 0);
        private Vector3 previousLocalTip;
        private float previousMetres;
        private float previousTime;
        private bool previousWelding;
        private bool previousReset;
        private bool focused = true;
        private bool suspended;
        private bool rehearsing;
        private float rehearsalStart;
        private float nextHudUpdate;
        private string status = "Ready";

        private void Start()
        {
            if (workpiece == null || bead == null || sparks == null || toolVisual == null)
            { Debug.LogError("Fusion demo: assign Workpiece, Bead, Sparks and Tool Visual.", this); enabled = false; return; }
            if ((workpiece.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
            { Debug.LogError("Fusion demo: Workpiece and its parents must have unit scale (metres).", this); enabled = false; }
        }

        private void Update()
        {
            if (!focused || suspended) { BreakStroke(); return; }
            ReadInput(out Vector3 tipWorld, out Quaternion rotation, out bool trigger, out bool valid, out bool reset);
            if (reset && !previousReset) ResetAttempt();
            previousReset = reset;
            if (rehearsing) ReadRehearsal(out tipWorld, out rotation, out trigger, out valid);
            toolVisual.gameObject.SetActive(valid);
            if (!valid)
            {
                BreakStroke();
                UpdateHud("Tracking unavailable — welding stopped");
                return;
            }
            toolVisual.SetPositionAndRotation(tipWorld, rotation);
            Vector3 tipLocal = workpiece.InverseTransformPoint(tipWorld);
            float z = Mathf.Clamp(tipLocal.z, -0.5f, 0.5f);
            Vector3 closestLocal = new Vector3(0, 0, z);
            float distance = Vector3.Distance(tipLocal, closestLocal);
            // The inside quadrant prevents welding through the back of either flange.
            bool accessible = tipLocal.x >= -0.001f && tipLocal.y >= -0.001f;
            bool welding = trigger && accessible && distance <= maximumDistance;
            float metres = z + 0.5f;
            if (welding)
            {
                bool continuous = previousWelding &&
                    Time.time - previousTime <= maximumSampleGap &&
                    Vector3.Distance(tipLocal, previousLocalTip) <= maximumBridgeDistance;
                bead.Deposit(continuous ? previousMetres : metres, metres);
            }
            sparks.SetWelding(welding,
                workpiece.TransformPoint(closestLocal + new Vector3(0.0045f, 0.0045f, 0)),
                workpiece.TransformDirection(new Vector3(1, 1, 0).normalized));
            previousWelding = welding;
            previousMetres = metres;
            previousLocalTip = tipLocal;
            previousTime = Time.time;
            UpdateHud(welding ? "FUSION — welding" : trigger ? "Move tip into the inside corner" : "Ready — hold trigger / Space");
        }

        private void ReadInput(out Vector3 world, out Quaternion rotation, out bool trigger, out bool valid, out bool reset)
        {
            trigger = reset = false;
            valid = true;
            rotation = workpiece.rotation * Quaternion.LookRotation(new Vector3(-1, -1, 0));
            world = workpiece.TransformPoint(desktopTip);
            if (controlMode == ControlMode.ExternalTip)
            {
                valid = externalTrackingValid && externalTip != null;
                if (valid) { world = externalTip.position; rotation = externalTip.rotation; }
                trigger = externalTrigger;
                return;
            }
            if (controlMode == ControlMode.XR)
            {
                if (!rightHand.isValid) rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                bool hasPosition = rightHand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 trackingPosition);
                bool hasRotation = rightHand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion trackingRotation);
                bool tracked = rightHand.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
                valid = rightHand.isValid && hasPosition && hasRotation && tracked && trackingOrigin != null;
                if (valid)
                {
                    rotation = trackingOrigin.rotation * trackingRotation;
                    world = trackingOrigin.TransformPoint(trackingPosition + trackingRotation * controllerTipOffset);
                }
                trigger = rightHand.TryGetFeatureValue(CommonUsages.trigger, out float value) && value >= 0.5f;
                trigger |= rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool button) && button;
                reset = rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary) && secondary;
                return;
            }
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) { valid = false; return; }
            if (keyboard.f2Key.wasPressedThisFrame) ToggleRehearsal();
            reset = keyboard.rKey.wasPressedThisFrame;
            trigger = keyboard.spaceKey.isPressed;
            float speed = keyboard.leftShiftKey.isPressed ? 0.3f : 0.08f;
            Vector3 movement = new Vector3(
                (keyboard.oKey.isPressed ? 1 : 0) - (keyboard.uKey.isPressed ? 1 : 0),
                (keyboard.iKey.isPressed ? 1 : 0) - (keyboard.kKey.isPressed ? 1 : 0),
                (keyboard.lKey.isPressed ? 1 : 0) - (keyboard.jKey.isPressed ? 1 : 0));
            desktopTip += Vector3.ClampMagnitude(movement, 1) * speed * Time.deltaTime;
            desktopTip = Vector3.Max(new Vector3(-0.06f, -0.06f, -0.6f), Vector3.Min(desktopTip, new Vector3(0.2f, 0.2f, 0.6f)));
            world = workpiece.TransformPoint(desktopTip);
#else
            valid = false;
#endif
        }

        public void ToggleRehearsal()
        {
            if (controlMode != ControlMode.Desktop) return;
            bool start = !rehearsing;
            ResetAttempt();
            rehearsing = start;
            rehearsalStart = Time.time;
        }

        private void ReadRehearsal(out Vector3 world, out Quaternion rotation, out bool trigger, out bool valid)
        {
            float t = Time.time - rehearsalStart;
            // Centre -> positive end, lift, return to centre, centre -> negative end.
            float z = t < 6 ? Mathf.Lerp(0, 0.5f, t / 6) : t < 7 ? Mathf.Lerp(0.5f, 0, t - 6) : Mathf.Lerp(0, -0.5f, (t - 7) / 6);
            trigger = t <= 6 || (t >= 7 && t <= 13);
            Vector3 local = new Vector3(0.007f, 0.007f, z);
            if (!trigger) local += new Vector3(0.05f, 0.05f, 0);
            world = workpiece.TransformPoint(local);
            rotation = workpiece.rotation * Quaternion.LookRotation(new Vector3(-1, -1, 0));
            valid = true;
            if (t > 13) { rehearsing = false; desktopTip = local; }
        }

        public void ResetAttempt()
        {
            rehearsing = false;
            previousWelding = false;
            desktopTip = new Vector3(0.007f, 0.007f, 0);
            bead.ResetBead();
            sparks.Clear();
        }

        private void BreakStroke()
        {
            previousWelding = false;
            if (sparks != null && sparks.isActiveAndEnabled)
                sparks.SetWelding(false, sparks.transform.position, sparks.transform.forward);
        }

        private void UpdateHud(string message)
        {
            status = message;
            if (statusText == null || Time.unscaledTime < nextHudUpdate) return;
            nextHudUpdate = Time.unscaledTime + 0.1f;
            statusText.text = $"FUSION / 1 METRE\n{bead.Coverage.Fraction:P0} welded\n{message}";
        }

        private void OnGUI()
        {
            if (!showDesktopPanel || controlMode != ControlMode.Desktop || bead == null || bead.Coverage == null) return;
            GUILayout.BeginArea(new Rect(20, 20, 440, 155), GUI.skin.box);
            GUILayout.Label($"FUSION  |  {bead.Coverage.Fraction:P1} welded  |  {status}");
            GUILayout.Label("J/L: along seam   I/K: height   U/O: depth\nSpace: weld   Shift: fast   R: reset   F2: presentation");
            if (GUILayout.Button(rehearsing ? "Stop presentation" : "Present: start in the middle")) ToggleRehearsal();
            if (GUILayout.Button("Reset weld")) ResetAttempt();
            GUILayout.EndArea();
        }

        private void OnApplicationFocus(bool value) { focused = value; if (!value) BreakStroke(); }
        private void OnApplicationPause(bool value) { suspended = value; if (value) BreakStroke(); }
        private void OnDisable() { previousWelding = false; if (sparks != null) sparks.Clear(); }
    }
}
