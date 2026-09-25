# Right Touch Plus tool adapter — Group A / contract v1

`Platform/Meta/Tool/Unity/RightControllerSource` is the sole input owner in the
standalone tool composition. It uses Input System over OpenXR, explicitly binds
RightHand grip (`devicePosition`/`deviceRotation`, **not aim**), analog trigger,
B, A and thumbstick, and captures HMD center-eye pose. No left/hand fallback.
Installed OpenXR 1.16.1 Touch Plus and Oculus Touch layouts expose these controls.
The current project enables Oculus Touch; explicit Touch Plus remains disabled.
The actual right-hand interaction profile is read from the runtime and recorded,
not inferred from that setting. Qualify the profile actually observed on Quest.

The current Bootstrap still contains the isolated prototype; replacing Bootstrap
belongs to #58. Do not combine it, FusionDemo, or RegistrationPreview's legacy
smoke controls with the tool composition. This change does not claim that the
existing Bootstrap is now production-ready. Generic Player/UI XR bindings were
removed, including trigger/Click and B/Jump. Desktop/gamepad bindings remain.

## Boundary and capture

`Capture()` is called by **one** consumer after Input System updates (standalone:
`ToolQualificationView.LateUpdate`). Visual consumers read `LastSnapshot` instead
of draining input again. `ToolHeadSnapshot` is immutable and schema version 1:

- Session identity, sequence, monotonic capture seconds, origin generation,
  input-context generation, focus/pause, device availability, active profile.
- Separate Controller, Tool, Tip, Head position/rotation validity, nullable
  components, nullable rigid pose, and failure flags. Missing rotation prevents
  placing any tool offset, even when controller position is available.
- Actual trigger analog (not force), hysteresis state and queued command records
  with sequence, callback capture seconds and context generation. A short pulse
  can produce both pressed and released commands in one capture. B produces only
  `EStopPressed`, survives context changes, and suppresses process request in its
  capture. The persistent safety latch and explicit arming belong to Group B.
- Full immutable calibration including physical assembly ID, revision/content
  hash, actual interaction profile, evidence and both named rigid transforms.

Times are `Time.realtimeSinceStartupAsDouble`, not UTC; `SourceSeconds=null`:
this adapter does not have a documented hardware measurement timestamp. Input
events witness component freshness; button-only events cannot refresh pose.
Stationary poses still receive full OpenXR state updates. Default freshness
budget is explicitly 0.1 s. No updates, absent flags, untracked device, invalid
quaternion, missing origin or non-unit scale produce unavailable measurements.

`IsUsableAt(now, origin)` additionally requires physical qualification and a
matching runtime profile. Unqualified candidate poses exist **only for visible
alignment**; they must not be accepted for process evaluation. Head validity is
separate and must also be required by #58. Never use `Pose.HasValue` alone as the
process permission check. No A component grants activation.

Pause/focus transitions, subsystem recenter and tracking-space changes invalidate
old evidence. Recovery requires new input and a released trigger. Commands already
captured are retained. A disabled source returns invalid snapshots, not a cached
valid pose. #58 must route a common origin-change notification to registration and
tool (`NotifyOriginDiscontinuity`) and validate session identity; equal numbers
from two independently started generation counters do not prove coherent origins.

Training/Registration/Menu contexts always retain safety. ProcessRequested is
false outside Training. Menu click inhibits immediately; closing a menu requires
release and Group B's explicit re-arm. Raw trigger edges remain evidence, not
permission to activate. A is Submit; navigation is available to the standalone
view. Full UI workflow and navigation command routing are deferred to #58.

The common `Content/Spatial/Unity/UnitySpatialPose` implements the existing
`S_z` vector / `S_z R S_z` rotation basis change. Registration forwards to this
same implementation. No Unity vector or Euler adjustment bypasses this boundary.

## What the supplied STEP establishes

Inspected with OCCT B-rep validation and tessellated views, without changing CAD.
Source: `controller_attachment.step`, SHA-256
`3ef01a87a06bcc5366b3dc441b3ede4fc51ae2aee63d9536b30f24510b8442a6`.
SolidWorks AP214 explicitly declares millimetres; one valid solid, 41 faces.
Bounds in original CAD millimetres:
X [-51.334831, 29.872045], Y [10.444601, 116.745047],
Z [-40.469939, 40.814586].

The shaft has radius 3.1 mm; its shoulder has radius 8.1 mm. The shaft axis
**toward the body** is approximately (0.575887601, -0.817528881, 0).
Shoulder centre is (-26.158403935, 81.159333603, 0.201350197) mm;
distal cap centre is (-49.193907967, 113.860488843, 0.201350197) mm.
Their axial separation is 40 mm. Small conical/end features extend past this
cap, so the cap centre is **not a certified effective working/contact point**.
The actual working end must be selected and aligned on the physical part.

A reproducible *authored* Tool convention is origin at the shoulder centre,
+Z along the shaft toward the body, +Y along CAD +Z, +X=Y cross Z. The oblique
export coordinates are preserved; they are not the controller frame. A visible
asymmetric feature of the broad attachment must identify the physical +Y/roll
reference. A round shaft alone cannot determine roll.

The draft's `ToolFromTip=(0,0,-0.040 m), identity rotation` is an explicit
**nominal cap candidate**, not glue calibration. Both its position and rotation
are present in the JSON. Device controls adjust axial length; other geometric
changes can be entered explicitly in JSON and reviewed before loading.
The STEP has no measured ControllerFromTool, controller model or glue layer.
Nothing here derives such a transform from CAD placement.

FusionDemo was read from `demo/FusionDemo`, without switching branches. Its
source default tip offset is Unity (-0.12,0,0), but the actual `FusionQuest.unity`
scene serializes **(0,-0.12,+0.12) m**, identity Euler offset, with debug axes
disabled by default. The scene direction is the more useful empirical reference:
down and forward in Unity controller coordinates (RH: 0,-0.12,-0.12).
Neither value is a calibration seed or evidence for the new glued assembly.

## Calibration on Quest 3

1. In Unity choose **Welding Trainer → Tool → Create standalone qualification
   scene**. Record the generated `Tool qualification only / physicalAssemblyId`
   on this specific glued controller/attachment, or replace it with your unique
   assembly ID. It is a local configuration label, not a hardware serial. Save
   the scene. Build using
   **Build standalone Quest qualification APK**. It builds only this scene with
   Development enabled, without changing production Build Settings. Install the
   resulting `artifacts/tool-qualification.apk`; switch the left controller off.
2. Hold the right controller where passthrough clearly shows the attachment.
   White marks controller origin. Long RGB lines show **RH** controller XYZ:
   the blue +Z line is opposite the Unity +Z line in FusionDemo. Small RGB lines
   show Tool; cyan is incident direction (-Tool Z); yellow marks the effective
   tip and rigid connections. Compare corresponding physical directions, not
   identical sign labels across the two encodings.
3. **A cycles parameters. Stick left/right adjusts**, normally 1 mm or 1 degree;
   hold stick upward while moving sideways for 0.1 mm/degree increments. First
   adjust Tip XYZ in RH Controller metres (UI shows mm) until the marker matches
   the chosen physical working point. Then adjust all three Tool rotations,
   including roll, until +Z points back along the shaft and +Y matches the chosen
   asymmetric reference. Rotation increments are about named RH Controller axes;
   the persisted value is a quaternion, not ambiguous Euler angles.
4. Set shoulder-to-effective-tip distance. The implementation derives
   `ControllerFromTool.position = controllerTip - rotation * ToolFromTip.position`.
   Thus adjusting orientation does not move the already calibrated effective tip;
   adjusting tip position does not change orientation. The final composition is
   exactly `WorldFromController * ControllerFromTool * ToolFromTip`.
5. Check several wrist rotations around **all three axes**, different positions
   and viewing directions. Use a physical ruler/marked reference for positional
   error, and an orientation reference (including roll) for angular error; record the largest
   observed deviations conservatively. Confirm the virtual Tool origin also
   matches the intended shoulder and the roll reference remains correct.
   If alignment is view-dependent, investigate tracking/passthrough/choice of
   working point; do not compensate it with another presentation offset.
6. Enter observed tip/full-orientation error bounds. Default acceptance gates are explicit
   3 mm / 3 degrees, **not measured Quest accuracy**. They are stored as
   `acceptedTipErrorMm` / `acceptedOrientationErrorDegrees` and can be deliberately set
   in the draft JSON before qualification. No measured error may exceed its gate.
   Zero/unknown error does not qualify. Select SAVE DRAFT or QUALIFY + SAVE with
   A, then hold stick UP for two seconds. The latter explicitly attests that the
   checks above were performed. B remains E-stop and interrupts editing for that
   capture; it never saves, resets or changes calibration.
7. The JSON is saved to `Application.persistentDataPath/right-tool-calibration.json`
   with a previous-file backup; its path, values and SHA-256 are logged. Restart
   and verify the same values and physical overlay. A mismatched assembly ID is
   rejected; a different interaction profile is unusable until requalified.
   Any parameter edit immediately clears qualification. Re-gluing or changing
   the physical tip requires a new assembly/configuration revision and checking
   the full pose again.

Retrieve the saved JSON via ADB from the logged path, keep it with the physical
assembly's qualification record, and import it as a TextAsset for the source's
`calibrationJson` field in #58. The serialized geometry, quaternion, revision,
hash and evidence are sufficient to reproduce the transform. Do not copy only
the tip Vector3. A development snapshot never silently becomes approved content.

## Hardware acceptance still required

With left controller off: verify rigid tip/body/roll alignment through wrist
motion, compare physical direction to FusionDemo, test occlusion and loss of
tracking (overlay disappears, output invalid), reconnect, focus loss, pause/resume
and recenter. Recovery must need new state and must not restart held-trigger
intent. Check analog range/hysteresis, short press/release and short B pulses in
the logs, including B concurrent with trigger. Record the actual runtime profile,
device/runtime versions, calibration JSON/hash, measurement setup and error
bounds. Re-run after a profile/runtime/physical assembly change.

Desktop tests prove rigid composition, conversions, validity and capture behavior;
they do not measure the glued assembly, tracking accuracy or passthrough alignment.
No scoring, contact, registration, mode kernel, renderer, haptic routing, full menu
or training-coordinator/Bootstrap integration is implemented here.
