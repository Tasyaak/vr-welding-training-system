using UnityEngine;
using UnityEngine.XR;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using Keyboard = UnityEngine.InputSystem.Keyboard;
#endif

namespace WeldingTrainer.Prototype
{
    /// <summary>
    /// Demo-only vertical slice for early scene/XR testing.
    /// It intentionally uses one straight seam and no QR, ESP32, Spatial Anchor,
    /// persistence, Hall sensor, or production session architecture.
    ///
    /// Add this component explicitly to a scene GameObject.
    /// Assign Tracking Origin to OVRCameraRig/TrackingSpace in the Inspector.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PrototypeWeldingDemo : MonoBehaviour
    {
        private const float SeamLengthMetres = 0.5f;
        private const float GoodDistanceMetres = 0.025f;
        private const float MaximumWeldDistanceMetres = 0.05f;
        private const float MinimumGoodSpeed = 0.05f;
        private const float MaximumGoodSpeed = 0.15f;
        private const float CompletionThreshold = 0.97f;
        private const float PositionSmoothing = 16f;
        private const float SpeedSmoothing = 10f;

        private static readonly Color GoodColour = new Color(0.1f, 1f, 0.25f, 1f);
        private static readonly Color WarningColour = new Color(1f, 0.75f, 0.05f, 1f);
        private static readonly Color BadColour = new Color(1f, 0.12f, 0.08f, 1f);
        private static readonly Color GuideColour = new Color(0.1f, 0.75f, 1f, 1f);

        [Header("Scene references")]
        [SerializeField] private Transform trackingOrigin;
        [SerializeField] private Camera xrCamera;

        private InputDevice _rightController;
        private Transform _toolTip;
        private LineRenderer _guide;
        private LineRenderer _bead;
        private TextMesh _hud;

        private Vector3 _seamStart;
        private Vector3 _seamEnd;
        private Vector3 _smoothedToolPosition;
        private Vector3 _previousToolPosition;
        private Vector3 _editorToolPosition;
        private float _smoothedSpeed;
        private float _beadProgress;
        private float _errorSum;
        private float _speedSum;
        private int _sampleCount;
        private int _goodSampleCount;
        private float _nextHapticTime;
        private bool _hasPreviousPosition;
        private bool _seamPlaced;
        private bool _completed;

        private void Awake()
        {
            if (xrCamera == null)
            {
                xrCamera = Camera.main;
            }

            // For the standard OVRCameraRig hierarchy, CenterEyeAnchor is a direct
            // child of TrackingSpace. This is only a convenience fallback.
            // Prefer assigning TrackingSpace explicitly in the Inspector.
            if (trackingOrigin == null && xrCamera != null)
            {
                trackingOrigin = xrCamera.transform.parent;
            }
        }

        private void Start()
        {
            CreateToolTip();
            CreateGuide();
            CreateBead();
            CreateHud();

            if (trackingOrigin == null)
            {
                Debug.LogWarning(
                    "PrototypeWeldingDemo: Tracking Origin is not assigned. " +
                    "Assign OVRCameraRig/TrackingSpace in the Inspector for XR controller poses.",
                    this);
            }
        }

        private void Update()
        {
            if (!EnsureSeamPlaced())
            {
                SetHud("Waiting for the XR camera...");
                return;
            }

            bool resetPressed;
            bool triggerPressed;
            bool trackingValid;
            Vector3 rawToolPosition;
            Quaternion rawToolRotation;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            EnsureDevice(ref _rightController, XRNode.RightHand);
            if (!_rightController.isValid && Keyboard.current != null)
            {
                ReadEditorInput(
                    out trackingValid,
                    out rawToolPosition,
                    out rawToolRotation,
                    out triggerPressed,
                    out resetPressed);
            }
            else
#endif
            {
                ReadXrInput(
                    out trackingValid,
                    out rawToolPosition,
                    out rawToolRotation,
                    out triggerPressed,
                    out resetPressed);
            }

            if (resetPressed)
            {
                ResetAttempt();
            }

            if (!trackingValid)
            {
                _hasPreviousPosition = false;
                SetToolColour(BadColour);
                SetHud("Controller tracking unavailable\nMove or wake the right controller");
                return;
            }

            float positionBlend = 1f - Mathf.Exp(-PositionSmoothing * Time.unscaledDeltaTime);
            _smoothedToolPosition = _hasPreviousPosition
                ? Vector3.Lerp(_smoothedToolPosition, rawToolPosition, positionBlend)
                : rawToolPosition;

            _toolTip.SetPositionAndRotation(_smoothedToolPosition, rawToolRotation);
            UpdateEvaluation(triggerPressed);
        }

        private void LateUpdate()
        {
            if (xrCamera == null || _hud == null)
            {
                return;
            }

            Transform cameraTransform = xrCamera.transform;
            _hud.transform.position =
                cameraTransform.position +
                cameraTransform.forward * 0.65f +
                cameraTransform.up * 0.22f;

            _hud.transform.rotation = Quaternion.LookRotation(
                _hud.transform.position - cameraTransform.position,
                cameraTransform.up);
        }

        private void UpdateEvaluation(bool triggerPressed)
        {
            Vector3 seam = _seamEnd - _seamStart;
            float seamLengthSquared = seam.sqrMagnitude;

            float progress = seamLengthSquared > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(_smoothedToolPosition - _seamStart, seam) / seamLengthSquared)
                : 0f;

            Vector3 closestPoint = Vector3.Lerp(_seamStart, _seamEnd, progress);
            float distance = Vector3.Distance(_smoothedToolPosition, closestPoint);

            float instantaneousSpeed = 0f;
            if (_hasPreviousPosition && Time.unscaledDeltaTime > Mathf.Epsilon)
            {
                instantaneousSpeed =
                    Vector3.Distance(_smoothedToolPosition, _previousToolPosition) /
                    Time.unscaledDeltaTime;
            }

            float speedBlend = 1f - Mathf.Exp(-SpeedSmoothing * Time.unscaledDeltaTime);
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instantaneousSpeed, speedBlend);
            _previousToolPosition = _smoothedToolPosition;
            _hasPreviousPosition = true;

            bool positionGood = distance <= GoodDistanceMetres;
            bool positionWeldable = distance <= MaximumWeldDistanceMetres;
            bool speedGood =
                _smoothedSpeed >= MinimumGoodSpeed &&
                _smoothedSpeed <= MaximumGoodSpeed;

            bool welding = triggerPressed && positionWeldable && !_completed;

            Color stateColour = positionGood && speedGood
                ? GoodColour
                : positionWeldable
                    ? WarningColour
                    : BadColour;

            SetToolColour(stateColour);
            SetGuideColour(stateColour);

            if (welding)
            {
                _sampleCount++;
                _errorSum += distance;
                _speedSum += _smoothedSpeed;

                if (positionGood && speedGood)
                {
                    _goodSampleCount++;
                }

                if (progress >= _beadProgress - 0.02f)
                {
                    _beadProgress = Mathf.Max(_beadProgress, progress);
                    UpdateBead();
                }

                SendWarningHaptic(stateColour);
            }

            if (_beadProgress >= CompletionThreshold)
            {
                _completed = true;
                _beadProgress = 1f;
                UpdateBead();
            }

            SetHud(BuildStatusText(distance, progress, triggerPressed, positionWeldable));
        }

        private string BuildStatusText(
            float distance,
            float progress,
            bool triggerPressed,
            bool positionWeldable)
        {
            if (_completed)
            {
                float averageError = _sampleCount > 0 ? _errorSum / _sampleCount : 0f;
                float averageSpeed = _sampleCount > 0 ? _speedSum / _sampleCount : 0f;
                float quality = _sampleCount > 0
                    ? 100f * _goodSampleCount / _sampleCount
                    : 0f;

                return string.Format(
                    "WELD COMPLETE\n" +
                    "Completion: 100%\n" +
                    "Average error: {0:0.0} cm\n" +
                    "Average speed: {1:0.0} cm/s\n" +
                    "Good samples: {2:0}%\n" +
                    "Press B or R to reset",
                    averageError * 100f,
                    averageSpeed * 100f,
                    quality);
            }

            string positionLabel = distance <= GoodDistanceMetres
                ? "Position: GOOD"
                : positionWeldable
                    ? "Position: WARNING"
                    : "Position: TOO FAR";

            string speedLabel = _smoothedSpeed < MinimumGoodSpeed
                ? "Speed: TOO SLOW"
                : _smoothedSpeed > MaximumGoodSpeed
                    ? "Speed: TOO FAST"
                    : "Speed: GOOD";

            string activationLabel = triggerPressed
                ? positionWeldable
                    ? "Laser simulation: ACTIVE"
                    : "Laser simulation: INHIBITED"
                : "Hold trigger to weld";

            return string.Format(
                "{0}\n{1}\nError: {2:0.0} cm\nProgress: {3:0}%\n{4}",
                positionLabel,
                speedLabel,
                distance * 100f,
                Mathf.Max(progress, _beadProgress) * 100f,
                activationLabel);
        }

        private void ReadXrInput(
            out bool trackingValid,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out bool triggerPressed,
            out bool resetPressed)
        {
            EnsureDevice(ref _rightController, XRNode.RightHand);

            bool hasPosition = _rightController.TryGetFeatureValue(
                CommonUsages.devicePosition,
                out Vector3 localPosition);

            bool hasRotation = _rightController.TryGetFeatureValue(
                CommonUsages.deviceRotation,
                out Quaternion localRotation);

            trackingValid =
                _rightController.isValid &&
                hasPosition &&
                hasRotation &&
                trackingOrigin != null;

            if (_rightController.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked))
            {
                trackingValid &= isTracked;
            }

            if (trackingOrigin != null)
            {
                worldPosition = trackingOrigin.TransformPoint(localPosition);
                worldRotation = trackingOrigin.rotation * localRotation;
            }
            else
            {
                worldPosition = Vector3.zero;
                worldRotation = Quaternion.identity;
            }

            triggerPressed =
                _rightController.TryGetFeatureValue(
                    CommonUsages.triggerButton,
                    out bool triggerButton) &&
                triggerButton;

            if (!triggerPressed &&
                _rightController.TryGetFeatureValue(
                    CommonUsages.trigger,
                    out float triggerValue))
            {
                triggerPressed = triggerValue >= 0.5f;
            }

            resetPressed =
                _rightController.TryGetFeatureValue(
                    CommonUsages.secondaryButton,
                    out bool secondary) &&
                secondary;
        }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        private void ReadEditorInput(
            out bool trackingValid,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out bool triggerPressed,
            out bool resetPressed)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                trackingValid = false;
                worldPosition = Vector3.zero;
                worldRotation = Quaternion.identity;
                triggerPressed = false;
                resetPressed = false;
                return;
            }

            float movementSpeed = keyboard.leftShiftKey.isPressed ? 0.35f : 0.12f;
            Vector3 movement = Vector3.zero;

            movement += keyboard.lKey.isPressed ? Vector3.right : Vector3.zero;
            movement += keyboard.jKey.isPressed ? Vector3.left : Vector3.zero;
            movement += keyboard.iKey.isPressed ? Vector3.up : Vector3.zero;
            movement += keyboard.kKey.isPressed ? Vector3.down : Vector3.zero;

            if (xrCamera != null)
            {
                movement += keyboard.oKey.isPressed
                    ? xrCamera.transform.forward
                    : Vector3.zero;

                movement -= keyboard.uKey.isPressed
                    ? xrCamera.transform.forward
                    : Vector3.zero;
            }

            _editorToolPosition +=
                movement.normalized *
                movementSpeed *
                Time.unscaledDeltaTime;

            trackingValid = true;
            worldPosition = _editorToolPosition;
            worldRotation = Quaternion.identity;
            triggerPressed = keyboard.spaceKey.isPressed;
            resetPressed = keyboard.rKey.wasPressedThisFrame;
        }
#endif

        private bool EnsureSeamPlaced()
        {
            if (_seamPlaced)
            {
                return true;
            }

            if (xrCamera == null)
            {
                xrCamera = Camera.main;
            }

            if (xrCamera == null || Time.frameCount < 3)
            {
                return false;
            }

            Transform cameraTransform = xrCamera.transform;

            Vector3 centre =
                cameraTransform.position +
                cameraTransform.forward * 0.75f -
                cameraTransform.up * 0.25f;

            Vector3 direction =
                Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;

            if (direction.sqrMagnitude < 0.5f)
            {
                direction = Vector3.right;
            }

            _seamStart = centre - direction * (SeamLengthMetres * 0.5f);
            _seamEnd = centre + direction * (SeamLengthMetres * 0.5f);

            _guide.SetPosition(0, _seamStart);
            _guide.SetPosition(1, _seamEnd);

            _editorToolPosition = _seamStart + cameraTransform.up * 0.01f;
            _smoothedToolPosition = _editorToolPosition;

            _seamPlaced = true;
            UpdateBead();
            return true;
        }

        private static void EnsureDevice(ref InputDevice device, XRNode node)
        {
            if (!device.isValid)
            {
                device = InputDevices.GetDeviceAtXRNode(node);
            }
        }

        private void ResetAttempt()
        {
            _beadProgress = 0f;
            _errorSum = 0f;
            _speedSum = 0f;
            _sampleCount = 0;
            _goodSampleCount = 0;
            _smoothedSpeed = 0f;
            _hasPreviousPosition = false;
            _completed = false;
            UpdateBead();
        }

        private void SendWarningHaptic(Color stateColour)
        {
            if (!_rightController.isValid || Time.unscaledTime < _nextHapticTime)
            {
                return;
            }

            float amplitude =
                stateColour == BadColour
                    ? 0.65f
                    : stateColour == WarningColour
                        ? 0.25f
                        : 0f;

            if (amplitude <= 0f)
            {
                return;
            }

            _rightController.SendHapticImpulse(0u, amplitude, 0.04f);
            _nextHapticTime = Time.unscaledTime + 0.12f;
        }

        private void CreateToolTip()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Tracked Welding Tip";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * 0.028f;
            Destroy(sphere.GetComponent<Collider>());
            _toolTip = sphere.transform;
            SetToolColour(BadColour);
        }

        private void CreateGuide()
        {
            GameObject guideObject = new GameObject("Target Seam");
            guideObject.transform.SetParent(transform, false);
            _guide = guideObject.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_guide, 0.012f, GuideColour);
            _guide.positionCount = 2;
        }

        private void CreateBead()
        {
            GameObject beadObject = new GameObject("Progressive Weld Bead");
            beadObject.transform.SetParent(transform, false);
            _bead = beadObject.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_bead, 0.02f, GoodColour);
            _bead.positionCount = 2;
            _bead.enabled = false;
        }

        private void CreateHud()
        {
            GameObject hudObject = new GameObject("Prototype HUD");
            hudObject.transform.SetParent(transform, false);
            _hud = hudObject.AddComponent<TextMesh>();

            Font builtInFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtInFont != null)
            {
                _hud.font = builtInFont;
            }

            _hud.fontSize = 52;
            _hud.characterSize = 0.009f;
            _hud.anchor = TextAnchor.MiddleCenter;
            _hud.alignment = TextAlignment.Center;
            _hud.color = Color.white;
        }

        private static void ConfigureLineRenderer(
            LineRenderer line,
            float width,
            Color colour)
        {
            line.useWorldSpace = true;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 6;

            Material material = CreateMaterial(colour);
            if (material != null)
            {
                line.material = material;
            }

            line.startColor = colour;
            line.endColor = colour;
        }

        private static Material CreateMaterial(Color colour)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                Debug.LogError("PrototypeWeldingDemo: no compatible unlit shader was found.");
                return null;
            }

            Material material = new Material(shader);
            material.color = colour;
            return material;
        }

        private void SetToolColour(Color colour)
        {
            Renderer renderer =
                _toolTip != null
                    ? _toolTip.GetComponent<Renderer>()
                    : null;

            if (renderer != null)
            {
                renderer.material.color = colour;
            }
        }

        private void SetGuideColour(Color colour)
        {
            if (_guide == null)
            {
                return;
            }

            _guide.startColor = colour;
            _guide.endColor = colour;
        }

        private void UpdateBead()
        {
            if (_bead == null || !_seamPlaced || _beadProgress <= 0f)
            {
                if (_bead != null)
                {
                    _bead.enabled = false;
                }

                return;
            }

            _bead.enabled = true;
            _bead.SetPosition(0, _seamStart);
            _bead.SetPosition(
                1,
                Vector3.Lerp(_seamStart, _seamEnd, _beadProgress));
        }

        private void SetHud(string message)
        {
            if (_hud != null)
            {
                _hud.text = message;
            }
        }
    }
}