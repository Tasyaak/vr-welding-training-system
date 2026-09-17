# Tomorrow prototype runbook

This branch is a **presentation prototype**, not the final project architecture.
It demonstrates controller-based seam following without QR registration, ESP32,
Hall sensing, production-grade storage, or validated welding tolerances.

## Demonstrated behavior

- One 50 cm straight seam appears in front of the user.
- Green **START** and blue **END** markers make the required travel direction
  unambiguous.
- For the live demonstration, the seam can be aligned with two controller
  points: press A at its start and press A again at its end. This is a temporary
  demo placement mode, not the final QR registration workflow.
- The right Touch Plus controller drives a virtual welding tip.
- Position and movement speed are classified as good, warning, or bad.
- Holding the right trigger produces a progressive virtual bead while the tip
  remains within 5 cm of the seam.
- Welding must begin within the first 8% of the directed seam. Jumping forward
  or reversing beyond the small continuity tolerance inhibits activation, so
  skipped regions are not credited.
- Controller haptics warn about marginal or invalid movement.
- Short generated audio cues announce welding start, inhibition, and completion;
  no external sound assets are required.
- Losing controller tracking immediately pauses welding. After tracking returns,
  the trigger must be released before the simulation can activate again.
- Returning from a headset/application pause uses the same trigger-release
  re-arm rule.
- Completion, mean error, mean speed, percentage of good samples, attempt time,
  active welding time, and blocked-trigger time are shown.
- Each completed, reset, paused, or interrupted attempt is saved locally as a
  JSON summary and CSV sample stream.
- The B button resets the attempt.

The prototype is automatically created at runtime by
`PrototypeWeldingDemo.cs`; no instance needs to be placed in the scene.

If the controller represents an attached physical tool, create one scene object
with a `PrototypeWeldingDemo` component instead of relying on auto-start. Set
**Controller To Tip Offset** to the measured controller-to-tip vector in metres.
The same Inspector also exposes demo distance, speed, start, continuity, and
completion thresholds. These values are demonstration configuration, not
validated laser-welding requirements.

Optional travel/work-angle feedback is disabled by default. Enable it only
after verifying which local controller/attachment axis points toward the tool
tip. Configure that axis, the target angles, and tolerances in the same
Inspector. Keep **Orientation Inhibits Welding** disabled for the presentation
until the physical axis and angle convention have been checked on-device.

Audio feedback is enabled by default at a conservative volume. It can be
disabled or adjusted on a scene instance of `PrototypeWeldingDemo` if the
presentation room or headset audio setup makes the cues distracting.

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
| `C` | Capture the start/end point for demo seam placement |
| `P` | Toggle an automatic repeating rehearsal pass |
| `R` | Reset attempt |

Expected result: the sphere follows the simulated tool, feedback changes
between red/yellow/green, holding Space extends the bead, and reaching the end
shows the result summary.

If the live Quest demonstration is not ready, press `P` in Play Mode to run a
repeating automatic rehearsal. It begins away from the seam to show inhibited
feedback, then performs a valid pass, displays results, and resets. Record this
as the emergency presentation fallback; do not present it as device tracking.

## Quest build and smoke test

1. Connect a developer-enabled Quest 3 by USB and approve USB debugging inside
   the headset.
2. Confirm the headset is visible as the Android run device.
3. Build and Run the Bootstrap scene.
4. Confirm passthrough is visible and the HUD is readable.
5. Put the right controller at the physical seam start and press A. Move it to
   the seam end and press A again. The two points must be 15 cm to 1.5 m apart.
6. Return to the start, hold the trigger, and follow the displayed seam.
   Travel from the green **START** marker toward the blue **END** marker.
7. Verify position/speed feedback, audio cues, haptics, bead progress,
   completion, and reset.
8. As a negative test, release the trigger, jump well ahead of the bead edge,
   and hold it again. Activation must remain inhibited until returning to the
   current bead edge.
9. While welding, deliberately hide or sleep the controller. Confirm welding
   pauses and does not resume after tracking returns until the trigger is
   released once.

Session files are written below `Application.persistentDataPath` in
`PrototypeSessions/<session-id>/`. Each directory contains `summary.json` and
`samples.csv`. Samples include measured travel/work angles and their configured
quality state even when angle-based inhibition is disabled. The Unity Console
prints the exact path after saving. The summary also records tracking
interruption count, total invalid-tracking time, attempt duration, active
welding time, and blocked-trigger time. On Quest,
retrieve the application files later through USB/ADB; data export is not needed
for the live demonstration.

### Validate an exported session

After copying one session directory to a computer, validate it and print its
result metrics from the repository root:

```powershell
python tools/summarize_prototype_session.py "<path-to-session-directory>"
```

The command uses only the Python standard library. It checks the JSON/CSV
structure, numeric ranges, timestamp order, progress range, boolean values, and
sample-count agreement. It exits with a nonzero status and prints specific
errors when a recording is incomplete or inconsistent. Add `--json` when
machine-readable output is preferable.

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
