# Development setup

## Required software

- Unity Hub and Unity `6000.3.24f1`
- Android Build Support, including OpenJDK and Android SDK/NDK tools
- Git and a C# editor
- Meta Quest Developer Hub or `adb` for device deployment
- A developer-enabled Meta Quest 3 for MR integration tests

## First open

1. Clone the repository.
2. In Unity Hub, add the repository's `unity` directory as the project.
3. Let Unity restore the package versions locked in `Packages/packages-lock.json`.
4. Open `Assets/Trainer/Scenes/Bootstrap.unity`.
5. Check that Android is the selected build target and OpenXR is enabled.

Do not create a second Unity project or reinstall packages manually. If package
restore is corrupt, close Unity, remove the local `unity/Library` directory,
and reopen the project.

## Local-only configuration

Do not commit station addresses, access tokens, Wi-Fi credentials, participant
data, keystores, or signing credentials. Keep them in ignored local files or a
runtime provisioning mechanism. `DevAgentSettings.asset` must stay disabled,
use loopback by default, and contain no access token.

## Validation

Run the repository checks before pushing:

```text
python tests/repository_checks.py
```

In Unity, run Edit Mode and Play Mode tests. A device-facing change also needs a
Quest smoke test; successful Editor play mode is not sufficient evidence for
passthrough, permissions, tracking, Android networking, or performance.

## Current bootstrap limitation

The checked-in Bootstrap scene is a composition placeholder. It does not yet
contain the Meta XR origin, passthrough setup, workpiece registration, or the
training session pipeline. Those belong in the next vertical-slice milestone.
