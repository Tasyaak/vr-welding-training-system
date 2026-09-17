# Tomorrow prototype runbook

This branch is a **presentation prototype**, not the final project architecture.
It demonstrates controller-based seam following without QR registration, ESP32,
Hall sensing, persistent logs, or validated welding tolerances.

## Demonstrated behavior

- One 50 cm straight seam appears in front of the user.
- The right Touch Plus controller drives a virtual welding tip.
- Position and movement speed are classified as good, warning, or bad.
- Holding the right trigger produces a progressive virtual bead while the tip
  remains within 5 cm of the seam.
- Controller haptics warn about marginal or invalid movement.
- Completion, mean error, mean speed, and percentage of good samples are shown.
- Each completed, reset, paused, or interrupted attempt is saved locally as a
  JSON summary and CSV sample stream.
- The B button resets the attempt.

The prototype is automatically created at runtime by
`PrototypeWeldingDemo.cs`; no instance needs to be placed in the scene.

## Required Unity setup

Use Unity `6000.3.24f1` and open the repository's `unity` directory.

1. Switch the build profile to Android.
2. Open `Assets/Trainer/Scenes/Bootstrap.unity`.
3. Delete the existing `Main Camera` object.
4. Add the Meta XR `OVRCameraRig` prefab to the scene.
5. On `OVRManager`, set tracking origin to **Stage**.
6. Set Passthrough Support to **Supported** or **Required**.
7. Enable Insight Passthrough.
8. Add an `OVRPassthroughLayer` component on a persistent empty object and set
   its placement to **Underlay**.
9. Set the scene skybox material to None.
10. On `CenterEyeAnchor`, use a solid black background with alpha zero.
11. Run Meta's Project Setup Tool and fix blocking Android/Quest issues.
12. Save the scene and commit it together with all generated `.meta` files.

The project already contains Meta XR Core, MRUK, OpenXR, and Android settings.
Do not upgrade packages for the demo.

## Editor smoke test

Press Play. The prototype starts automatically.

| Key | Action |
| --- | --- |
| `J` / `L` | Move the simulated tool left/right |
| `I` / `K` | Move up/down |
| `U` / `O` | Move away/toward the camera |
| Left Shift | Move faster |
| Space | Hold simulated trigger |
| `R` | Reset attempt |

Expected result: the sphere follows the simulated tool, feedback changes
between red/yellow/green, holding Space extends the bead, and reaching the end
shows the result summary.

## Quest build and smoke test

1. Connect a developer-enabled Quest 3 by USB and approve USB debugging inside
   the headset.
2. Confirm the headset is visible as the Android run device.
3. Build and Run the Bootstrap scene.
4. Confirm passthrough is visible and the HUD is readable.
5. Move the right controller near the seam and hold its trigger.
6. Verify position/speed feedback, haptics, bead progress, completion, and reset.

Session files are written below `Application.persistentDataPath` in
`PrototypeSessions/<session-id>/`. Each directory contains `summary.json` and
`samples.csv`. The Unity Console prints the exact path after saving. On Quest,
retrieve the application files later through USB/ADB; data export is not needed
for the live demonstration.

If controller-to-tip placement is visibly wrong, record the symptom and adjust
the prototype only enough for the presentation. The final project will use a
versioned physical attachment transform.

## Presentation script

1. State that this is a geometry/kinematics prototype, not a laser-physics
   simulation and not a real-laser safety system.
2. Show the target seam over passthrough.
3. Demonstrate a good pass and the growing bead.
4. Deliberately move too far away and show activation being inhibited.
5. Demonstrate too-slow and too-fast feedback.
6. Finish the seam and show the result metrics.
7. Explain that QR registration, ESP32/Hall input, persistent logging, and
   validated tolerances are the next integration stages.

Record one successful backup video before the presentation.
