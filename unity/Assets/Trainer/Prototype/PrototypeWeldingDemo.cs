using System;
using UnityEngine;
using UnityEngine.XR;

#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace WeldingTrainer.Prototype
{
    /// <summary>
    /// Demo-only vertical slice for the presentation prototype.
    /// It intentionally uses one straight seam and no QR, ESP32, or persistence.
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
        private const float StartProgressThreshold = 0.08f;
        private const float MaximumProgressJump = 0.08f;
        private const float ReverseProgressTolerance = 0.03f;
        private const float MinimumCalibrationLength = 0.15f;
        private const float MaximumCalibrationLength = 1.5f;
        private const float PositionSmoothing = 16f;
        private const float SpeedSmoothing = 10f;

        private static readonly Color GoodColour = new Color(0.1f, 1f, 0.25f, 1f);
        private static readonly Color WarningColour = new Color(1f, 0.75f, 0.05f, 1f);
        private static readonly Color BadColour = new Color(1f, 0.12f, 0.08f, 1f);
        private static readonly Color GuideColour = new Color(0.1f, 0.75f, 1f, 1f);

        private InputDevice _rightController;
        private InputDevice _leftController;
        private Transform _trackingOrigin;
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
        private float _recordingStartedAt;
        private float _nextRecordTime;
        private bool _hasPreviousPosition;
        private bool _seamPlaced;
        private bool _attemptStarted;
        private bool _completed;
        private bool _resetWasPressed;
        private bool _calibrateWasPressed;
        private bool _sessionSaved;
        private bool _isCalibrating;
        private Vector3 _calibrationStart;
        private string _calibrationMessage;
        private PrototypeSessionRecorder _recorder;

#if UNITY_EDITOR
        private bool _autoRehearsal;
        private float _autoRehearsalStartedAt;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (UnityEngine.Object.FindFirstObjectByType<PrototypeWeldingDemo>() != null)
            {
                return;
            }

            var root = new GameObject("Tomorrow Prototype Demo");
            root.AddComponent<PrototypeWeldingDemo>();
        }

        private void Start()
        {
            _recorder = new PrototypeSessionRecorder();
            CreateToolTip();
            CreateGuide();
            CreateBead();
            CreateHud();
            ResolveTrackingOrigin();
        }

        private void Update()
        {
            ResolveTrackingOrigin();
            if (!EnsureSeamPlaced())
            {
                SetHud("Waiting for the XR camera...");
                return;
            }

            bool resetPressed;
            bool calibratePressed;
            bool triggerPressed;
            bool trackingValid;
            Vector3 rawToolPosition;
            Quaternion rawToolRotation;

#if UNITY_EDITOR
            EnsureDevice(ref _rightController, XRNode.RightHand);
            if (!_rightController.isValid && Keyboard.current != null)
            {
                ReadEditorInput(
                    out trackingValid,
                    out rawToolPosition,
                    out rawToolRotation,
                    out triggerPressed,
                    out resetPressed,
                    out calibratePressed);
            }
            else
#endif
            {
                ReadXrInput(
                    out trackingValid,
                    out rawToolPosition,
                    out rawToolRotation,
                    out triggerPressed,
                    out resetPressed,
                    out calibratePressed);
            }

            if (resetPressed && !_resetWasPressed)
            {
                ResetAttempt();
            }
            _resetWasPressed = resetPressed;

            if (!trackingValid)
            {
                _hasPreviousPosition = false;
                SetToolColour(BadColour);
                SetHud("Controller tracking unavailable\nMove or wake the right controller");
                _calibrateWasPressed = calibratePressed;
                return;
            }

            float positionBlend = 1f - Mathf.Exp(-PositionSmoothing * Time.unscaledDeltaTime);
            _smoothedToolPosition = _hasPreviousPosition
                ? Vector3.Lerp(_smoothedToolPosition, rawToolPosition, positionBlend)
                : rawToolPosition;

            _toolTip.SetPositionAndRotation(_smoothedToolPosition, rawToolRotation);

            if (calibratePressed && !_calibrateWasPressed)
            {
                HandleCalibration(_smoothedToolPosition);
            }
            _calibrateWasPressed = calibratePressed;

            if (_isCalibrating)
            {
                _hasPreviousPosition = false;
                SetToolColour(GuideColour);
                SetGuideColour(GuideColour);
                SetHud(_calibrationMessage);
                return;
            }

            UpdateEvaluation(triggerPressed);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null || _hud == null)
            {
                return;
            }

            Transform cameraTransform = camera.transform;
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
                instantaneousSpeed = Vector3.Distance(_smoothedToolPosition, _previousToolPosition)
                    / Time.unscaledDeltaTime;
            }

            float speedBlend = 1f - Mathf.Exp(-SpeedSmoothing * Time.unscaledDeltaTime);
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, instantaneousSpeed, speedBlend);
            _previousToolPosition = _smoothedToolPosition;
            _hasPreviousPosition = true;

            bool positionGood = distance <= GoodDistanceMetres;
            bool positionWeldable = distance <= MaximumWeldDistanceMetres;
            bool speedGood = _smoothedSpeed >= MinimumGoodSpeed && _smoothedSpeed <= MaximumGoodSpeed;
            bool nearStart = progress <= StartProgressThreshold;

            if (!_attemptStarted && triggerPressed && positionWeldable && nearStart)
            {
                _attemptStarted = true;
            }

            bool progressContinuous =
                progress <= _beadProgress + MaximumProgressJump &&
                progress >= _beadProgress - ReverseProgressTolerance;
            bool activationAllowed =
                _attemptStarted &&
                positionWeldable &&
                progressContinuous &&
                !_completed;
            bool welding = triggerPressed && activationAllowed;

            if (_recorder != null &&
                _recorder.IsActive &&
                Time.realtimeSinceStartup >= _nextRecordTime)
            {
                _recorder.Record(
                    Time.realtimeSinceStartup - _recordingStartedAt,
                    _smoothedToolPosition,
                    progress,
                    distance,
                    _smoothedSpeed,
                    triggerPressed,
                    activationAllowed);
                _nextRecordTime = Time.realtimeSinceStartup + 0.1f;
            }

            Color stateColour;
            if (triggerPressed && !activationAllowed)
            {
                stateColour = BadColour;
            }
            else if (positionGood && speedGood)
            {
                stateColour = GoodColour;
            }
            else
            {
                stateColour = positionWeldable ? WarningColour : BadColour;
            }
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

            if (!_completed && _beadProgress >= CompletionThreshold)
            {
                _completed = true;
                _beadProgress = 1f;
                UpdateBead();
                FinishRecording(true, "completed");
            }

            SetHud(BuildStatusText(
                distance,
                progress,
                triggerPressed,
                positionWeldable,
                nearStart,
                progressContinuous));
        }

        private string BuildStatusText(
            float distance,
            float progress,
            bool triggerPressed,
            bool positionWeldable,
            bool nearStart,
            bool progressContinuous)
        {
            if (_completed)
            {
                float averageError = _sampleCount > 0 ? _errorSum / _sampleCount : 0f;
                float averageSpeed = _sampleCount > 0 ? _speedSum / _sampleCount : 0f;
                float quality = _sampleCount > 0 ? 100f * _goodSampleCount / _sampleCount : 0f;
                string saveStatus = _sessionSaved
                    ? "Results saved locally"
                    : "Result save failed - see Console";
                return string.Format(
                    "WELD COMPLETE\nCompletion: 100%\nAverage error: {0:0.0} cm\nAverage speed: {1:0.0} cm/s\nGood samples: {2:0}%\n{3}\nPress B or R to reset\nPress A or C to place a new seam",
                    averageError * 100f,
                    averageSpeed * 100f,
                    quality,
                    saveStatus);
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
            string activationLabel;
            if (!_attemptStarted)
            {
                activationLabel = nearStart
                    ? "At seam start - hold trigger to begin"
                    : "Move to the seam start";
            }
            else if (!positionWeldable)
            {
                activationLabel = "Laser simulation: INHIBITED - too far";
            }
            else if (!progressContinuous)
            {
                activationLabel = "Laser simulation: INHIBITED - return to bead edge";
            }
            else
            {
                activationLabel = triggerPressed
                    ? "Laser simulation: ACTIVE"
                    : "Hold trigger to weld";
            }

            return string.Format(
                "{0}\n{1}\nError: {2:0.0} cm\nProgress: {3:0}%\n{4}\nPress A or C to place seam",
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
            out bool resetPressed,
            out bool calibratePressed)
        {
            EnsureDevice(ref _rightController, XRNode.RightHand);
            EnsureDevice(ref _leftController, XRNode.LeftHand);

            bool hasPosition = _rightController.TryGetFeatureValue(
                CommonUsages.devicePosition,
                out Vector3 localPosition);
            bool hasRotation = _rightController.TryGetFeatureValue(
                CommonUsages.deviceRotation,
                out Quaternion localRotation);

            trackingValid = _rightController.isValid && hasPosition && hasRotation;
            if (_rightController.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked))
            {
                trackingValid &= isTracked;
            }

            if (_trackingOrigin != null)
            {
                worldPosition = _trackingOrigin.TransformPoint(localPosition);
                worldRotation = _trackingOrigin.rotation * localRotation;
            }
            else
            {
                worldPosition = localPosition;
                worldRotation = localRotation;
            }

            triggerPressed =
                _rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButton)
                    && triggerButton;
            if (!triggerPressed &&
                _rightController.TryGetFeatureValue(CommonUsages.trigger, out float triggerValue))
            {
                triggerPressed = triggerValue >= 0.5f;
            }

            resetPressed =
                _rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary)
                    && secondary;
            calibratePressed =
                _rightController.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary)
                    && primary;
        }

#if UNITY_EDITOR
        private void ReadEditorInput(
            out bool trackingValid,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out bool triggerPressed,
            out bool resetPressed,
            out bool calibratePressed)
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard.pKey.wasPressedThisFrame)
            {
                _autoRehearsal = !_autoRehearsal;
                if (_autoRehearsal)
                {
                    ResetAttempt();
                    _autoRehearsalStartedAt = Time.unscaledTime;
                }
            }

            if (_autoRehearsal)
            {
                float cycleTime = Time.unscaledTime - _autoRehearsalStartedAt;
                if (cycleTime >= 10f)
                {
                    ResetAttempt();
                    _autoRehearsalStartedAt = Time.unscaledTime;
                    cycleTime = 0f;
                }

                if (cycleTime < 1f)
                {
                    worldPosition = _seamStart + Vector3.up * 0.07f;
                    triggerPressed = false;
                }
                else
                {
                    float progress = Mathf.Clamp01((cycleTime - 1f) / 6f);
                    float demonstrationWobble = Mathf.Sin(progress * Mathf.PI * 4f) * 0.006f;
                    worldPosition =
                        Vector3.Lerp(_seamStart, _seamEnd, progress) +
                        Vector3.up * demonstrationWobble;
                    triggerPressed = progress < 1f;
                }

                _editorToolPosition = worldPosition;
                trackingValid = true;
                worldRotation = Quaternion.identity;
                resetPressed = false;
                calibratePressed = false;
                return;
            }

            float movementSpeed = keyboard.leftShiftKey.isPressed ? 0.35f : 0.12f;
            Vector3 movement = Vector3.zero;

            movement += (keyboard.lKey.isPressed ? Vector3.right : Vector3.zero);
            movement += (keyboard.jKey.isPressed ? Vector3.left : Vector3.zero);
            movement += (keyboard.iKey.isPressed ? Vector3.up : Vector3.zero);
            movement += (keyboard.kKey.isPressed ? Vector3.down : Vector3.zero);

            Camera camera = Camera.main;
            if (camera != null)
            {
                movement += (keyboard.oKey.isPressed ? camera.transform.forward : Vector3.zero);
                movement -= (keyboard.uKey.isPressed ? camera.transform.forward : Vector3.zero);
            }

            _editorToolPosition += movement.normalized * movementSpeed * Time.unscaledDeltaTime;
            trackingValid = true;
            worldPosition = _editorToolPosition;
            worldRotation = Quaternion.identity;
            triggerPressed = keyboard.spaceKey.isPressed;
            resetPressed = keyboard.rKey.wasPressedThisFrame;
            calibratePressed = keyboard.cKey.wasPressedThisFrame;
        }
#endif

        private bool EnsureSeamPlaced()
        {
            if (_seamPlaced)
            {
                return true;
            }

            Camera camera = Camera.main;
            if (camera == null || Time.frameCount < 3)
            {
                return false;
            }

            Transform cameraTransform = camera.transform;
            Vector3 centre =
                cameraTransform.position +
                cameraTransform.forward * 0.75f -
                cameraTransform.up * 0.25f;
            Vector3 direction = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
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
            BeginRecording();
            return true;
        }

        private void ResolveTrackingOrigin()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Transform root = camera.transform.root;
            _trackingOrigin = root != camera.transform ? root : null;
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
            FinishRecording(false, "reset");
            _isCalibrating = false;
            _attemptStarted = false;
            _beadProgress = 0f;
            _errorSum = 0f;
            _speedSum = 0f;
            _sampleCount = 0;
            _goodSampleCount = 0;
            _smoothedSpeed = 0f;
            _hasPreviousPosition = false;
            _completed = false;
            _sessionSaved = false;
            _guide.SetPosition(0, _seamStart);
            _guide.SetPosition(1, _seamEnd);
            UpdateBead();
            BeginRecording();
        }

        private void HandleCalibration(Vector3 toolPosition)
        {
            if (!_isCalibrating)
            {
                FinishRecording(false, "recalibration_started");
                _isCalibrating = true;
                _calibrationStart = toolPosition;
                _guide.SetPosition(0, _calibrationStart);
                _guide.SetPosition(1, _calibrationStart);
                _bead.enabled = false;
                _calibrationMessage =
                    "SEAM PLACEMENT\nStart captured\nMove the tool to the seam end\nand press A or C again";
                return;
            }

            float length = Vector3.Distance(_calibrationStart, toolPosition);
            if (length < MinimumCalibrationLength)
            {
                _calibrationMessage =
                    "SEAM PLACEMENT\nEnd is too close to start\nUse at least 15 cm\nand press A or C again";
                return;
            }

            if (length > MaximumCalibrationLength)
            {
                _calibrationMessage =
                    "SEAM PLACEMENT\nEnd is too far from start\nUse less than 1.5 m\nand press A or C again";
                return;
            }

            _seamStart = _calibrationStart;
            _seamEnd = toolPosition;
            _guide.SetPosition(0, _seamStart);
            _guide.SetPosition(1, _seamEnd);
            _isCalibrating = false;
            ResetAttempt();
        }

        private void BeginRecording()
        {
            if (_recorder == null)
            {
                return;
            }

            _recorder.Begin();
            _sessionSaved = false;
            _recordingStartedAt = Time.realtimeSinceStartup;
            _nextRecordTime = _recordingStartedAt;
        }

        private void FinishRecording(bool completed, string reason)
        {
            if (_recorder == null || !_recorder.IsActive)
            {
                return;
            }

            float averageError = _sampleCount > 0 ? _errorSum / _sampleCount : 0f;
            float averageSpeed = _sampleCount > 0 ? _speedSum / _sampleCount : 0f;
            float goodPercent = _sampleCount > 0 ? 100f * _goodSampleCount / _sampleCount : 0f;

            try
            {
                string savedDirectory = _recorder.Finish(
                    completed,
                    reason,
                    _beadProgress,
                    averageError,
                    averageSpeed,
                    goodPercent);
                _sessionSaved = !string.IsNullOrEmpty(savedDirectory);
            }
            catch (Exception exception)
            {
                _sessionSaved = false;
                Debug.LogError($"Could not save prototype session: {exception}");
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                FinishRecording(false, "application_paused");
            }
            else if (_seamPlaced && !_completed && _recorder != null && !_recorder.IsActive)
            {
                BeginRecording();
            }
        }

        private void OnApplicationQuit()
        {
            FinishRecording(false, "application_quit");
        }

        private void SendWarningHaptic(Color stateColour)
        {
            if (!_rightController.isValid || Time.unscaledTime < _nextHapticTime)
            {
                return;
            }

            float amplitude = stateColour == BadColour ? 0.65f : stateColour == WarningColour ? 0.25f : 0f;
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
            _hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hud.fontSize = 52;
            _hud.characterSize = 0.009f;
            _hud.anchor = TextAnchor.MiddleCenter;
            _hud.alignment = TextAlignment.Center;
            _hud.color = Color.white;
        }

        private static void ConfigureLineRenderer(LineRenderer line, float width, Color colour)
        {
            line.useWorldSpace = true;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 6;
            line.material = CreateMaterial(colour);
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

            Material material = new Material(shader);
            material.color = colour;
            return material;
        }

        private void SetToolColour(Color colour)
        {
            Renderer renderer = _toolTip != null ? _toolTip.GetComponent<Renderer>() : null;
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
            _bead.SetPosition(1, Vector3.Lerp(_seamStart, _seamEnd, _beadProgress));
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
