# Handheld laser-welding training MVP

## Purpose and implementation status

A standalone Quest 3 MR application trains tool movement, process preparation
and response to simulated faults. A right Touch Plus is rigidly mounted in a
non-functional handheld laser-head mock-up. Passthrough shows the real stationary
fixture and a welding part bolted to it.

Audited main ee8999e contains the presentation prototype and XR scene, not the
production feature set described here. Existing unmerged PRs are not inputs or
dependencies of this revised implementation plan.

## Registration and mechanical setup

A QR code is fixed at an authored position/orientation on the fixture and carries
the welding-part ID. A bounded local catalog binding selects the fixture, part,
mounting revision, printed marker dimensions and rigid offsets. Marker pose plus
those offsets establishes the part pose; the ID alone does not.

The part is mechanically secured with bolts. Before accepting registration the
trainee confirms the actual part/revision and secured mounting, and checks the
ghost model. The system does not sense bolt tightness or authenticate part
identity. Mechanical fastening is separate from the virtual electrical clamp.

Use repeated stable MRUK observations, known marker dimensions, explicit
quality/age gates and an unsaved session anchor. QR occlusion after successful
registration is allowed while the anchor stays valid. Do not continually snap
the workpiece to noisy QR poses. Changing/rebolting the part, moving the fixture
or relocating the marker requires invalidation and a new registration/attempt.
Every new session registers again.

Four-point touch registration is superseded. Tool-to-tip calibration remains
an independent physical setup requirement. See [CAD/registration](fixture-cad-and-registration.md)
for source-model evidence, unknown installation parameters and qualification.

## Modes and shared semantics

| Mode | Training behavior |
| --- | --- |
| Fusion | Continuous output during permitted trigger intervals |
| Wobble | Tracked center path plus timestamp-based lateral beam scan; render an envelope when appropriate |
| Pulsed | Deterministic half-open ON windows; OFF travel contributes no output |
| Pre-weld cleaning | Sweep authored contamination/required regions; track acceptable and attempted area |
| Post-weld cleaning | Sweep a frozen weld-derived or explicitly authored target area |

No mode predicts penetration, metallurgy, temperature or real optical power.
Wobble does not ask the trainee to zigzag and does not forgive centerline errors.
Pulse/wobble semantics depend on monotonic time, not rendering FPS. Cleaning
keeps missed regions, overlap/repeated passes, speed and pose constraints.

Welding modes require WeldingNozzle; cleaning requires CleaningNozzle.
Changing nozzle is an explicit disarmed virtual transaction, not physical
recognition and not a change to the calibrated real tip offset. Process/profile
changes require a new frozen attempt configuration.

## Activation, safety and controls

One application/domain decision combines session state, registration and
assembly confirmation, current tool/head/system validity, trigger, virtual
clamp, finite-surface geometric contact, mode/nozzle compatibility, E-stop,
reflection state and recording health. Unknown required input inhibits.
Signed standoff, permitted approach side, finite bounds and real normals define
contact; distance to an infinite plane or seam alone is insufficient.

Right trigger requests output; B is a short-press global latched E-stop; A
confirms UI/registration preview; thumbstick navigates and its click requests
menu/pause. Grip is reserved. The left controller is unnecessary. Opening menu
inhibits/disarms before input focus changes. Closing it never resumes held-trigger
output. Release, valid conditions and explicit re-arm are required; faults also
need acknowledgement/reset.

Back-reflection uses incident direction and the real surface normal with
r=d-2(d dot n)n, a conservative training cone and tracked head danger volume.
Unknown is not Low/Safe; confirmed trips latch. Risk parameters are training
approximations, not measured reflectivity or real-world Class 4 safety approval.

## Evaluation, feedback and evidence

Evaluate directed seam progress, signed along-seam speed, lateral/normal error,
orientation and continuity in Workpiece coordinates. Separate attempted and
acceptable coverage; never bridge missing/blocked intervals or double-count
revisits. Keep cleaning area and weld-length denominators distinct.

One semantic state drives visuals/audio/right-controller haptics. Assistance
0–100% affects coaching only; mandatory status and objective metrics remain.
Presentation never determines whether simulated output is allowed.

Record immutable content/profile versions, marker/assembly binding, registration
generation and observation quality, head/tool/trigger validity, ordered commands,
faults, pulse/wobble epochs, coverage and results. Use bounded local journals,
crash-aware finalization and explicit incomplete state on evidence loss.
Replay must reproduce model decisions from the consumed timestamped inputs;
display-rate samples alone are insufficient.

## Delivery and limits

[Roadmap](roadmap.md) separates independently implementable A/B groups and #58
integration using [contract v1](parallel-development-contract.md). A uses the
provided STEP references; B uses synthetic geometry and fake ports. Neither
group's merge requires the other group's implementation.

No ESP32/Hall/magnets/coil/external network peer, sensed physical electrical
clamp, second controller, account/backend or commercial panel copy is included.
The application is a training aid and never controls a real laser.
