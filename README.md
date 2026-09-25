# VR Welding Training System

Standalone Meta Quest 3 mixed-reality training for handheld laser welding.

## Approved MVP

One right Touch Plus controller is rigidly attached to a non-functional mock
tool. A welding part is bolted to a stationary fixture. A QR code carrying the
workpiece ID is fixed at a documented location and orientation on that fixture.
Its tracked pose and versioned marker/assembly offsets establish the workpiece
frame; decoding an ID alone does not establish a pose.

The application supports Fusion, Wobble, Pulsed, pre-weld cleaning and post-weld
cleaning, with a virtual nozzle change, virtual workpiece-clamp state, geometric
contact, a right-button software E-stop, conservative reflection-risk training,
semantic MR/audio/right-controller haptics and deterministic local recording.

Fixture registration uses QR observations and a session-only anchor. The former
four-point touch registration is superseded; no touch-calibration fallback is
required. Calibrating the tool's fixed tip offset remains a separate setup task.
Bolts secure the assembly mechanically; they are not the simulated electrical
clamp/contact circuit and are not automatically sensed.

No ESP32, Hall sensor, magnets, coil, external runtime network peer, physical
electrical clamp, left-controller workflow or copied commercial-machine UI is
part of the MVP. Training works offline after installation.

## Current implementation

Spatial implementation baseline: `24ff28d75b1a2903fb5716cbf4be6e7e52a2f119`.

The integrated code is `unity/Assets/Trainer/Development/Prototype/PrototypeWeldingDemo.cs`
and the Bootstrap scene with OVRCameraRig, MRUK and passthrough. The prototype
uses a camera-relative straight seam and does not implement production QR
registration, process state or persistence. Group A content now lives in
`Content/Spatial`: supplied CAD, deterministic conversion, finite surfaces,
QR recess, versioned assembly catalog and validation. The supplied content is
unqualified for scoring: two known QR print candidates await selection and
physical installation qualification under #66. The owner-selected directed
T-joint and finite pre/post cleaning bands are now authored. #49 supplies an
independent [runtime QR registration workflow](docs/qr-registration.md), Meta
adapters and standalone preview. Final training composition remains #58.

## Parallel delivery

[Roadmap](docs/roadmap.md) defines two independent implementation groups:

- **A — Spatial setup:** #46 content/CAD, #48 right input, #49 QR registration,
  and #66 station qualification.
- **B — Training engine and experience:** #47 state/contracts, #10 evaluation,
  #50–#56 process/safety, #14–#17 feedback/coverage/storage, and #57 menu.
- **#58 Integration** joins A and B and qualifies the complete Quest workflow.

Both groups start from the [boundary contract](docs/parallel-development-contract.md)
and their own test fixtures; B does not wait for live QR or the actual CAD assets.

## Open and validate

Use Unity `6000.3.24f1`, Android Build Support, Meta Core/MRUK `205.0.0`
and OpenXR `1.16.1`. Open the existing `unity` directory in Unity Hub,
then `Assets/Trainer/Scenes/FusionMvp.unity` for the integrated Fusion presentation.
See the [MVP flow, station configuration and verification limits](docs/fusion-mvp.md).
Keep package versions pinned.

Run `python tests/repository_checks.py`; follow [setup](docs/setup.md)
for Unity and standalone Quest validation. Use the separate RegistrationPreview
scene for #49; Bootstrap remains unchanged pending #58.

## Architecture and evidence

- [Project description](docs/laser-welding-training-project-description.md)
- [Architecture](docs/architecture.md) and [coordinate conventions](docs/coordinate-conventions.md)
- [CAD and fixture registration](docs/fixture-cad-and-registration.md)
- [Spatial authoring, reproducible bake and schema](docs/spatial-content-authoring.md)
- [Spatial verification and remaining qualification](docs/spatial-content-verification.md)
- [Migration history](docs/quest-only-migration.md)
- [Prototype runbook](docs/tomorrow-prototype-runbook.md)

This is a training aid, not a real laser controller or a real-world Class 4
laser safety calculation. Unknown tracking, registration, assembly readiness
or safety evidence inhibits simulated output.
