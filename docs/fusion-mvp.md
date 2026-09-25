# Fusion presentation MVP

Base: `74098a9541eb529fd8a9a182f76157373f6a0140` (main, PR #88).
Scene: `Assets/Trainer/Scenes/FusionMvp.unity`, the sole enabled build scene.
Unity: 6000.3.24f1. Existing package versions are unchanged.

## Device flow

1. Scan the installed fixture QR **B**, `LW1:PART-001`, 63 mm symbol excluding
   quiet zone. During Preview, verify the actual part, fastening and seam overlay;
   right A explicitly confirms all three. The existing registration runtime owns
   QR acquisition, evidence, and the unsaved anchor.
2. The authored `PART-001-T-JOINT` (184.746 mm, original direction) is selected.
   A selects the welding nozzle; another A connects the virtual clamp.
3. Place the calibrated tip at green START, release the trigger and press A to
   arm. Hold the right trigger and follow the seam. Target speed is 15–35 mm/s;
   lateral/normal tolerances are each 4 mm. Work angle is relative to the authored
   T-joint normal bisector; travel angle retains the existing evaluator's meaning
   (motion direction versus seam tangent), not an invented torch-lead metric.
4. Release to stop depositing. Right B is the existing latched emergency stop.
   After an interruption, release, acknowledge/reset if necessary, resume with A,
   and explicitly re-arm with A. There is no automatic output resumption.
5. Right stick click finishes the attempt and displays results. A fully acceptable
   contiguous pass can finish automatically at the end gate. Touching END alone
   is insufficient. A from results creates a new attempt with a fresh clamp step.

The display presents the authoritative block reason, prioritized coaching,
speed, path error, travel/work angles, attempted and acceptable coverage. Start
and end markers and a thin seam guide leave the physical part visible. The tip
line uses the existing calibrated right-tool poses. Active Fusion produces sparks
and a scalloped metal bead that cools; poor intervals remain bronze. These are
training visuals, with no thermal, penetration or metallurgy claim.

Assistance is one 0–100 inspector value, affecting optional feedback strength
and cadence. It does not enter evaluator, interlock, profile, coverage or metrics.
Mandatory safety/operational messages remain visible at zero assistance. Audio
uses two pre-created short tone clips and one source. A single right-controller
haptic owner uses installed Meta Core vibration, finite pulses and lifecycle stop.

## Station configuration and evidence

`fusion-mvp.spatial` is a separate station catalog. The original `supplied.spatial`
remains unchanged. On 2026-09-25 the owner confirmed that installed QR B (63 mm
excluding quiet zone) had successfully exercised PR #84 and #87, and supplied:

```
Android OS 14 / API-34 (UP1A.231005.007.A1/52433670036000520) | Oculus Quest 3 | OVRPlugin 1.205.0 | Core/MRUK 205.0.0 | OpenXR 1.16.1 | RH-Sz/MRUK-Sx v1
```

`Quest3QrB.asset` matches exactly this tuple. The station catalog records the
owner's functional confirmation, selects B, and includes the existing print
specification's 0.1 mm label thickness in the marker plane (fixture Y=0.0077 m,
instead of the recess Y=0.0076 m). Other fixture/workpiece poses are unchanged.
This is **owner-attested functional qualification**, not new measured station
accuracy, repeatability or a completed #66 error-budget exercise. Recheck overlay
alignment on the physical fixture before recording; another OS/SDK tuple fails
closed until its evidence is supplied. Tool calibration is the file merged in #88.

## Current A/B integration

`FusionMvpComposition` captures `RightControllerSource` and
`QuestRegistrationBridge` once per frame, and feeds cached input/registration
ports into the existing `TrainingCoordinator`. Independent A origin counters
are bound as a pair once; a change on either side invalidates the registration.
Evaluation uses immutable RH Workpiece coordinates from the registered pose,
not scene transforms. Presentation converts RH to Unity exactly once.

The original authored primary face's triangles provide finite contact evidence;
shared coplanar triangle edges are treated as one support. Incident tool -Z,
contact normal and tracked head produce a conservative reflected-direction cone
(15 degrees plus 120 mm head radius). Unknown/unsafe evidence inhibits through
the existing reducer; this is a simplified training risk check, not laser safety
certification. The selected T-joint bisector supplies the work-angle reference.

`WeldPathEvaluator` remains measurement owner. `TrainingCoordinator` /
`ActivationReducer` remain activation owner. `FusionProcessKernel` consumes their
results and the existing `SeamCoverageTracker`; it adds start/end gates, epochs,
interval continuity and quality attribution. Release/invalidation edges close the
Fusion segment, including release/repress between captured frames. The retained
coverage union preserves gaps and worst prior quality on revisits.

## Results and local files

Results show attempted/acceptable/missed millimetres from `CoverageSnapshot`,
active/blocked/invalid seconds, mean/max positional error, speed-in-range percent,
and mean travel/work errors. Active errors use elapsed time and separate validity
denominators; unavailable measurements display `--`. Reverse duration and mean
speed are also saved. A finish is distinct from a successful completed pass.

Files are under `Application.persistentDataPath/FusionSessions/<attempt-id>/`:

- `samples.jsonl`: per-frame explicit validity, poses in RH Workpiece coordinates,
  input/registration/origin IDs, trigger, activation/block reasons, actual metrics,
  semantic feedback, assistance, profile/content IDs and coverage deltas/epochs;
- `summary.json`: the same derived result shown in the HUD, with session/attempt
  IDs, content/profile identity and recording/completion/interruption flags.

The recording is buffered and flushed once per second. `.partial` files remain
for interrupted/crashed writes; summary is renamed last. Failure marks the
recorder unavailable and inhibits via the coordinator. Attempts have a ten-minute
limit. No automatic file deletion; copy or remove completed attempts manually
over USB/ADB. Summary persistence is not deterministic offline replay certification.

## Historical sources and deliberate reductions

- **#71:** adapted `SemanticFeedbackEngine` and immutable cue state. Removed the
  incorrect equality assumption between input-context and registration counters;
  mapped current reflection/contact flags and made specific causes outrank the
  generic blocked message.
- **#81:** adapted `FeedbackDeliveryCoordinator` and sink interfaces, including
  finite delivery/cooldowns and transient stops. Built a readable live/results
  panel and concrete right-Core haptics instead of importing its legacy presenter
  (whose progress line could suggest coverage through gaps).
- **#72:** adapted the time-weighted metrics and explicit partial/final file
  concepts. Rewrote the small MVP recorder and statistics for current path and
  coverage contracts. Did not import its worker/retention/replay implementation
  or its old generation/completion assumptions.
- **#76:** reused/adapted Fusion profile, kernel, epoch and coverage-delta types;
  added finite profile validation and current attempt/seam identity checks.
  No stacked coordinator, evaluator, interlock or coverage versions were copied.
- **demo/FusionDemo:** reused `FusionSparks`, Fusion Metal and Fusion Spark
  shaders; adapted the scalloped crown/cooling visual into `FusionBeadView`.
  Removed origin-based vertex scaling and added persistent poor-region tint.
  No old demo controls, coverage authority, overheating/burn-through simulation,
  demo scene ancestry or generated build directories were imported.

Deferred: other process modes, menu redesign, full angle widgets, configurable
audio/haptic libraries, recording worker queues/retention/checksums/recovery/replay,
comprehensive timing matrices, complete fault-history analytics and physical
Quest performance/accuracy acceptance. This PR does not close #14/#15/#17/#53.

## Small verification workflow

`Welding Trainer > Rehearse Fusion MVP` opens the same scene in Play Mode with
clearly labelled Editor-only synthetic registration/tool input and real authored
CAD, then writes welding/results PNGs and the derived summary to `artifacts`.
Synthetic input and the rehearsal driver are excluded from the Quest player.
The three `FusionMvpTests` cover stopped-gap attribution/end-touch, assistance
versus independent generations, and valid-time statistics. Actual checks and
limitations are recorded in the PR; do not treat desktop rehearsal as Quest proof.

### Verification performed on 2026-09-25

- Unity Editor compilation and scene generation: PASS.
- Three targeted EditMode tests: 3/3 PASS.
- Existing repository checks: PASS (48 spatial, 77 registration, 14 tool checks).
- Final Play Mode rehearsal: PASS; 184.08/184.75 mm attempted, 171.66 mm
  acceptable, 7.40 s active, 92.80% speed-in-range, files saved. Capture/render
  stalls remain visible as 0.85 s invalid data; the run correctly did not claim
  a fully acceptable completed pass.
- Rendered [welding](fusion-mvp-evidence/fusion-welding.png) and
  [results](fusion-mvp-evidence/fusion-results.png) inspected after fixing HUD
  size/framing. No project runtime exception in the final rehearsal.
- ADB: no connected device. No new Quest run, APK build, device audio/haptic,
  stereo, performance or registration-accuracy PASS is claimed. Android build
  entry point is `FusionMvpBuilder.BuildAndroid`; device acceptance remains next.
