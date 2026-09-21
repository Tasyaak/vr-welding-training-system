# Quest-only laser-welding training MVP

## Purpose

The system is a standalone Meta Quest 3 mixed-reality training aid for handheld
laser-welding techniques. A right Touch Plus controller is rigidly attached to
a non-functional mock tool. The application overlays guidance on a stationary
physical fixture, evaluates motion locally and records deterministic sessions.

It does not control a real laser and is not a physical safety interlock.

## Approved equipment and interaction

- Meta Quest 3 with passthrough;
- one right Touch Plus controller and mock tool attachment;
- stationary fixture/workpiece with four known calibration points;
- virtual clamp and explicit virtual nozzle selection;
- right-controller MR menu and software emergency stop.

No runtime path uses QR markers, external microcontrollers, sensors, magnets,
coils, network telemetry, a physical clamp, or the left controller.

## Training modes

1. Fusion welding on a shared directed seam evaluator.
2. Wobble welding with a time-based scan and footprint coverage.
3. Pulsed welding with deterministic ON/OFF windows.
4. Pre-weld cleaning with explicit cleaning-nozzle selection.
5. Post-weld cleaning with explicit cleaning-nozzle selection.

All modes share calibrated local geometry, tracking validity, finite-surface
contact, coverage, reflection-risk evaluation, E-stop and recording.

## Safety model

Simulated process activation is allowed only when the session is running,
calibration and tracking are valid, the selected virtual nozzle/process matches,
the virtual clamp is applied, contact and process conditions are acceptable,
the trigger is intentionally pressed, E-stop is clear and no latched reflection
fault exists. Any missing condition fails closed with an explicit reason.

Recovery requires stable state, trigger release and explicit re-arm. E-stop and
defined reflection faults latch until their reset workflow succeeds.

## Geometry and data

Content is versioned: fixture points, finite surfaces, seams, normals, tool/tip
offsets, nozzle profiles and process parameters. Evaluation runs in
fixture/workpiece-local coordinates using metres, seconds and radians. Inspector
and trainee UI may display degrees only through explicit conversion.

Each recording includes content/evaluator versions, monotonic inputs, state
transitions, calibration generation/residuals, process timing, coverage,
interlock reasons and summary metrics. Replay must reproduce decisions without
Unity scene-object identity or participant personal data.

Authoring uses one selected versioned catalog entry containing exactly four
labeled fixture points, finite polygonal surface patches, baked directed seams,
cleaning regions, a right-controller tool definition and five discriminated
process profiles. Catalog baking rejects unsupported scale and invalid geometry;
attempt startup freezes a deep immutable snapshot and deterministic hashes.

## Failure behavior

- tracking/calibration/contact loss: inhibit immediately;
- recenter/resume: revalidate the same session anchor before continuing;
- E-stop: latch, stop feedback/activation and require reset plus re-arm;
- reflection risk: conservatively inhibit and latch when specified;
- storage pressure/crash: mark incomplete data and preserve committed records;
- missing optional feedback sink: continue scoring, report degraded feedback;
- missing mandatory provider: do not start or arm the process.

## Delivery state

`main` currently contains the presentation prototype and Quest scene. Production
features are implemented in order by Issues #46–#58. Historical external-device
design is intentionally retired; see `quest-only-migration.md`.

# Production session and process flow

The standalone Quest runtime uses one authoritative application coordinator for all five
process modes. Registration is accepted before content selection; each attempt freezes one
versioned content/profile snapshot; nozzle and clamp preparation are explicit; and activation
is possible only while all tracking, registration, recording, and safety inputs are valid.
Loss of those inputs suspends or inhibits the attempt without synthesizing missed poses.

The normal flow is registration, seam/process selection, preparation, arm, running,
save/review, then a distinct retry/new attempt or session completion. Emergency stop dominates
commands at the same timestamp and immediately stops haptics. Prototype evaluation is disabled
whenever the production composition root is enabled.
