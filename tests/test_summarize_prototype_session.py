"""Tests for the dependency-free prototype session validator."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "summarize_prototype_session.py"
CSV_HEADER = (
    "elapsed_s,tool_x_m,tool_y_m,tool_z_m,seam_progress,distance_m,"
    "speed_mps,travel_angle_deg,work_angle_deg,orientation_good,trigger,"
    "welding_allowed\n"
)
CSV_ROWS = (
    "0.000,0,0,0,0.000,0.010,0.050,0,45,1,1,1\n"
    "0.100,0.01,0,0,0.020,0.012,0.060,0,45,1,1,1\n"
)


def write_session(
    directory: Path, sample_count: int = 2, schema_version: int = 6
) -> None:
    summary = {
        "schemaVersion": schema_version,
        "sessionId": "test-session",
        "startedUtc": "2026-09-17T10:00:00Z",
        "finishedUtc": "2026-09-17T10:00:02Z",
        "completed": True,
        "finishReason": "completed",
        "completion": 1.0,
        "averageErrorMetres": 0.011,
        "averageSpeedMetresPerSecond": 0.055,
        "trackingInterruptionCount": 0,
        "invalidTrackingSeconds": 0.0,
        "attemptElapsedSeconds": 2.0,
        "weldingActiveSeconds": 1.8,
        "blockedTriggerSeconds": 0.1,
        "sampleCount": sample_count,
    }
    quality_field = "qualityInRangePercent" if schema_version >= 5 else "goodSamplePercent"
    summary[quality_field] = 100.0
    if schema_version >= 6:
        summary["configuration"] = {
            "evaluatorVersion": "prototype-straight-seam-v1",
            "applicationVersion": "0.1.0",
            "unityVersion": "6000.3.24f1",
            "runtimePlatform": "WindowsEditor",
            "seamStartWorldMetres": {"x": 0.0, "y": 0.0, "z": 0.0},
            "seamEndWorldMetres": {"x": 0.5, "y": 0.0, "z": 0.0},
            "controllerToTipOffsetMetres": {"x": 0.0, "y": 0.0, "z": 0.0},
            "goodDistanceMetres": 0.025,
            "maximumWeldDistanceMetres": 0.05,
            "minimumGoodSpeedMetresPerSecond": 0.05,
            "maximumGoodSpeedMetresPerSecond": 0.15,
            "completionThreshold": 0.97,
            "startProgressThreshold": 0.08,
            "maximumProgressJump": 0.08,
            "reverseProgressTolerance": 0.03,
            "evaluateOrientation": False,
            "orientationInhibitsWelding": False,
            "localToolForwardAxis": {"x": 0.0, "y": 0.0, "z": 1.0},
            "targetTravelAngleDegrees": 0.0,
            "travelAngleToleranceDegrees": 15.0,
            "targetWorkAngleDegrees": 45.0,
            "workAngleToleranceDegrees": 15.0,
        }
    (directory / "summary.json").write_text(json.dumps(summary), encoding="utf-8")
    (directory / "samples.csv").write_text(CSV_HEADER + CSV_ROWS, encoding="utf-8")


class SessionValidatorTests(unittest.TestCase):
    def run_validator(self, directory: Path) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), str(directory)],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_valid_session_passes_and_prints_metrics(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            session = Path(temporary_directory)
            write_session(session)
            result = self.run_validator(session)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Validation: PASSED", result.stdout)
        self.assertIn("Average error: 1.1 cm", result.stdout)
        self.assertIn("Quality in range: 100.0%", result.stdout)
        self.assertIn("Timing: attempt 2.0 s", result.stdout)
        self.assertIn("prototype-straight-seam-v1, seam 0.5 m", result.stdout)

    def test_legacy_schema_four_quality_field_is_supported(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            session = Path(temporary_directory)
            write_session(session, schema_version=4)
            result = self.run_validator(session)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Quality in range: 100.0%", result.stdout)

    def test_schema_six_requires_configuration_snapshot(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            session = Path(temporary_directory)
            write_session(session)
            summary_path = session / "summary.json"
            summary = json.loads(summary_path.read_text(encoding="utf-8"))
            del summary["configuration"]
            summary_path.write_text(json.dumps(summary), encoding="utf-8")
            result = self.run_validator(session)

        self.assertEqual(result.returncode, 1)
        self.assertIn("schema 6 summary is missing configuration", result.stderr)

    def test_sample_count_mismatch_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            session = Path(temporary_directory)
            write_session(session, sample_count=3)
            result = self.run_validator(session)

        self.assertEqual(result.returncode, 1)
        self.assertIn("samples.csv contains 2 rows", result.stderr)
        self.assertIn("Validation: FAILED", result.stdout)

    def test_invalid_timing_value_is_reported_without_crashing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            session = Path(temporary_directory)
            write_session(session)
            summary_path = session / "summary.json"
            summary = json.loads(summary_path.read_text(encoding="utf-8"))
            summary["attemptElapsedSeconds"] = "not-a-number"
            summary_path.write_text(json.dumps(summary), encoding="utf-8")
            result = self.run_validator(session)

        self.assertEqual(result.returncode, 1)
        self.assertIn("attemptElapsedSeconds is not a number", result.stderr)
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()
