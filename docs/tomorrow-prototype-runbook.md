# Presentation prototype runbook

This is the integrated presentation prototype, not the production architecture.
It demonstrates a right-controller straight-seam exercise in Quest passthrough.
It intentionally has no external device, QR path or production calibration.

## Unity setup

1. Use Unity `6000.3.24f1` and open the repository `unity` directory.
2. Switch to Android and open `Assets/Trainer/Scenes/Bootstrap.unity`.
3. Confirm `OVRCameraRig`, `MRUK`, `OVRPassthroughLayer` and `PrototypeDemo`
   exist with no missing scripts.
4. Set OVR tracking origin to Stage, passthrough support to Supported/Required,
   passthrough layer to Underlay, skybox to None and camera clear alpha to zero.
5. Run Meta Project Setup Tool and resolve blocking Quest findings.
6. Save generated `.meta` changes only; never commit `unity/.utmp`.

## Editor rehearsal

Press Play. Use `J/L`, `I/K`, `U/O` to move, Space to hold the simulated trigger,
Left Shift for faster movement and `R` to reset. Verify feedback colors, growing
bead and result metrics.

## Quest smoke test

Connect a developer-enabled Quest 3, approve USB debugging and Build and Run.
Verify passthrough, right-controller tracking, trigger activation, feedback,
haptics, completion and reset. Disable network access during the test to prove
there is no runtime network dependency.

Record a successful backup video. Explain that the prototype validates the
presentation interaction while Issues #46–#58 build the production fixture
calibration, process modes, safety state and deterministic recording.
