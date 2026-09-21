# Development and Unity setup

## Required software

- Unity Hub and Unity `6000.3.24f1`;
- Android Build Support, OpenJDK and Android SDK/NDK;
- Git and a C# editor;
- Meta Quest Developer Hub or `adb`;
- developer-enabled Meta Quest 3 and one right Touch Plus controller.

## Open and validate

1. Clone the repository and open its existing `unity` directory in Unity Hub.
2. Allow the locked packages to restore; do not reinstall or upgrade packages.
3. Open `Assets/Trainer/Scenes/Bootstrap.unity`.
4. Select Android and confirm OpenXR is enabled.
5. Run Meta Project Setup Tool and resolve blocking findings only.
6. Check Console for compile errors and missing scripts/references.
7. Run `python tests/repository_checks.py` from the repository root.

## Current scene composition

`Bootstrap.unity` is not an empty placeholder. It contains `OVRCameraRig`,
`MRUK`, `OVRPassthroughLayer` and the integrated `PrototypeDemo`. The retired
Hall frame has been removed. Production composition remains owned by #47.

MRUK is retained because the scene currently consumes its room/scene support.
Meta Core, OpenXR, passthrough and anchor permissions are retained for Quest MR
and four-point session anchoring. No QR permission, network service or external
device configuration is required.

## Detailed Unity changes needed for downstream scripts

Issues #46–#58 must provide prefabs/assets and editor wiring with their code.
When those scripts land:

1. Create versioned fixture, finite-surface, seam, tool/nozzle and process assets.
2. Add one production composition-root GameObject; do not put Domain logic on
   arbitrary scene objects.
3. Bind only the right Touch Plus controller to the tool-pose adapter and author
   its rigid controller-to-tool/tip offsets.
4. Add four visible ordered calibration-point prompts and a session-anchor
   visual; display residual error and reject invalid fits.
5. Add virtual clamp/nozzle, process menu, E-stop/reset/re-arm controls and
   explicit inhibited-reason UI.
6. Wire visual/audio/right-controller-haptic sinks to the same semantic feedback
   state; sinks must never alter scoring.
7. Wire crash-aware local recording and deterministic replay; keep participant
   identity out of tracked assets and exports.
8. Keep all serialized angles labelled in degrees in Inspector/UI and convert to
   radians when constructing Domain values.

Until a mandatory provider exists, the composition root must show the missing
provider and keep simulated process activation inhibited.

For issue #46 content creation and validation, follow `content-authoring.md`.

## Quest smoke test

Build and Run to Quest 3 with Wi-Fi disabled or unrelated to the application.
Verify passthrough startup, right-controller-only input, suspend/resume,
recenter behavior and absence of missing-reference errors. Four-point
calibration, process modes, E-stop, replay and performance checks remain pending
until their owning issues are integrated.
