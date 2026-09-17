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
        private const float PositionSmoothing = 16f;
        private const float SpeedSmoothing = 10f;

        [Header("Controller attachment")]
        [Tooltip("Welding tip position relative to the tracked right-controller pose, in metres.")]
        [SerializeField] private Vector3 controllerToTipOffset = Vector3.zero;

        [Header("Demo seam")]
        [SerializeField, Min(0.15f)] private float defaultSeamLengthMetres = 0.5f;
        [SerializeField, Min(0.01f)] private float minimumCalibrationLength = 0.15f;
        [SerializeField, Min(0.15f)] private float maximumCalibrationLength = 1.5f;

        [Header("Demo tolerances - not validated welding requirements")]
        [SerializeField, Min(0.001f)] private float goodDistanceMetres = 0.025f;
        [SerializeField, Min(0.001f)] private float maximumWeldDistanceMetres = 0.05f;
        [SerializeField, Min(0f)] private float minimumGoodSpeed = 0.05f;
        [SerializeField, Min(0.01f)] private float maximumGoodSpeed = 0.15f;
        [SerializeField, Range(0.8f, 1f)] private float completionThreshold = 0.97f;
        [SerializeField, Range(0.01f, 0.25f)] private float startProgressThreshold = 0.08f;
        [SerializeField, Range(0.01f, 0.25f)] private float maximumProgressJump = 0.08f;
        [SerializeField, Range(0f, 0.25f)] private float reverseProgressTolerance = 0.03f;

        [Header("Optional angle feedback")]
        [SerializeField] private bool evaluateOrientation;
        [SerializeField] private bool orientationInhibitsWelding;
        [SerializeField] private Vector3 localToolForwardAxis = Vector3.forward;
        [SerializeField, Range(0f, 180f)] private float targetTravelAngleDegrees;
        [SerializeField, Range(0f, 90f)] private float travelAngleToleranceDegrees = 15f;
        [SerializeField, Range(0f, 180f)] private float targetWorkAngleDegrees = 45f;
        [SerializeField, Range(0f, 90f)] private float workAngleToleranceDegrees = 15f;

        [Header("Presentation feedback")]
        [SerializeField] private bool enableAudioFeedback = true;
        [SerializeField, Range(0f, 1f)] private float audioFeedbackVolume = 0.25f;

        private static readonly Color GoodColour = new Color(0.1f, 1f, 0.25f, 1f);
        private static readonly Color WarningColour = new Color(1f, 0.75f, 0.05f, 1f);
        private static readonly Color BadColour = new Color(1f, 0.12f, 0.08f, 1f);
        private static readonly Color GuideColour = new Color(0.1f, 0.75f, 1f, 1f);
        private static readonly Color EndColour = new Color(0.35f, 0.55f, 1f, 1f);

        private InputDevice _rightController;
        private InputDevice _leftController;
        private Transform _trackingOrigin;
        private Transform _toolTip;
        private Transform _seamStartMarker;
        private Transform _seamEndMarker;
        private LineRenderer _guide;
        private LineRenderer _bead;
        private TextMesh _hud;
        private TextMesh _seamStartLabel;
        private TextMesh _seamEndLabel;
        private AudioSource _audioSource;
        private AudioClip _weldStartedClip;
        private AudioClip _weldInhibitedClip;
        private AudioClip _weldCompletedClip;

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
        private float _nextAudioCueTime;
        private float _recordingStartedAt;
        private float _nextRecordTime;
        private float _trackingLossStartedAt;
        private float _invalidTrackingSeconds;
        private int _trackingInterruptionCount;
        private bool _hasPreviousPosition;
        private bool _seamPlaced;
        private bool _attemptStarted;
        private bool _completed;
        private bool _resetWasPressed;
        private bool _calibrateWasPressed;
        private bool _sessionSaved;
        private bool _isCalibrating;
        private bool _trackingInterrupted;
        private bool _requiresTriggerRelease;
        private Vector3 _calibrationStart;
        private string _calibrationMessage;
        private PrototypeSessionRecorder _recorder;
        private FeedbackAudioState _feedbackAudioState;

        private enum FeedbackAudioState
        {
            Idle,
            Active,
            Inhibited,
            Completed
        }

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
            ClampSettings();
            _recorder = new PrototypeSessionRecorder();
            CreateToolTip();
            CreateGuide();
            CreateSeamMarkers();
            CreateBead();
            CreateHud();
            CreateAudioFeedback();
            ResolveTrackingOrigin();
        }

        private void OnValidate()
        {
            ClampSettings();
        }

        private void ClampSettings()
        {
            defaultSeamLengthMetres = Mathf.Max(0.15f, defaultSeamLengthMetres);
            minimumCalibrationLength = Mathf.Max(0.01f, minimumCalibrationLength);
            maximumCalibrationLength = Mathf.Max(
                minimumCalibrationLength,
                maximumCalibrationLength);
            goodDistanceMetres = Mathf.Max(0.001f, goodDistanceMetres);
            maximumWeldDistanceMetres = Mathf.Max(goodDistanceMetres, maximumWeldDistanceMetres);
            minimumGoodSpeed = Mathf.Max(0f, minimumGoodSpeed);
            maximumGoodSpeed = Mathf.Max(minimumGoodSpeed, maximumGoodSpeed);
            if (localToolForwardAxis.sqrMagnitude < 0.0001f)
            {
                localToolForwardAxis = Vector3.forward;
            }
            localToolForwardAxis.Normalize();
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
                HandleTrackingLost();
                _hasPreviousPosition = false;
                SetToolColour(BadColour);
                SetHud(
                    "CONTROLLER TRACKING LOST\nWelding paused\n" +
                    "Move or wake the right controller\nRelease trigger before resuming");
                _calibrateWasPressed = calibratePressed;
                return;
            }

            HandleTrackingRestored(triggerPressed);

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

            UpdateEvaluation(triggerPressed, rawToolRotation);
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
            FaceTextToCamera(_seamStartLabel, cameraTransform);
            FaceTextToCamera(_seamEndLabel, cameraTransform);
        }

        private void UpdateEvaluation(bool triggerPressed, Quaternion toolRotation)
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

            bool positionGood = distance <= goodDistanceMetres;
            bool positionWeldable = distance <= maximumWeldDistanceMetres;
            bool speedGood = _smoothedSpeed >= minimumGoodSpeed && _smoothedSpeed <= maximumGoodSpeed;
            bool nearStart = progress <= startProgressThreshold;
            Vector3 toolForward = toolRotation * localToolForwardAxis;
            float travelAngle = Vector3.Angle(toolForward, seam.normalized);
            float workAngle = Vector3.Angle(toolForward, Vector3.up);
            bool orientationGood =
                !evaluateOrientation ||
                (Mathf.Abs(travelAngle - targetTravelAngleDegrees) <= travelAngleToleranceDegrees &&
                 Mathf.Abs(workAngle - targetWorkAngleDegrees) <= workAngleToleranceDegrees);

            if (!_attemptStarted && triggerPressed && positionWeldable && nearStart)
            {
                _attemptStarted = true;
            }

            bool progressContinuous =
                progress <= _beadProgress + maximumProgressJump &&
                progress >= _beadProgress - reverseProgressTolerance;
            bool activationAllowed =
                _attemptStarted &&
                !_requiresTriggerRelease &&
                positionWeldable &&
                progressContinuous &&
                (!evaluateOrientation || !orientationInhibitsWelding || orientationGood) &&
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
                    travelAngle,
                    workAngle,
                    orientationGood,
                    triggerPressed,
                    activationAllowed);
                _nextRecordTime = Time.realtimeSinceStartup + 0.1f;
            }

            Color stateColour;
            if (triggerPressed && !activationAllowed)
            {
                stateColour = BadColour;
            }
            else if (positionGood && speedGood && orientationGood)
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
                if (positionGood && speedGood && orientationGood)
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

            if (!_completed && _beadProgress >= completionThreshold)
            {
                _completed = true;
                _beadProgress = 1f;
                UpdateBead();
                FinishRecording(true, "completed");
            }

            UpdateAudioFeedback(triggerPressed, activationAllowed, welding);

            SetHud(BuildStatusText(
                distance,
                progress,
                triggerPressed,
                positionWeldable,
                nearStart,
                progressContinuous,
                travelAngle,
                workAngle,
                orientationGood));
        }

        private string BuildStatusText(
            float distance,
            float progress,
            bool triggerPressed,
            bool positionWeldable,
            bool nearStart,
            bool progressContinuous,
            float travelAngle,
            float workAngle,
            bool orientationGood)
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
                    "WELD COMPLETE\nCompletion: 100%\nAverage error: {0:0.0} cm\nAverage speed: {1:0.0} cm/s\nGood samples: {2:0}%\nTracking interruptions: {3} ({4:0.0} s)\n{5}\nPress B or R to reset\nPress A or C to place a new seam",
                    averageError * 100f,
                    averageSpeed * 100f,
                    quality,
                    _trackingInterruptionCount,
                    GetInvalidTrackingSeconds(),
                    saveStatus);
            }

            string positionLabel = distance <= goodDistanceMetres
                ? "Position: GOOD"
                : positionWeldable
                    ? "Position: WARNING"
                    : "Position: TOO FAR";
            string speedLabel = _smoothedSpeed < minimumGoodSpeed
                ? "Speed: TOO SLOW"
                : _smoothedSpeed > maximumGoodSpeed
                    ? "Speed: TOO FAST"
                    : "Speed: GOOD";
            string activationLabel;
            if (!_attemptStarted)
            {
                activationLabel = nearStart
                    ? "At seam start - hold trigger to begin"
                    : "Move to the seam start";
            }
            else if (_requiresTriggerRelease)
            {
                activationLabel = "Laser simulation: INHIBITED - release trigger to re-arm";
            }
            else if (!positionWeldable)
            {
                activationLabel = "Laser simulation: INHIBITED - too far";
            }
            else if (!progressContinuous)
            {
                activationLabel = "Laser simulation: INHIBITED - return to bead edge";
            }
            else if (evaluateOrientation && orientationInhibitsWelding && !orientationGood)
            {
                activationLabel = "Laser simulation: INHIBITED - tool angle";
            }
            else
            {
                activationLabel = triggerPressed
                    ? "Laser simulation: ACTIVE"
                    : "Hold trigger to weld";
            }

            string angleLabel = evaluateOrientation
                ? string.Format(
                    "Angles: travel {0:0} deg, work {1:0} deg - {2}",
                    travelAngle,
                    workAngle,
                    orientationGood ? "GOOD" : "ADJUST")
                : "Angles: disabled until tool axis is verified";

            return string.Format(
                "{0}\n{1}\n{2}\nError: {3:0.0} cm\nProgress: {4:0}%\n{5}\nFollow green START to blue END\nPress A or C to place seam",
                positionLabel,
                speedLabel,
                angleLabel,
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

            worldPosition += worldRotation * controllerToTipOffset;

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

            _seamStart = centre - direction * (defaultSeamLengthMetres * 0.5f);
            _seamEnd = centre + direction * (defaultSeamLengthMetres * 0.5f);
            _guide.SetPosition(0, _seamStart);
            _guide.SetPosition(1, _seamEnd);
            UpdateSeamMarkers(_seamStart, _seamEnd);
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
            _feedbackAudioState = FeedbackAudioState.Idle;
            _trackingInterrupted = false;
            _requiresTriggerRelease = false;
            _trackingLossStartedAt = 0f;
            _invalidTrackingSeconds = 0f;
            _trackingInterruptionCount = 0;
            _guide.SetPosition(0, _seamStart);
            _guide.SetPosition(1, _seamEnd);
            UpdateSeamMarkers(_seamStart, _seamEnd);
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
                UpdateSeamMarkers(_calibrationStart, _calibrationStart, false);
                _bead.enabled = false;
                _calibrationMessage =
                    "SEAM PLACEMENT\nStart captured\nMove the tool to the seam end\nand press A or C again";
                return;
            }

            float length = Vector3.Distance(_calibrationStart, toolPosition);
            if (length < minimumCalibrationLength)
            {
                _calibrationMessage =
                    $"SEAM PLACEMENT\nEnd is too close to start\nUse at least {minimumCalibrationLength * 100f:0} cm\nand press A or C again";
                return;
            }

            if (length > maximumCalibrationLength)
            {
                _calibrationMessage =
                    $"SEAM PLACEMENT\nEnd is too far from start\nUse less than {maximumCalibrationLength:0.0} m\nand press A or C again";
                return;
            }

            _seamStart = _calibrationStart;
            _seamEnd = toolPosition;
            _guide.SetPosition(0, _seamStart);
            _guide.SetPosition(1, _seamEnd);
            UpdateSeamMarkers(_seamStart, _seamEnd);
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
                    goodPercent,
                    _trackingInterruptionCount,
                    GetInvalidTrackingSeconds());
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
                if (_attemptStarted && !_completed)
                {
                    _requiresTriggerRelease = true;
                }
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

        private void HandleTrackingLost()
        {
            if (!_attemptStarted || _completed || _trackingInterrupted)
            {
                return;
            }

            _trackingInterrupted = true;
            _requiresTriggerRelease = true;
            _trackingLossStartedAt = Time.realtimeSinceStartup;
            _trackingInterruptionCount++;
        }

        private void HandleTrackingRestored(bool triggerPressed)
        {
            if (_trackingInterrupted)
            {
                _invalidTrackingSeconds += Mathf.Max(
                    0f,
                    Time.realtimeSinceStartup - _trackingLossStartedAt);
                _trackingInterrupted = false;
                _trackingLossStartedAt = 0f;
            }

            if (_requiresTriggerRelease && !triggerPressed)
            {
                _requiresTriggerRelease = false;
            }
        }

        private float GetInvalidTrackingSeconds()
        {
            if (!_trackingInterrupted)
            {
                return _invalidTrackingSeconds;
            }

            return _invalidTrackingSeconds + Mathf.Max(
                0f,
                Time.realtimeSinceStartup - _trackingLossStartedAt);
        }

        private void CreateAudioFeedback()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
            _audioSource.volume = audioFeedbackVolume;

            _weldStartedClip = CreateToneClip("Weld started", 660f, 820f, 0.09f);
            _weldInhibitedClip = CreateToneClip("Weld inhibited", 240f, 150f, 0.14f);
            _weldCompletedClip = CreateToneClip("Weld completed", 620f, 1040f, 0.28f);
        }

        private void UpdateAudioFeedback(
            bool triggerPressed,
            bool activationAllowed,
            bool welding)
        {
            FeedbackAudioState nextState;
            if (_completed)
            {
                nextState = FeedbackAudioState.Completed;
            }
            else if (welding)
            {
                nextState = FeedbackAudioState.Active;
            }
            else if (triggerPressed && !activationAllowed)
            {
                nextState = FeedbackAudioState.Inhibited;
            }
            else
            {
                nextState = FeedbackAudioState.Idle;
            }

            if (nextState == _feedbackAudioState)
            {
                return;
            }

            _feedbackAudioState = nextState;
            if (!enableAudioFeedback || _audioSource == null)
            {
                return;
            }

            AudioClip cue = nextState switch
            {
                FeedbackAudioState.Active => _weldStartedClip,
                FeedbackAudioState.Inhibited => _weldInhibitedClip,
                FeedbackAudioState.Completed => _weldCompletedClip,
                _ => null
            };

            if (cue != null)
            {
                if (nextState != FeedbackAudioState.Completed &&
                    Time.unscaledTime < _nextAudioCueTime)
                {
                    return;
                }

                _audioSource.PlayOneShot(cue);
                _nextAudioCueTime = Time.unscaledTime + 0.25f;
            }
        }

        private static AudioClip CreateToneClip(
            string clipName,
            float startFrequency,
            float endFrequency,
            float durationSeconds)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
            var samples = new float[sampleCount];
            float phase = 0f;

            for (int index = 0; index < sampleCount; index++)
            {
                float progress = index / (float)Mathf.Max(1, sampleCount - 1);
                float frequency = Mathf.Lerp(startFrequency, endFrequency, progress);
                phase += 2f * Mathf.PI * frequency / sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * progress);
                samples[index] = Mathf.Sin(phase) * envelope * 0.35f;
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
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

        private void CreateSeamMarkers()
        {
            _seamStartMarker = CreateSeamMarker("Seam Start Marker", GoodColour);
            _seamEndMarker = CreateSeamMarker("Seam End Marker", EndColour);
            _seamStartLabel = CreateWorldLabel("Seam Start Label", "START", GoodColour);
            _seamEndLabel = CreateWorldLabel("Seam End Label", "END", EndColour);

            _seamStartMarker.gameObject.SetActive(false);
            _seamEndMarker.gameObject.SetActive(false);
            _seamStartLabel.gameObject.SetActive(false);
            _seamEndLabel.gameObject.SetActive(false);
        }

        private Transform CreateSeamMarker(string markerName, Color colour)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = Vector3.one * 0.032f;
            Destroy(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().material = CreateMaterial(colour);
            return marker.transform;
        }

        private TextMesh CreateWorldLabel(string objectName, string text, Color colour)
        {
            GameObject labelObject = new GameObject(objectName);
            labelObject.transform.SetParent(transform, false);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 48;
            label.characterSize = 0.0035f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = colour;
            label.text = text;
            return label;
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

        private void UpdateSeamMarkers(Vector3 start, Vector3 end, bool showEnd = true)
        {
            if (_seamStartMarker == null || _seamEndMarker == null)
            {
                return;
            }

            _seamStartMarker.gameObject.SetActive(true);
            _seamStartLabel.gameObject.SetActive(true);
            _seamEndMarker.gameObject.SetActive(showEnd);
            _seamEndLabel.gameObject.SetActive(showEnd);

            _seamStartMarker.position = start;
            _seamEndMarker.position = end;
            _seamStartLabel.transform.position = start + Vector3.up * 0.045f;
            _seamEndLabel.transform.position = end + Vector3.up * 0.045f;
        }

        private static void FaceTextToCamera(TextMesh label, Transform cameraTransform)
        {
            if (label == null || !label.gameObject.activeInHierarchy)
            {
                return;
            }

            label.transform.rotation = Quaternion.LookRotation(
                label.transform.position - cameraTransform.position,
                cameraTransform.up);
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
