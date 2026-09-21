using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
        public Vector3 controllerTipOffset = new Vector3(-0.12f, 0, 0);
        public Vector3 controllerTipEulerOffset = Vector3.zero;
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
        private bool overheatRehearsal;
        private float rehearsalStart;
        private float nextHudUpdate;
        private string status = "Ready";

        private enum AttemptState
        {
            Ready,
            Welding,
            InvalidStart,
            Completed
        }

        private enum StartSide
        {
            None,
            Left,
            Right
        }

        private AttemptState attemptState = AttemptState.Ready;
        private StartSide startSide = StartSide.None;

        [Header("Attempt rules")]
        [Range(0.005f, 0.1f)]
        public float endTolerance = 0.025f;

        private float attemptStartTime;
        private float arcOnTime;
        private float weldingTravelDistance;
        private float distanceIntegral;
        private float distanceSampleTime;
        private float maxTipDistance;
        private int interruptions;

        [Header("Tool tip marker")]
        [Tooltip("Create a visible sphere exactly at the calculated welding tip.")]
        public bool ensureTipMarkerSphere = true;

        [Range(0.005f, 0.05f)]
        public float tipMarkerDiameter = 0.018f;

        public Color tipMarkerColor = new Color(1f, 0.85f, 0.1f, 1f);

        [Tooltip("Optional material for the tip sphere. Leave empty to clone the active render pipeline default material.")]
        public Material tipMarkerMaterial;

        [Tooltip("Disable existing renderers below Tool Visual so only the tip sphere is shown.")]
        public bool hideExistingToolVisualRenderers = true;

        [Header("Laser power and penetration model")]
        [Tooltip("Current laser power. This affects subsequent penetration samples.")]
        [Min(1f)]
        public float laserPowerWatts = 1500f;

        [Min(1f)]
        public float minimumLaserPowerWatts = 500f;

        [Min(1f)]
        public float maximumLaserPowerWatts = 3000f;

        [Min(1f)]
        public float laserPowerStepWatts = 100f;

        [Tooltip("Right thumbstick up/down changes power by one step per deflection. Disable when a virtual menu owns power control.")]
        public bool enableXrPowerThumbstick = true;

        [Tooltip("Plate thickness in mm: full penetration is separate from subsequent burn-through.")]
        [Min(0.1f)]
        public float materialThicknessMm = 6f;

        [Tooltip("Depth after Formation Dose + Reference Exposure equivalent seconds at reference power.")]
        [Min(0.01f)]
        public float referencePenetrationMm = 3f;

        [Min(1f)]
        public float referencePowerWatts = 1500f;

        [Tooltip("Legacy presentation speed. Used for movement statistics only, never for penetration.")]
        [Min(0.001f)]
        public float referenceTravelSpeedMps = 0.08f;

        [Tooltip("Exponent of accumulated thermal dose to penetration depth (presentation model).")]
        [Range(0.1f, 2f)]
        public float penetrationEnergyExponent = 1f;

        [Tooltip("Legacy serialized setting; no longer used in penetration or speed calculation.")]
        [Min(0.001f)]
        public float minimumModelSpeedMps = 0.01f;

        [Tooltip("Smoothing rate for hand-speed measurements. Higher values react faster.")]
        [Range(0.5f, 30f)]
        public float speedSmoothing = 8f;

        [Header("Cell heating and burn-through (presentation model)")]
        [Min(0.001f)] public float heatedFootprintMetres = 0.024f;
        [Min(0.01f)] public float formationDoseSeconds = 0.12f;
        [Min(0.01f)] public float referenceExposureSeconds = 0.18f;
        [Min(0.01f)] public float thermalCoolingSeconds = 2f;
        [Range(0.5f, 0.99f)] public float burnWarningFraction = 0.85f;
        [Min(0.05f)] public float burnThroughExtraDoseSeconds = 0.35f;
        [Tooltip("Optional flange renderers. Empty = mesh renderers under Workpiece, excluding the bead.")]
        public Renderer[] burnThroughSurfaces;

        [Header("Penetration graph")]
        public bool showPenetrationGraph = true;

        [Tooltip("Optional pose for the graph. If empty, the graph is positioned below Status Text.")]
        public Transform penetrationGraphAnchor;

        [Tooltip("Width and height of the graph in world metres.")]
        public Vector2 penetrationGraphSize = new Vector2(0.42f, 0.16f);

        [Tooltip("Automatic graph offset relative to Status Text axes when no anchor is assigned.")]
        public Vector3 penetrationGraphOffset = new Vector3(0f, -0.22f, 0.005f);

        [Range(0.001f, 0.012f)]
        public float penetrationGraphLineWidth = 0.003f;

        [Range(0.003f, 0.03f)]
        public float penetrationGraphTextSize = 0.01f;

        [Tooltip("Optional URP-compatible material for penetration graph LineRenderers.")]
        public Material penetrationGraphMaterial;

        [Header("XR Controller Debug")]
        [Tooltip("Show controller axes in Editor and Development Builds.")]
        public bool showControllerDebugAxes = false;

        [Range(0.03f, 0.30f)]
        public float debugAxisLength = 0.15f;

        [Range(0.001f, 0.02f)]
        public float debugAxisThickness = 0.006f;

        [Tooltip("Optional material for LineRenderer axes. Leave empty to use the active render pipeline default line material.")]
        public Material debugAxisMaterial;

        private GameObject debugAxesRoot;
        private LineRenderer debugAxisX;
        private LineRenderer debugAxisY;
        private LineRenderer debugAxisZ;
        private LineRenderer debugTipLine;
        private Transform debugControllerOrigin;
        private Renderer debugOriginRenderer;
        private Transform runtimeTipMarker;
        private Renderer runtimeTipMarkerRenderer;

        private readonly List<Material> ownedRuntimeMaterials = new List<Material>();
        private bool waitForTriggerRelease;
        private string completedSummary = string.Empty;

        private SeamThermalModel thermal;
        private FusionBurnThroughVisual burnVisual;
        private float lastThermalTime;
        private float currentMetres;
        private int warningCells;
        private Mesh graphBars;
        private Vector3[] graphVertices;
        private Color[] graphColors;
        private LineRenderer warningThresholdLine;
        private TextMesh graphLegend;
        private float[] penetrationByCellMm;
        private float filteredTravelSpeedMps;
        private float currentPenetrationMm;
        private float powerTimeIntegral;
        private bool xrPowerAxisLatched;

        private GameObject penetrationGraphRoot;
        private LineRenderer penetrationGraphLine;
        private LineRenderer penetrationGraphFrame;
        private TextMesh penetrationGraphTitle;
        private TextMesh penetrationGraphTopLabel;
        private TextMesh penetrationGraphLeftLabel;
        private TextMesh penetrationGraphRightLabel;
        private float nextPenetrationGraphUpdate;

        private bool ShouldShowControllerDebugAxes =>
            controlMode == ControlMode.XR &&
            showControllerDebugAxes &&
            (Application.isEditor || Debug.isDebugBuild);

        private void Start()
        {
            if (workpiece == null || bead == null || sparks == null || toolVisual == null)
            {
                Debug.LogError("Fusion demo: assign Workpiece, Bead, Sparks and Tool Visual.", this);
                enabled = false;
                return;
            }

            if ((workpiece.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
            {
                Debug.LogError("Fusion demo: Workpiece and its parents must have unit scale (metres).", this);
                enabled = false;
                return;
            }

            EnsureTipMarker();
            InitializePenetrationModel();

            if (ShouldShowControllerDebugAxes)
                CreateControllerDebugAxes();

            Debug.Log(
                $"Fusion demo: XR debug axes requested={showControllerDebugAxes}, " +
                $"debugBuild={Debug.isDebugBuild}, controlMode={controlMode}.",
                this);
        }

        private void Update()
        {
            if (!focused || suspended)
            {
                BreakStroke();
                return;
            }

            ReadInput(
                out Vector3 tipWorld,
                out Quaternion rotation,
                out bool trigger,
                out bool valid,
                out bool reset);

            if (reset && !previousReset)
                ResetAttempt();

            previousReset = reset;

            if (rehearsing)
                ReadRehearsal(out tipWorld, out rotation, out trigger, out valid);

            toolVisual.gameObject.SetActive(valid);

            if (!valid)
            {
                SetDebugAxesVisible(false);
                BreakStroke();

                // Do not destroy a final result merely because tracking was lost later.
                if (attemptState == AttemptState.Completed)
                    ShowStatusImmediate(completedSummary);
                else if (attemptState == AttemptState.InvalidStart)
                    ShowInvalidStartStatus();
                else
                    UpdateHud("Tracking unavailable — welding stopped");

                return;
            }

            toolVisual.SetPositionAndRotation(tipWorld, rotation);

            // B/R starts a new attempt, but a still-held trigger must not immediately
            // start another weld on the same frame sequence.
            if (waitForTriggerRelease)
            {
                if (trigger)
                {
                    BreakStroke();
                    UpdateHud("Release trigger, then start from the LEFT or RIGHT end");
                    return;
                }

                waitForTriggerRelease = false;
                previousWelding = false;
            }

            if (attemptState == AttemptState.Completed)
            {
                BreakStroke();
                ShowStatusImmediate(completedSummary);
                return;
            }

            if (attemptState == AttemptState.InvalidStart)
            {
                BreakStroke();
                ShowInvalidStartStatus();
                return;
            }

            Vector3 tipLocal = workpiece.InverseTransformPoint(tipWorld);
            float z = Mathf.Clamp(tipLocal.z, -0.5f, 0.5f);
            Vector3 closestLocal = new Vector3(0, 0, z);
            float distance = Vector3.Distance(tipLocal, closestLocal);

            // The inside quadrant prevents welding through the back of either flange.
            bool accessible = tipLocal.x >= -0.001f && tipLocal.y >= -0.001f;
            bool weldingCandidate =
                trigger &&
                accessible &&
                distance <= maximumDistance;

            float metres = z + 0.5f;


            if (attemptState == AttemptState.Ready && weldingCandidate)
            {
                if (IsAtLeftEnd(metres))
                {
                    BeginAttempt(StartSide.Left);
                }
                else if (IsAtRightEnd(metres))
                {
                    BeginAttempt(StartSide.Right);
                }
                else
                {
                    RejectAttempt();
                    return;
                }
            }

            currentMetres = metres;
            bool welding =
                attemptState == AttemptState.Welding &&
                weldingCandidate;

            if (welding)
            {
                bool continuous =
                    previousWelding &&
                    Time.time - previousTime <= maximumSampleGap &&
                    Vector3.Distance(tipLocal, previousLocalTip) <= maximumBridgeDistance;

                // Heat only physically visited cells. Never snap thermal exposure to an end.
                GetWeldingTravelSpeedMps(continuous, metres); // display/statistics only
                AdvanceThermal(true, continuous ? previousMetres : metres, metres);
                powerTimeIntegral += laserPowerWatts * Time.deltaTime;

                arcOnTime += Time.deltaTime;
                distanceIntegral += distance * Time.deltaTime;
                distanceSampleTime += Time.deltaTime;
                maxTipDistance = Mathf.Max(maxTipDistance, distance);

                if (continuous)
                    weldingTravelDistance += Mathf.Abs(metres - previousMetres);

                if (HasReachedRequiredFinish(metres))
                {
                    CompleteAttempt();
                    return;
                }
            }

            if (!welding) AdvanceThermal(false, metres, metres);

            if (attemptState == AttemptState.Welding &&
                previousWelding &&
                !welding)
            {
                interruptions++;
            }

            sparks.SetWelding(
                welding,
                workpiece.TransformPoint(
                    closestLocal + new Vector3(0.0045f, 0.0045f, 0)),
                workpiece.TransformDirection(
                    new Vector3(1, 1, 0).normalized));

            previousWelding = welding;
            previousMetres = metres;
            previousLocalTip = tipLocal;
            previousTime = Time.time;

            UpdateHud(
                welding
                    ? "Сварка"
                    : trigger
                        ? "Переместите рабочий орган интрумента в начало заготовки"
                        : attemptState == AttemptState.Welding
                            ? "Сварка прекращена — продолжайте"
                            : "Начните сварку");
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
                if (!rightHand.isValid)
                    rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

                bool hasPosition = rightHand.TryGetFeatureValue(
                    CommonUsages.devicePosition,
                    out Vector3 trackingPosition);

                bool hasRotation = rightHand.TryGetFeatureValue(
                    CommonUsages.deviceRotation,
                    out Quaternion trackingRotation);

                bool tracked =
                    rightHand.TryGetFeatureValue(
                        CommonUsages.isTracked,
                        out bool isTracked)
                    && isTracked;

                valid =
                    rightHand.isValid &&
                    hasPosition &&
                    hasRotation &&
                    tracked &&
                    trackingOrigin != null;

                if (valid)
                {
                    Vector3 controllerWorld =
                        trackingOrigin.TransformPoint(trackingPosition);

                    Quaternion controllerWorldRotation =
                        trackingOrigin.rotation * trackingRotation;

                    // Position offset remains in the controller's local coordinates.
                    world = trackingOrigin.TransformPoint(
                        trackingPosition +
                        trackingRotation * controllerTipOffset);

                    // Optional visual rotation correction for a nozzle/tool model.
                    rotation =
                        controllerWorldRotation *
                        Quaternion.Euler(controllerTipEulerOffset);

                    if (ShouldShowControllerDebugAxes)
                    {
                        UpdateControllerDebugAxes(
                            controllerWorld,
                            controllerWorldRotation,
                            world);
                    }
                    else
                    {
                        SetDebugAxesVisible(false);
                    }
                }
                else
                {
                    SetDebugAxesVisible(false);
                }

                HandleXrPowerInput();

                trigger =
                    rightHand.TryGetFeatureValue(
                        CommonUsages.trigger,
                        out float value)
                    && value >= 0.5f;

                trigger |=
                    rightHand.TryGetFeatureValue(
                        CommonUsages.triggerButton,
                        out bool button)
                    && button;

                reset =
                    rightHand.TryGetFeatureValue(
                        CommonUsages.secondaryButton,
                        out bool secondary)
                    && secondary;

                return;
            }
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) { valid = false; return; }
            if (keyboard.upArrowKey.wasPressedThisFrame) AdjustLaserPower(laserPowerStepWatts);
            if (keyboard.downArrowKey.wasPressedThisFrame) AdjustLaserPower(-laserPowerStepWatts);
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
            if (controlMode != ControlMode.Desktop)
                return;

            bool start = !rehearsing;
            ResetAttempt();

            rehearsing = start;
            overheatRehearsal = false;
            waitForTriggerRelease = false;
            rehearsalStart = Time.time;
        }

        private void ReadRehearsal(
            out Vector3 world,
            out Quaternion rotation,
            out bool trigger,
            out bool valid)
        {
            float t = Time.time - rehearsalStart;

            if (overheatRehearsal)
            {
                // Start legally at an end, lift, then dwell at the centre so the hole
                // is surrounded by metal and is visible after the tip moves away.
                float centreZ = t < 0.25f ? -0.5f : Mathf.Lerp(-0.5f, 0, (t - 0.25f) / 0.5f);
                trigger = t < 0.25f || (t >= 0.75f && t < 3.75f);
                var tip = new Vector3(0.007f, 0.007f, centreZ);
                if (!trigger) tip += new Vector3(0.08f, 0.08f, 0);
                world = workpiece.TransformPoint(tip);
                rotation = workpiece.rotation * Quaternion.LookRotation(new Vector3(-1, -1, 0));
                valid = true;
                if (t >= 4) { rehearsing = false; desktopTip = tip; }
                return;
            }

            // The attempt rules require a weld to begin at one end.
            // Presentation therefore performs one valid LEFT -> RIGHT pass.
            const float duration = 12f;
            float progress = Mathf.Clamp01(t / duration);
            float z = Mathf.Lerp(-0.5f, 0.5f, progress);

            trigger = t <= duration;

            Vector3 local = new Vector3(0.007f, 0.007f, z);
            world = workpiece.TransformPoint(local);
            rotation =
                workpiece.rotation *
                Quaternion.LookRotation(new Vector3(-1, -1, 0));
            valid = true;

            if (t > duration)
            {
                rehearsing = false;
                desktopTip = local;
            }
        }

        public void ResetAttempt()
        {
            rehearsing = false;
            overheatRehearsal = false;
            previousWelding = false;

            attemptState = AttemptState.Ready;
            startSide = StartSide.None;

            attemptStartTime = 0f;
            arcOnTime = 0f;
            weldingTravelDistance = 0f;
            distanceIntegral = 0f;
            distanceSampleTime = 0f;
            maxTipDistance = 0f;
            interruptions = 0;
            completedSummary = string.Empty;
            currentPenetrationMm = 0f;
            filteredTravelSpeedMps = 0f;
            powerTimeIntegral = 0f;

            thermal?.Clear();
            lastThermalTime = Time.time;
            warningCells = 0;
            bead.ResetBead();
            if (thermal != null) burnVisual?.Refresh(thermal);
            UpdatePenetrationGraph(true);

            waitForTriggerRelease = true;

            desktopTip = new Vector3(0.007f, 0.007f, 0);

            sparks.Clear();

            ShowStatusImmediate(
                "READY\n" +
                "Start welding from the LEFT or RIGHT end.\n" +
                "Release trigger before starting.");
        }

        private void BreakStroke()
        {
            previousWelding = false;
            AdvanceThermal(false, currentMetres, currentMetres);
            if (sparks != null && sparks.isActiveAndEnabled)
                sparks.SetWelding(false, sparks.transform.position, sparks.transform.forward);
        }

        private void UpdateHud(string message)
        {
            string thermalStatus = ThermalStatus();
            if (!string.IsNullOrEmpty(thermalStatus)) message = thermalStatus + "\n" + message;
            status = message;

            if (statusText == null || Time.unscaledTime < nextHudUpdate)
                return;

            nextHudUpdate = Time.unscaledTime + 0.1f;
            statusText.color = thermal != null && thermal.Burned[bead.Coverage.IndexAt(currentMetres)]
                ? new Color(1, 0.3f, 0.2f)
                : warningCells > 0 ? new Color(1f, 0.65f, 0.15f) : Color.white;
            statusText.text =
                $"Статус\n" +
                $"{bead.Coverage.Fraction:P0} обработано\n" +
                $"Мощность: {laserPowerWatts:F0} Ватт\n" +
                $"Скорость: {filteredTravelSpeedMps * 1000f:F0} мм/с\n" +
                $"Проплавление: {currentPenetrationMm:F1}/{materialThicknessMm:F1} мм\n" +
                $"{message}";

            if (ShouldShowControllerDebugAxes)
            {
                statusText.text +=
                    $"\nTIP OFFSET: " +
                    $"X {controllerTipOffset.x:F3}  " +
                    $"Y {controllerTipOffset.y:F3}  " +
                    $"Z {controllerTipOffset.z:F3}";
            }
        }

        private void OnGUI()
        {
            if (!showDesktopPanel || controlMode != ControlMode.Desktop || bead == null || bead.Coverage == null) return;
            GUILayout.BeginArea(new Rect(20, 20, 560, 280), GUI.skin.box);
            string panelStatus = attemptState == AttemptState.Completed
                ? $"Проход завершён | Прожжено {thermal.BurnedCount * bead.Coverage.CellLength * 1000f:F0} мм"
                : status;
            GUILayout.Label($"FUSION  |  {bead.Coverage.Fraction:P1} обработано  |  {panelStatus}");
            GUILayout.Label($"Power: {laserPowerWatts:F0} W  |  Speed: {filteredTravelSpeedMps * 1000f:F0} mm/s  |  Penetration(model): {currentPenetrationMm:F1} mm");
            GUILayout.Label("Up/Down: power   J/L: along seam   I/K: height   U/O: depth\nSpace: weld   Shift: fast   R: reset   F2: presentation");
            if (GUILayout.Button(rehearsing ? "Stop presentation" : "Present: LEFT -> RIGHT")) ToggleRehearsal();
            if (GUILayout.Button("Show overheating at the centre")) StartOverheatRehearsal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-100 W")) AdjustLaserPower(-laserPowerStepWatts);
            if (GUILayout.Button("+100 W")) AdjustLaserPower(laserPowerStepWatts);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Reset weld")) ResetAttempt();
            GUILayout.EndArea();
        }

        public void StartOverheatRehearsal()
        {
            if (controlMode != ControlMode.Desktop) return;
            ResetAttempt();
            rehearsing = overheatRehearsal = true;
            waitForTriggerRelease = false;
            rehearsalStart = Time.time;
        }

        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!value)
                BreakStroke();
        }

        private void OnApplicationPause(bool value)
        {
            suspended = value;
            if (value)
                BreakStroke();
        }

        private void OnDisable()
        {
            previousWelding = false;
            SetDebugAxesVisible(false);

            if (sparks != null)
                sparks.Clear();
        }

        private void OnDestroy()
        {
            burnVisual?.Dispose();
            if (graphBars != null) Destroy(graphBars);
            if (debugAxesRoot != null)
                Destroy(debugAxesRoot);

            if (runtimeTipMarker != null)
                Destroy(runtimeTipMarker.gameObject);

            if (penetrationGraphRoot != null)
                Destroy(penetrationGraphRoot);

            foreach (Material material in ownedRuntimeMaterials)
            {
                if (material != null)
                    Destroy(material);
            }

            ownedRuntimeMaterials.Clear();
        }
        private bool IsAtLeftEnd(float metres) { return metres <= endTolerance; }
        private bool IsAtRightEnd(float metres) { return metres >= FusionBead.Length - endTolerance; }
        private bool HasReachedRequiredFinish(float metres)
        {
            if (bead.Coverage.CoveredCount != bead.Coverage.Count)
                return false;

            return startSide switch
            {
                StartSide.Left => IsAtRightEnd(metres),
                StartSide.Right => IsAtLeftEnd(metres),
                _ => false
            };
        }
        private void BeginAttempt(StartSide side)
        {
            attemptState = AttemptState.Welding;
            startSide = side;

            attemptStartTime = Time.time;
            arcOnTime = 0f;
            weldingTravelDistance = 0f;
            distanceIntegral = 0f;
            distanceSampleTime = 0f;
            maxTipDistance = 0f;
            interruptions = 0;
            filteredTravelSpeedMps = referenceTravelSpeedMps;
            currentPenetrationMm = 0f;
            powerTimeIntegral = 0f;

            previousWelding = false;
        }
        private void CompleteAttempt()
        {
            attemptState = AttemptState.Completed;
            BreakStroke();
            UpdatePenetrationGraph(true);

            float totalTime = Time.time - attemptStartTime;
            float averageSpeed =
                arcOnTime > 0f
                    ? weldingTravelDistance / arcOnTime
                    : 0f;

            float averageDistance =
                distanceSampleTime > 0f
                    ? distanceIntegral / distanceSampleTime
                    : 0f;

            float extraTravel =
                Mathf.Max(0f, weldingTravelDistance - FusionBead.Length);

            GetPenetrationStatistics(
                out float averagePenetrationMm,
                out float minimumPenetrationMm,
                out float maximumPenetrationMm,
                out float fullPenetrationPercent);

            float averagePowerWatts =
                arcOnTime > 0f
                    ? powerTimeIntegral / arcOnTime
                    : laserPowerWatts;

            string direction =
                startSide == StartSide.Left
                    ? "LEFT -> RIGHT"
                    : "RIGHT -> LEFT";

            completedSummary =
                (thermal.BurnedCount > 0 ? "Проход завершён — обнаружены прожоги\n" : "Сварка завершена\n") +
                $"Прожжено: {thermal.BurnedCount * bead.Coverage.CellLength * 1000f:F0} мм; " +
                $"целый шов: {(bead.Coverage.CoveredCount - thermal.BurnedCount) / (float)thermal.Count:P0}\n" +
                $"Итоговое время: {totalTime:F1} с\n" +
                $"Время работы: {arcOnTime:F1} с\n" +
                $"Средняя мощность: {averagePowerWatts:F0} Вт\n" +
                $"Средняя скорость: {averageSpeed * 1000f:F0} мм/с\n" +
                $"Проплавка целых участков, ср/мин/макс: " +
                $"{averagePenetrationMm:F1}/{minimumPenetrationMm:F1}/{maximumPenetrationMm:F1} мм\n" +
                $"Участков с полным проплавлением: {fullPenetrationPercent:F0}%\n" +
                $"Количество прерываний: {interruptions}\n" +
                $"Среднее отклонение: {averageDistance * 1000f:F1} мм\n" +
                "Нажмите B, чтобы начать сначала";

            ShowStatusImmediate(completedSummary);
        }

        private void RejectAttempt()
        {
            attemptState = AttemptState.InvalidStart;
            startSide = StartSide.None;

            BreakStroke();

            if (sparks != null)
                sparks.Clear();

            ShowInvalidStartStatus();
        }

        private void ShowInvalidStartStatus()
        {
            ShowStatusImmediate(
                "INVALID START\n" +
                "Start welding from the LEFT or RIGHT end.\n" +
                "Press B to START AGAIN.");
        }

        private void ShowStatusImmediate(string text)
        {
            status = text;

            if (statusText != null)
            {
                statusText.color = Color.white;
                statusText.text = text;
            }
        }
        public void SetLaserPower(float watts)
        {
            float minPower = Mathf.Min(minimumLaserPowerWatts, maximumLaserPowerWatts);
            float maxPower = Mathf.Max(minimumLaserPowerWatts, maximumLaserPowerWatts);
            laserPowerWatts = Mathf.Clamp(watts, minPower, maxPower);

            // Changing the setpoint must not rewrite previously measured penetration.
            UpdatePenetrationGraph(true);
        }

        public void AdjustLaserPower(float deltaWatts)
        {
            SetLaserPower(laserPowerWatts + deltaWatts);
        }

        private void HandleXrPowerInput()
        {
            if (!enableXrPowerThumbstick || !rightHand.isValid)
                return;

            if (!rightHand.TryGetFeatureValue(
                    CommonUsages.primary2DAxis,
                    out Vector2 axis))
            {
                xrPowerAxisLatched = false;
                return;
            }

            if (!xrPowerAxisLatched && axis.y >= 0.70f)
            {
                AdjustLaserPower(laserPowerStepWatts);
                xrPowerAxisLatched = true;
            }
            else if (!xrPowerAxisLatched && axis.y <= -0.70f)
            {
                AdjustLaserPower(-laserPowerStepWatts);
                xrPowerAxisLatched = true;
            }
            else if (Mathf.Abs(axis.y) <= 0.35f)
            {
                xrPowerAxisLatched = false;
            }
        }

        private void InitializePenetrationModel()
        {
            if (bead == null || bead.Coverage == null)
                return;

            thermal = new SeamThermalModel(bead.Coverage.Count, FusionBead.Length)
            {
                ReferencePower = Mathf.Max(1, referencePowerWatts),
                ReferenceDepth = Mathf.Max(0.01f, referencePenetrationMm),
                Thickness = Mathf.Max(0.1f, materialThicknessMm),
                DepthExponent = Mathf.Clamp(penetrationEnergyExponent, 0.1f, 2f),
                Footprint = Mathf.Max(bead.Coverage.CellLength, heatedFootprintMetres),
                FormationDose = Mathf.Max(0.01f, formationDoseSeconds),
                ReferenceExposure = Mathf.Max(0.01f, referenceExposureSeconds),
                CoolingTime = Mathf.Max(0.01f, thermalCoolingSeconds),
                WarningFraction = Mathf.Clamp(burnWarningFraction, 0.5f, 0.99f),
                BurnExtraDose = Mathf.Max(0.05f, burnThroughExtraDoseSeconds)
            };
            penetrationByCellMm = thermal.PenetrationMm;
            burnVisual = new FusionBurnThroughVisual(workpiece, bead, burnThroughSurfaces);
            lastThermalTime = Time.time;
            filteredTravelSpeedMps = 0f;

            SetLaserPower(laserPowerWatts);

            if (showPenetrationGraph)
            {
                CreatePenetrationGraph();
                UpdatePenetrationGraph(true);
            }
        }

        private float GetWeldingTravelSpeedMps(
            bool continuous,
            float metres)
        {
            float targetSpeed = 0f;

            if (continuous)
            {
                float dt = Time.time - previousTime;

                if (dt > 0.0001f)
                {
                    targetSpeed = Mathf.Abs(metres - previousMetres) / dt;
                }
            }


            if (filteredTravelSpeedMps <= 0f)
                filteredTravelSpeedMps = targetSpeed;

            float smoothing =
                1f - Mathf.Exp(
                    -Mathf.Max(0.01f, speedSmoothing) *
                    Mathf.Max(Time.deltaTime, 0.0001f));

            filteredTravelSpeedMps =
                Mathf.Lerp(
                    filteredTravelSpeedMps,
                    targetSpeed,
                    smoothing);

            return filteredTravelSpeedMps;
        }

        private void AdvanceThermal(bool welding, float from, float to)
        {
            if (thermal == null) return;
            float dt = Mathf.Max(0, Time.time - lastThermalTime);
            if (dt <= 0) return;
            lastThermalTime = Time.time;
            // Long gaps cool the part, they never count as unobserved laser exposure.
            thermal.Step(dt, laserPowerWatts, from, to, welding && dt <= maximumSampleGap);
            warningCells = 0;
            for (int i = 0; i < thermal.Count; i++)
            {
                if (thermal.Active[i] && thermal.Formed[i]) bead.DepositCell(i);
                if (thermal.Burned[i]) bead.BurnCell(i);
                if (thermal.IsWarning(i)) warningCells++;
            }
            int cell = bead.Coverage.IndexAt(currentMetres);
            currentPenetrationMm = thermal.PenetrationMm[cell];
            burnVisual.Refresh(thermal);
            UpdatePenetrationGraph(false);
        }

        private string ThermalStatus()
        {
            if (thermal == null) return string.Empty;
            int cell = bead.Coverage.IndexAt(currentMetres);
            if (thermal.Burned[cell]) return "ПРОЖОГ — отверстие; участок повреждён";
            if (warningCells > 0) return "ОПАСНОСТЬ ПРОЖОГА\nПереместите инструмент или снизьте мощность";
            if (thermal.BurnedCount > 0) return $"Прожжено {thermal.BurnedCount * bead.Coverage.CellLength * 1000f:F0} мм шва";
            if (thermal.Active[cell] && !thermal.Formed[cell]) return "Нагрев — металл ещё не сформирован";
            return string.Empty;
        }

        private void GetPenetrationStatistics(
            out float averageMm,
            out float minimumMm,
            out float maximumMm,
            out float fullPenetrationPercent)
        {
            averageMm = 0f;
            minimumMm = 0f;
            maximumMm = 0f;
            fullPenetrationPercent = 0f;

            if (penetrationByCellMm == null ||
                bead == null ||
                bead.Coverage == null)
            {
                return;
            }

            float sum = 0f;
            float min = float.PositiveInfinity;
            float max = 0f;
            int count = 0;
            int fullCount = 0;

            float fullThreshold =
                Mathf.Max(materialThicknessMm, 0.01f) * 0.999f;

            for (int i = 0; i < penetrationByCellMm.Length; i++)
            {
                if (!bead.Coverage[i] || thermal.Burned[i])
                    continue;

                float value = penetrationByCellMm[i];
                sum += value;
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);

                if (value >= fullThreshold)
                    fullCount++;

                count++;
            }

            if (count <= 0)
                return;

            averageMm = sum / count;
            minimumMm =
                float.IsPositiveInfinity(min)
                    ? 0f
                    : min;
            maximumMm = max;
            fullPenetrationPercent =
                100f * fullCount / count;
        }

        private void CreatePenetrationGraph()
        {
            if (!showPenetrationGraph ||
                penetrationGraphRoot != null)
            {
                return;
            }

            penetrationGraphRoot =
                new GameObject("Глубина проплавления");

            Transform root =
                penetrationGraphRoot.transform;

            if (penetrationGraphAnchor != null)
            {
                root.SetParent(
                    penetrationGraphAnchor,
                    false);

                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
            }
            else if (statusText != null)
            {
                if (statusText.transform.parent != null)
                {
                    root.SetParent(
                        statusText.transform.parent,
                        true);
                }

                root.position =
                    statusText.transform.position +
                    statusText.transform.rotation *
                    penetrationGraphOffset;

                root.rotation =
                    statusText.transform.rotation;

                root.localScale = Vector3.one;
            }

            penetrationGraphFrame =
                CreatePenetrationGraphLine(
                    "Graph frame",
                    new Color(0.75f, 0.75f, 0.75f, 1f),
                    penetrationGraphLineWidth * 0.65f);

            penetrationGraphLine =
                CreatePenetrationGraphLine(
                    "Tool position",
                    Color.white,
                    penetrationGraphLineWidth);

            CreateGraphBars();
            warningThresholdLine = CreatePenetrationGraphLine("Warning depth", new Color(1, 0.65f, 0.1f), penetrationGraphLineWidth * 0.5f);
            graphLegend = CreatePenetrationGraphText("Legend", TextAnchor.UpperLeft);
            graphLegend.characterSize *= 0.6f;
            graphLegend.text = "Голубой: глубина | Оранжевый: перегрев\nКрасный: отверстие | Белая линия: инструмент";

            penetrationGraphTitle =
                CreatePenetrationGraphText(
                    "Graph title",
                    TextAnchor.LowerLeft);

            penetrationGraphTopLabel =
                CreatePenetrationGraphText(
                    "Graph maximum",
                    TextAnchor.MiddleRight);

            penetrationGraphLeftLabel =
                CreatePenetrationGraphText(
                    "Graph zero",
                    TextAnchor.UpperLeft);

            penetrationGraphRightLabel =
                CreatePenetrationGraphText(
                    "Graph end",
                    TextAnchor.UpperRight);

            float width =
                Mathf.Max(0.05f, penetrationGraphSize.x);

            float height =
                Mathf.Max(0.03f, penetrationGraphSize.y);

            float halfWidth = width * 0.5f;
            float z = 0f;

            warningThresholdLine.positionCount = 2;
            warningThresholdLine.SetPosition(0, new Vector3(-halfWidth, height * thermal.WarningFraction, -0.002f));
            warningThresholdLine.SetPosition(1, new Vector3(halfWidth, height * thermal.WarningFraction, -0.002f));
            graphLegend.transform.localPosition = new Vector3(-halfWidth, -penetrationGraphTextSize * 4, -0.002f);
            var zero = CreatePenetrationGraphText("Depth zero", TextAnchor.MiddleRight);
            zero.text = "0 мм";
            zero.transform.localPosition = new Vector3(-halfWidth - penetrationGraphTextSize, 0, 0);
            var middle = CreatePenetrationGraphText("Depth middle", TextAnchor.MiddleRight);
            middle.text = $"{thermal.Thickness * 0.5f:F1}";
            middle.transform.localPosition = new Vector3(-halfWidth - penetrationGraphTextSize, height * 0.5f, 0);
            var half = CreatePenetrationGraphText("Seam middle", TextAnchor.UpperCenter);
            half.text = "0.5 м";
            half.transform.localPosition = new Vector3(0, -penetrationGraphTextSize * 1.2f, 0);
            var grid = CreatePenetrationGraphLine("Half-depth grid", new Color(0.25f,0.3f,0.35f), penetrationGraphLineWidth * 0.3f);
            grid.positionCount = 2;
            grid.SetPosition(0, new Vector3(-halfWidth, height * 0.5f, 0));
            grid.SetPosition(1, new Vector3(halfWidth, height * 0.5f, 0));
            penetrationGraphFrame.positionCount = 5;
            penetrationGraphFrame.SetPosition(
                0,
                new Vector3(-halfWidth, 0f, z));
            penetrationGraphFrame.SetPosition(
                1,
                new Vector3(halfWidth, 0f, z));
            penetrationGraphFrame.SetPosition(
                2,
                new Vector3(halfWidth, height, z));
            penetrationGraphFrame.SetPosition(
                3,
                new Vector3(-halfWidth, height, z));
            penetrationGraphFrame.SetPosition(
                4,
                new Vector3(-halfWidth, 0f, z));

            penetrationGraphTitle.transform.localPosition =
                new Vector3(
                    -halfWidth,
                    height + penetrationGraphTextSize * 1.7f,
                    0f);

            penetrationGraphTopLabel.transform.localPosition =
                new Vector3(
                    -halfWidth - penetrationGraphTextSize,
                    height,
                    0f);

            penetrationGraphLeftLabel.transform.localPosition =
                new Vector3(
                    -halfWidth,
                    -penetrationGraphTextSize * 1.2f,
                    0f);

            penetrationGraphRightLabel.transform.localPosition =
                new Vector3(
                    halfWidth,
                    -penetrationGraphTextSize * 1.2f,
                    0f);
        }

        private void CreateGraphBars()
        {
            var chart = new GameObject("Depth cells", typeof(MeshFilter), typeof(MeshRenderer));
            chart.transform.SetParent(penetrationGraphRoot.transform, false);
            graphBars = new Mesh { name = "Fusion depth chart" };
            graphBars.MarkDynamic();
            graphVertices = new Vector3[(thermal.Count + 1) * 4];
            graphColors = new Color[graphVertices.Length];
            var indices = new int[(thermal.Count + 1) * 6];
            for (int i = 0; i <= thermal.Count; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t+1] = v+1; indices[t+2] = v+2;
                indices[t+3] = v; indices[t+4] = v+2; indices[t+5] = v+3;
            }
            graphBars.vertices = graphVertices;
            graphBars.triangles = indices;
            chart.GetComponent<MeshFilter>().sharedMesh = graphBars;
            var material = new Material(Resources.Load<Shader>("FusionGraph"));
            ownedRuntimeMaterials.Add(material);
            var renderer = chart.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void SetGraphQuad(int index, float left, float right, float bottom, float top, float z, Color color)
        {
            int v = index * 4;
            graphVertices[v] = new Vector3(left,bottom,z);
            graphVertices[v+1] = new Vector3(left,top,z);
            graphVertices[v+2] = new Vector3(right,top,z);
            graphVertices[v+3] = new Vector3(right,bottom,z);
            for (int j = 0; j < 4; j++) graphColors[v+j] = color;
        }

        private LineRenderer CreatePenetrationGraphLine(
            string objectName,
            Color color,
            float width)
        {
            GameObject lineObject =
                new GameObject(objectName);

            lineObject.transform.SetParent(
                penetrationGraphRoot.transform,
                false);

            LineRenderer line =
                lineObject.AddComponent<LineRenderer>();

            line.useWorldSpace = false;
            line.positionCount = 0;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            Material source = ResolvePenetrationGraphMaterial();

            if (source != null)
            {
                Material material =
                    new Material(source)
                    {
                        name =
                            $"Fusion Penetration {objectName}"
                    };

                ApplyMaterialColor(
                    material,
                    color,
                    true);

                ownedRuntimeMaterials.Add(material);
                line.sharedMaterial = material;
            }

            return line;
        }

        private Material ResolvePenetrationGraphMaterial()
        {
            if (penetrationGraphMaterial != null)
                return penetrationGraphMaterial;

            RenderPipelineAsset pipeline =
                GraphicsSettings.currentRenderPipeline;

            if (pipeline != null &&
                pipeline.defaultLineMaterial != null)
            {
                return pipeline.defaultLineMaterial;
            }

            return ResolveDebugLineMaterial();
        }

        private TextMesh CreatePenetrationGraphText(
            string objectName,
            TextAnchor anchor)
        {
            GameObject textObject =
                new GameObject(objectName);

            textObject.transform.SetParent(
                penetrationGraphRoot.transform,
                false);

            TextMesh text =
                textObject.AddComponent<TextMesh>();

            text.anchor = anchor;
            text.alignment = TextAlignment.Left;
            text.characterSize =
                penetrationGraphTextSize;
            text.fontSize = 32;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;

            if (statusText != null)
            {
                text.font = statusText.font;

                MeshRenderer sourceRenderer =
                    statusText.GetComponent<MeshRenderer>();

                MeshRenderer targetRenderer =
                    text.GetComponent<MeshRenderer>();

                if (sourceRenderer != null &&
                    targetRenderer != null)
                {
                    targetRenderer.sharedMaterial =
                        sourceRenderer.sharedMaterial;
                }
            }

            return text;
        }

        private void UpdatePenetrationGraph(bool force)
        {
            if (!showPenetrationGraph)
            {
                if (penetrationGraphRoot != null)
                    penetrationGraphRoot.SetActive(false);

                return;
            }

            if (penetrationGraphRoot == null)
                CreatePenetrationGraph();

            if (penetrationGraphRoot == null ||
                penetrationGraphLine == null ||
                penetrationByCellMm == null)
            {
                return;
            }

            penetrationGraphRoot.SetActive(true);

            if (!force &&
                Time.unscaledTime <
                nextPenetrationGraphUpdate)
            {
                return;
            }

            nextPenetrationGraphUpdate =
                Time.unscaledTime + 0.08f;

            int count = thermal.Count;
            float width = Mathf.Max(0.05f, penetrationGraphSize.x);
            float height = Mathf.Max(0.03f, penetrationGraphSize.y);
            float halfWidth = width * 0.5f;
            // Separate cells do not interpolate a fictitious depth across untouched gaps.
            for (int i = 0; i < count; i++)
            {
                float left = -halfWidth + width * i / count;
                float right = -halfWidth + width * (i + 1) / count;
                float depth = thermal.PenetrationMm[i] / thermal.Thickness;
                Color color = thermal.Burned[i] ? new Color(1,0.12f,0.08f) :
                    thermal.IsWarning(i) ? new Color(1,0.6f,0.1f) : new Color(0.1f,0.75f,0.95f);
                float top = thermal.Burned[i] ? height * 1.08f : depth * height;
                SetGraphQuad(i, left, right, 0, top, -0.001f, color);
            }
            SetGraphQuad(count, -halfWidth - 0.055f, halfWidth + 0.02f, -0.075f,
                height + 0.045f, 0.006f, new Color(0.025f, 0.035f, 0.05f));
            graphBars.vertices = graphVertices;
            graphBars.colors = graphColors;
            graphBars.RecalculateBounds();
            float cursorX = -halfWidth + width * Mathf.Clamp01(currentMetres / FusionBead.Length);
            penetrationGraphLine.positionCount = 2;
            penetrationGraphLine.SetPosition(0, new Vector3(cursorX, 0, -0.004f));
            penetrationGraphLine.SetPosition(1, new Vector3(cursorX, height * 1.1f, -0.004f));

            if (penetrationGraphTitle != null)
            {
                penetrationGraphTitle.text =
                    $"Глубина / {laserPowerWatts:F0} Вт | Прожоги: {thermal.BurnedCount * bead.Coverage.CellLength * 1000f:F0} мм";
            }

            if (penetrationGraphTopLabel != null)
            {
                penetrationGraphTopLabel.text =
                    $"{materialThicknessMm:F1} мм";
            }

            if (penetrationGraphLeftLabel != null)
                penetrationGraphLeftLabel.text = "0 м";

            if (penetrationGraphRightLabel != null)
                penetrationGraphRightLabel.text = "1 м";
        }

        private void EnsureTipMarker()
        {
            if (!ensureTipMarkerSphere || toolVisual == null)
                return;

            if (hideExistingToolVisualRenderers)
            {
                Renderer[] existingRenderers =
                    toolVisual.GetComponentsInChildren<Renderer>(true);

                foreach (Renderer renderer in existingRenderers)
                    renderer.enabled = false;
            }

            Transform existing = toolVisual.Find("Fusion Tip Marker");

            if (existing != null)
            {
                runtimeTipMarker = existing;
                runtimeTipMarkerRenderer =
                    existing.GetComponent<Renderer>();

                existing.gameObject.SetActive(true);
                return;
            }

            GameObject marker =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            marker.name = "Fusion Tip Marker";
            marker.transform.SetParent(toolVisual, false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale =
                Vector3.one * tipMarkerDiameter;

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            runtimeTipMarker = marker.transform;
            runtimeTipMarkerRenderer =
                marker.GetComponent<Renderer>();

            Material material =
                CreateMarkerMaterial(tipMarkerMaterial, tipMarkerColor);

            if (material != null)
                runtimeTipMarkerRenderer.sharedMaterial = material;
        }

        private void CreateControllerDebugAxes()
        {
            if (debugAxesRoot != null)
                return;

            debugAxesRoot =
                new GameObject("XR Controller Debug Axes");

            debugControllerOrigin =
                CreateDebugOriginSphere();

            debugControllerOrigin.SetParent(
                debugAxesRoot.transform,
                false);

            debugAxisX = CreateDebugLine(
                "X Axis",
                Color.red);

            debugAxisY = CreateDebugLine(
                "Y Axis",
                Color.green);

            debugAxisZ = CreateDebugLine(
                "Z Axis",
                Color.blue);

            debugTipLine = CreateDebugLine(
                "Controller To Tip",
                Color.yellow);
        }

        private LineRenderer CreateDebugLine(
            string objectName,
            Color color)
        {
            GameObject lineObject =
                new GameObject(objectName);

            lineObject.transform.SetParent(
                debugAxesRoot.transform,
                false);

            LineRenderer line =
                lineObject.AddComponent<LineRenderer>();

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = debugAxisThickness;
            line.endWidth = debugAxisThickness;
            line.startColor = color;
            line.endColor = color;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            Material source = ResolveDebugLineMaterial();

            if (source != null)
            {
                Material material =
                    new Material(source)
                    {
                        name = $"Fusion Debug {objectName}"
                    };

                ApplyMaterialColor(material, color, true);
                ownedRuntimeMaterials.Add(material);
                line.sharedMaterial = material;
            }

            return line;
        }

        private Transform CreateDebugOriginSphere()
        {
            GameObject sphere =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            sphere.name = "Controller Origin";
            sphere.transform.localScale =
                Vector3.one * 0.014f;

            Collider collider =
                sphere.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            debugOriginRenderer =
                sphere.GetComponent<Renderer>();

            Material material =
                CreateMarkerMaterial(null, Color.white);

            if (material != null)
                debugOriginRenderer.sharedMaterial = material;

            return sphere.transform;
        }

        private Material ResolveDebugLineMaterial()
        {
            if (debugAxisMaterial != null)
                return debugAxisMaterial;

            RenderPipelineAsset pipeline =
                GraphicsSettings.currentRenderPipeline;

            if (pipeline != null &&
                pipeline.defaultLineMaterial != null)
            {
                return pipeline.defaultLineMaterial;
            }

            Shader shader =
                Shader.Find("Sprites/Default");

            if (shader != null)
            {
                Material fallback =
                    new Material(shader)
                    {
                        name = "Fusion Debug Line Fallback"
                    };

                ownedRuntimeMaterials.Add(fallback);
                return fallback;
            }

            Debug.LogError(
                "Fusion demo: no renderable line material is available for XR debug axes.",
                this);

            return null;
        }

        private Material CreateMarkerMaterial(
            Material explicitMaterial,
            Color color)
        {
            Material source = explicitMaterial;

            if (source == null)
            {
                RenderPipelineAsset pipeline =
                    GraphicsSettings.currentRenderPipeline;

                if (pipeline != null)
                    source = pipeline.defaultMaterial;
            }

            if (source == null)
            {
                Renderer sourceRenderer =
                    bead != null
                        ? bead.GetComponent<Renderer>()
                        : null;

                if (sourceRenderer != null)
                    source = sourceRenderer.sharedMaterial;
            }

            if (source == null)
            {
                Debug.LogError(
                    "Fusion demo: no material is available for the tip/debug spheres.",
                    this);

                return null;
            }

            Material material =
                new Material(source)
                {
                    name = "Fusion Runtime Marker"
                };

            ApplyMaterialColor(material, color, true);
            ownedRuntimeMaterials.Add(material);

            return material;
        }

        private static void ApplyMaterialColor(
            Material material,
            Color color,
            bool emission)
        {
            if (material == null)
                return;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            if (emission &&
                material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(
                    "_EmissionColor",
                    color * 1.5f);
            }
        }

        private void SetDebugLine(
            LineRenderer line,
            Vector3 start,
            Vector3 end,
            float thickness)
        {
            if (line == null)
                return;

            float length =
                Vector3.Distance(start, end);

            if (length < 0.0001f)
            {
                line.gameObject.SetActive(false);
                return;
            }

            line.gameObject.SetActive(true);
            line.startWidth = thickness;
            line.endWidth = thickness;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private void UpdateControllerDebugAxes(
            Vector3 controllerWorld,
            Quaternion controllerRotation,
            Vector3 calculatedTipWorld)
        {
            if (debugAxesRoot == null)
                CreateControllerDebugAxes();

            debugAxesRoot.SetActive(true);

            debugControllerOrigin.position =
                controllerWorld;

            debugControllerOrigin.rotation =
                controllerRotation;

            Vector3 xEnd =
                controllerWorld +
                controllerRotation *
                Vector3.right *
                debugAxisLength;

            Vector3 yEnd =
                controllerWorld +
                controllerRotation *
                Vector3.up *
                debugAxisLength;

            Vector3 zEnd =
                controllerWorld +
                controllerRotation *
                Vector3.forward *
                debugAxisLength;

            SetDebugLine(
                debugAxisX,
                controllerWorld,
                xEnd,
                debugAxisThickness);

            SetDebugLine(
                debugAxisY,
                controllerWorld,
                yEnd,
                debugAxisThickness);

            SetDebugLine(
                debugAxisZ,
                controllerWorld,
                zEnd,
                debugAxisThickness);

            SetDebugLine(
                debugTipLine,
                controllerWorld,
                calculatedTipWorld,
                debugAxisThickness * 1.35f);
        }

        private void SetDebugAxesVisible(bool visible)
        {
            if (debugAxesRoot != null)
                debugAxesRoot.SetActive(visible);
        }

    }
}
