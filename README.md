# Welding VR Training System

Mixed-reality training system for handheld laser welding on Meta Quest 3

The application runs standalone on Quest 3 and overlays training information on a real, stationary workpiece visible through passthrough. The project is intended to evaluate and guide the trainee's movement along predefined weld seams using controller tracking, QR-based workpiece registration, visual/audio/haptic feedback, ESP32 telemetry, and local session logging

## Repository structure

```text
welding-vr-training-system/
├── .github/
│   └── workflows/
│       └── repository-checks.yml
├── analysis/
├── docs/
│   ├── architecture.md
│   ├── coordinate-conventions.md
│   ├── git-and-github.md
│   └── setup.md
├── firmware/
│   └── README.md
├── protocol/
│   └── specification.md
├── tests/
├── unity/
│   ├── Assets/
│   │   └── Trainer/
│   │       ├── Application/
│   │       ├── Content/
│   │       ├── Domain/
│   │       ├── Infrastructure/
│   │       ├── Platform/
│   │       ├── Presentation/
│   │       └── Scenes/
│   ├── Packages/
│   └── ProjectSettings/
├── .editorconfig
├── .gitattributes
├── .gitignore
└── README.md
```

### Main directories

- `unity/` — the Unity project. Open this directory in Unity Hub
- `unity/Assets/Trainer/` — project-owned Unity code, scenes, configuration, and content
- `firmware/` — ESP32 firmware developed with Arduino IDE
- `protocol/` — the Quest ↔ ESP32 communication contract
- `analysis/` — offline analysis of exported session data
- `tests/` — project-level tests that do not naturally belong inside Unity or firmware
- `docs/` — architecture, setup, coordinate conventions, and collaboration documentation
- `.github/` — GitHub automation such as repository hygiene checks

Unity-generated folders such as `unity/Library/`, `unity/Temp/`, `unity/Logs/`, and `unity/UserSettings/` are local files and must not be committed

## Development prerequisites

Install:

- [Unity Hub](https://docs.unity.com/en-us/hub/install-hub) (register first)
- Unity 6.3 LTS in the Unity Hub with platform modules Android Build Support (OpenJDK and Android SDK & NDK Tools), don't install Microsoft Visual Studio Community 2026 and сancel automatic download of newest Unity LTS version
- VS Code
- Extensions in the VS Code:
  - Unity — C# and C# Dev Kit will be installed automatically 
  - Python — optional, for data analysis
  - Jupyter — optional, for data analysis
- Git
- GitHub CLI — optional, for working with a github repository via a terminal

A Meta Quest 3 is required for final MR/device integration testing

## Clone and open the project

Clone the repository:

```bash
gh repo clone OWNER/REPOSITORY
cd REPOSITORY
```

or:

```bash
git clone <repository-clone-url>
cd REPOSITORY
```

In Unity Hub, choose **Add project from disk** and select:

```text
<repository>/unity
```

Do not create a new Unity project and copy `Assets` into it.

On the first open, Unity restores dependencies from:

```text
unity/Packages/manifest.json
unity/Packages/packages-lock.json
```

Developers normally do not reinstall project packages manually. The first restore requires Internet access

If package restoration fails, close Unity and delete `unity/Library/PackageCache/` or, if necessary, the complete local `unity/Library/` directory, then reopen the project. Do not delete the tracked `manifest.json` or `packages-lock.json`

## Unity project rules

Commit `.meta` files together with their corresponding Unity assets

Do not casually update Unity, Meta XR, OpenXR, MRUK, Splines, or other shared packages as part of unrelated work. Package changes affect the whole team and should be reviewed in a dedicated task/PR

Project-owned code and content should normally be placed under:

```text
unity/Assets/Trainer/
```

## Firmware and protocol

The ESP32 firmware is stored under `firmware/`

Do not commit Wi-Fi credentials, tokens, local IP overrides, or other station-specific secrets

When changing the Quest ↔ ESP32 message format or semantics, update:

```text
protocol/specification.md
```

and verify both Unity and firmware sides together

## Git and GitHub workflow

Normal feature work must not be done directly on `main`

Use short-lived task branches, Pull Requests, review, relevant tests, and squash merge

The complete project-specific Git/GitHub guide is here:

**[`docs/git-and-github.md`](docs/git-and-github.md)**

It explains commits, branches, `.gitignore`, `.gitattributes`, Issues, Pull Requests, branch synchronization, conflict resolution, GitHub CLI, and the recommended workflow for this project

The staged implementation plan is documented in
**[`docs/roadmap.md`](docs/roadmap.md)**.

The temporary presentation prototype and its Unity/Quest test procedure are
documented in **[`docs/tomorrow-prototype-runbook.md`](docs/tomorrow-prototype-runbook.md)**.

The Fusion presentation demo (one-metre angle workpiece, sparks, cooling metal,
and progressive seam formation) has a detailed Russian Unity setup guide:
**[`docs/fusion-demo-unity-guide.md`](docs/fusion-demo-unity-guide.md)**.

For the current end-start attempt rules, dwell-based penetration, overheating,
burn-through holes, and the updated graph, see
**[`docs/fusion-overheating.md`](docs/fusion-overheating.md)**.
