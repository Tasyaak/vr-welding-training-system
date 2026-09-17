"""Fast repository invariants used locally and in GitHub Actions."""

from __future__ import annotations

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
)


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
        if name.endswith(("/secrets.h", "/config.local.h")):
            errors.append(f"Local firmware configuration is tracked: {name}")
        if Path(name).name in {".DS_Store", "Thumbs.db"}:
            errors.append(f"OS-generated file is tracked: {name}")

        path = ROOT / name
        if path.is_file() and path.stat().st_size > 25 * 1024 * 1024:
            errors.append(f"File exceeds 25 MiB: {name}")

    check_build_scenes(errors)
    check_dev_agent_settings(errors)

    if errors:
        print("\n".join(f"ERROR: {error}" for error in errors))
        return 1

    print("Repository checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
