# VR Welding Training System

Standalone Meta Quest 3 mixed-reality training for handheld laser-welding processes.

## Approved MVP

The MVP uses one right Touch Plus controller rigidly attached to a mock tool, a
stationary fixture, four-point fixture calibration, local deterministic
evaluation, virtual nozzle/clamp state, geometric contact, software emergency
stop and reflection-risk training. It supports Fusion, Wobble, Pulsed, and
pre/post-weld cleaning workflows.

The runtime is Quest-only. It has no QR registration, external sensor,
microcontroller, network peer, magnet, electromagnetic actuator, physical clamp,
second-controller workflow, or copied commercial-machine interface.

## Current repository state

- `unity/Assets/Trainer/Development/Prototype/` contains the integrated
  presentation prototype.
- `unity/Assets/Trainer/Scenes/Bootstrap.unity` contains the Quest XR,
  passthrough, MRUK and prototype scene composition.
- `docs/` defines the production architecture and ordered implementation plan.
- Production Domain/Application modules are planned in Issues #46–#58 and are
  not yet integrated into `main`.

Pending pull requests are proposals, not proof that their files are in `main`.
See `docs/quest-only-migration.md` for the Issue/PR adoption matrix.

## Software

- Unity `6000.3.24f1`
- Meta XR Core and MR Utility Kit `205.0.0`
- Unity OpenXR `1.16.1`
- Android Build Support for standalone Quest deployment

Open the existing `unity` directory in Unity Hub; do not create another Unity
project. Then open `Assets/Trainer/Scenes/Bootstrap.unity`.

## Validation

```text
python tests/repository_checks.py
```

Then run Unity compilation and the Quest smoke-test procedure in
`docs/setup.md`. Device results must be recorded as evidence; Editor Play Mode
does not validate passthrough, tracking, permissions, anchors or performance.

## Safety boundary

This is a training aid. It never controls or authorizes a real laser. Software
activation, contact, reflection and emergency-stop states are simulated and
must fail closed when required tracking, calibration or session state is invalid.
