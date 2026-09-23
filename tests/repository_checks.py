"""Fast repository invariants used locally and in GitHub Actions."""

from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]

REQUIRED = (
    "README.md",
    ".gitignore",
    ".gitattributes",
    ".editorconfig",
    "unity/Packages/manifest.json",
    "unity/Packages/packages-lock.json",
    "unity/ProjectSettings/ProjectVersion.txt",
    "unity/ProjectSettings/EditorBuildSettings.asset",
)

FORBIDDEN_PREFIXES = (
    "unity/Library/",
    "unity/Temp/",
    "unity/Obj/",
    "unity/Logs/",
    "unity/UserSettings/",
    "unity/Build/",
    "unity/Builds/",
    "unity/MemoryCaptures/",
    "unity/Recordings/",
    "unity/.utmp/",
)

RETIRED_RUNTIME_PREFIXES = (
    "firmware/",
    "protocol/",
    "unity/Assets/Trainer/Infrastructure/ESP32",
)

RETIRED_SCENE_NAMES = ("HallSensorFrame",)


def tracked_files() -> list[str]:
    output = subprocess.check_output(
        ["git", "ls-files", "-z"], cwd=ROOT
    ).decode("utf-8")
    return [name.replace("\\", "/") for name in output.split("\0") if name]


def check_build_scenes(errors: list[str]) -> None:
    settings = ROOT / "unity/ProjectSettings/EditorBuildSettings.asset"
    text = settings.read_text(encoding="utf-8")
    enabled_scene_paths = re.findall(
        r"- enabled: 1\s+path: Assets/(.+\.unity)", text
    )
    if not enabled_scene_paths:
        errors.append("No enabled scene exists in EditorBuildSettings.asset")
        return

    for relative_path in enabled_scene_paths:
        scene = ROOT / "unity/Assets" / relative_path
        if not scene.is_file():
            errors.append(f"Enabled build scene does not exist: {scene.relative_to(ROOT)}")


def check_dev_agent_settings(errors: list[str]) -> None:
    settings = ROOT / "unity/Assets/Resources/DevAgentSettings.asset"
    if not settings.exists():
        return

    text = settings.read_text(encoding="utf-8")
    token = re.search(r"^\s*accessToken:\s*(\S+)\s*$", text, re.MULTILINE)
    address = re.search(r"^\s*serverAddress:\s*(\S+)\s*$", text, re.MULTILINE)
    enabled = re.search(r"^\s*enabled:\s*(\d+)\s*$", text, re.MULTILINE)

    if token:
        errors.append("DevAgentSettings.asset contains a tracked access token")
    if address and address.group(1) not in {"127.0.0.1", "localhost"}:
        errors.append("DevAgentSettings.asset contains a non-loopback server address")
    if enabled and enabled.group(1) != "0":
        errors.append("DevAgentSettings.asset must be disabled in source control")


def check_training_test_isolation(errors: list[str]) -> None:
    build_path = ROOT / "unity/ProjectSettings/EditorBuildSettings.asset"
    build = build_path.read_text(encoding="utf-8")

    if "TrainingTest/FakeTraining.unity" in build:
        errors.append("FakeTraining scene must not be included in release Build Settings")

    fake_scene = ROOT / "unity/Assets/Trainer/Scenes/TrainingTest/FakeTraining.unity"
    if fake_scene.is_file() and fake_scene.read_text(encoding="utf-8").count(
        "WeldingTrainer.TrainingTest.FakeTrainingComposition"
    ) != 1:
        errors.append("FakeTraining scene must contain exactly one fake composition")

    fake_asmdef = (
        ROOT
        / "unity/Assets/Trainer/Scenes/TrainingTest/WeldingTrainer.TrainingTest.asmdef"
    )
    if fake_asmdef.is_file():
        try:
            data = json.loads(fake_asmdef.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            errors.append(f"TrainingTest asmdef is invalid JSON: {exc}")
        else:
            include_platforms = data.get("includePlatforms")
            if include_platforms != ["Editor"]:
                errors.append(
                    "WeldingTrainer.TrainingTest must be Editor-only so synthetic-valid "
                    "providers cannot compile into the Quest player"
                )
            if data.get("autoReferenced") is not False:
                errors.append(
                    "WeldingTrainer.TrainingTest must keep autoReferenced=false"
                )

    bootstrap = ROOT / "unity/Assets/Trainer/Scenes/Bootstrap.unity"
    if bootstrap.is_file():
        bootstrap_text = bootstrap.read_text(encoding="utf-8")
        if "WeldingTrainer.TrainingTest.FakeTrainingComposition" in bootstrap_text:
            errors.append("Production Bootstrap must not contain FakeTrainingComposition")


def main() -> int:
    tracked = tracked_files()
    tracked_set = set(tracked)
    errors: list[str] = []

    for required in REQUIRED:
        if required not in tracked_set:
            errors.append(f"Required file is not tracked: {required}")

    for name in tracked:
        if name.startswith(FORBIDDEN_PREFIXES):
            errors.append(f"Generated Unity path is tracked: {name}")
        if name.startswith(RETIRED_RUNTIME_PREFIXES):
            errors.append(f"Retired external-device path is tracked: {name}")
        if Path(name).name in {".DS_Store", "Thumbs.db"}:
            errors.append(f"OS-generated file is tracked: {name}")

        path = ROOT / name
        if path.is_file() and path.stat().st_size > 25 * 1024 * 1024:
            errors.append(f"File exceeds 25 MiB: {name}")

    check_build_scenes(errors)
    check_dev_agent_settings(errors)
    check_training_test_isolation(errors)

    scene_path = ROOT / "unity/Assets/Trainer/Scenes/Bootstrap.unity"
    if scene_path.is_file():
        scene_text = scene_path.read_text(encoding="utf-8")
        for object_name in RETIRED_SCENE_NAMES:
            if f"m_Name: {object_name}" in scene_text:
                errors.append(f"Retired scene object remains: {object_name}")

    if errors:
        print("\n".join(f"ERROR: {error}" for error in errors))
        return 1

    print("Repository checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
