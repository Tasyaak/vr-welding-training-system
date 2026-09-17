"""Validate and summarize one exported prototype session using only Python."""

from __future__ import annotations

import argparse
import csv
import json
import math
import sys
from pathlib import Path
from typing import Any


SUPPORTED_SCHEMA_VERSIONS = {2, 3, 4}
REQUIRED_SUMMARY_FIELDS = (
    "schemaVersion",
    "sessionId",
    "completed",
    "finishReason",
    "completion",
    "averageErrorMetres",
    "averageSpeedMetresPerSecond",
    "goodSamplePercent",
    "sampleCount",
)
REQUIRED_SAMPLE_COLUMNS = (
    "elapsed_s",
    "seam_progress",
    "distance_m",
    "speed_mps",
    "trigger",
    "welding_allowed",
)


def finite_number(value: Any, field: str, errors: list[str]) -> float | None:
    try:
        number = float(value)
    except (TypeError, ValueError):
        errors.append(f"{field} is not a number: {value!r}")
        return None

    if not math.isfinite(number):
        errors.append(f"{field} must be finite: {value!r}")
        return None
    return number


def validate_session(session_path: Path) -> tuple[dict[str, Any], list[str], list[str]]:
    session_directory = session_path.parent if session_path.is_file() else session_path
    summary_path = session_directory / "summary.json"
    samples_path = session_directory / "samples.csv"
    errors: list[str] = []
    warnings: list[str] = []

    if not summary_path.is_file():
        return {}, [f"Missing summary file: {summary_path}"], warnings
    if not samples_path.is_file():
        return {}, [f"Missing sample file: {samples_path}"], warnings

    try:
        summary = json.loads(summary_path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        return {}, [f"Cannot read summary.json: {exception}"], warnings

    if not isinstance(summary, dict):
        return {}, ["summary.json must contain one JSON object"], warnings

    for field in REQUIRED_SUMMARY_FIELDS:
        if field not in summary:
            errors.append(f"summary.json is missing {field}")

    schema_version = summary.get("schemaVersion")
    if not isinstance(schema_version, int):
        errors.append("schemaVersion must be an integer")
    elif schema_version not in SUPPORTED_SCHEMA_VERSIONS:
        warnings.append(
            f"Schema version {schema_version} is not one of the tested versions "
            f"{sorted(SUPPORTED_SCHEMA_VERSIONS)}"
        )

    if not isinstance(summary.get("completed"), bool):
        errors.append("completed must be a boolean")
    for field in ("sessionId", "finishReason"):
        if not isinstance(summary.get(field), str) or not summary.get(field):
            errors.append(f"{field} must be a non-empty string")

    if isinstance(schema_version, int) and schema_version >= 3:
        for field in ("trackingInterruptionCount", "invalidTrackingSeconds"):
            if field not in summary:
                errors.append(f"schema {schema_version} summary is missing {field}")
    if isinstance(schema_version, int) and schema_version >= 4:
        for field in (
            "attemptElapsedSeconds",
            "weldingActiveSeconds",
            "blockedTriggerSeconds",
        ):
            if field not in summary:
                errors.append(f"schema {schema_version} summary is missing {field}")

    completion = finite_number(summary.get("completion"), "completion", errors)
    average_error = finite_number(
        summary.get("averageErrorMetres"), "averageErrorMetres", errors
    )
    average_speed = finite_number(
        summary.get("averageSpeedMetresPerSecond"),
        "averageSpeedMetresPerSecond",
        errors,
    )
    good_percent = finite_number(
        summary.get("goodSamplePercent"), "goodSamplePercent", errors
    )
    optional_numbers: dict[str, float | None] = {}
    for field in (
        "invalidTrackingSeconds",
        "attemptElapsedSeconds",
        "weldingActiveSeconds",
        "blockedTriggerSeconds",
    ):
        optional_numbers[field] = (
            finite_number(summary[field], field, errors) if field in summary else None
        )

    tracking_interruptions = summary.get("trackingInterruptionCount")
    if tracking_interruptions is not None and (
        not isinstance(tracking_interruptions, int)
        or isinstance(tracking_interruptions, bool)
        or tracking_interruptions < 0
    ):
        errors.append("trackingInterruptionCount must be a non-negative integer")

    if completion is not None and not 0 <= completion <= 1:
        errors.append(f"completion is outside 0..1: {completion}")
    if good_percent is not None and not 0 <= good_percent <= 100:
        errors.append(f"goodSamplePercent is outside 0..100: {good_percent}")
    if average_error is not None and average_error < 0:
        errors.append("averageErrorMetres cannot be negative")
    if average_speed is not None and average_speed < 0:
        errors.append("averageSpeedMetresPerSecond cannot be negative")
    for field, value in optional_numbers.items():
        if value is not None and value < 0:
            errors.append(f"{field} cannot be negative")

    row_count = 0
    previous_elapsed = -math.inf
    try:
        with samples_path.open("r", encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            columns = reader.fieldnames or []
            for column in REQUIRED_SAMPLE_COLUMNS:
                if column not in columns:
                    errors.append(f"samples.csv is missing column {column}")

            if all(column in columns for column in REQUIRED_SAMPLE_COLUMNS):
                for line_number, row in enumerate(reader, start=2):
                    row_count += 1
                    elapsed = finite_number(
                        row["elapsed_s"], f"samples.csv line {line_number} elapsed_s", errors
                    )
                    progress = finite_number(
                        row["seam_progress"],
                        f"samples.csv line {line_number} seam_progress",
                        errors,
                    )
                    distance = finite_number(
                        row["distance_m"],
                        f"samples.csv line {line_number} distance_m",
                        errors,
                    )
                    speed = finite_number(
                        row["speed_mps"],
                        f"samples.csv line {line_number} speed_mps",
                        errors,
                    )

                    if elapsed is not None:
                        if elapsed < 0:
                            errors.append(
                                f"samples.csv line {line_number} elapsed_s cannot be negative"
                            )
                        if elapsed < previous_elapsed:
                            errors.append(
                                f"samples.csv line {line_number} time goes backwards"
                            )
                        previous_elapsed = elapsed
                    if progress is not None and not 0 <= progress <= 1:
                        errors.append(
                            f"samples.csv line {line_number} seam_progress is outside 0..1"
                        )
                    if distance is not None and distance < 0:
                        errors.append(
                            f"samples.csv line {line_number} distance_m cannot be negative"
                        )
                    if speed is not None and speed < 0:
                        errors.append(
                            f"samples.csv line {line_number} speed_mps cannot be negative"
                        )
                    for column in ("trigger", "welding_allowed"):
                        if row[column] not in {"0", "1"}:
                            errors.append(
                                f"samples.csv line {line_number} {column} must be 0 or 1"
                            )
    except (OSError, UnicodeError, csv.Error) as exception:
        errors.append(f"Cannot read samples.csv: {exception}")

    expected_count = summary.get("sampleCount")
    if not isinstance(expected_count, int) or isinstance(expected_count, bool):
        errors.append("sampleCount must be an integer")
    elif expected_count != row_count:
        errors.append(
            f"sampleCount is {expected_count}, but samples.csv contains {row_count} rows"
        )

    report = {
        "sessionDirectory": str(session_directory.resolve()),
        "schemaVersion": schema_version,
        "sessionId": summary.get("sessionId"),
        "completed": summary.get("completed"),
        "finishReason": summary.get("finishReason"),
        "completionPercent": completion * 100 if completion is not None else None,
        "averageErrorCentimetres": average_error * 100 if average_error is not None else None,
        "averageSpeedCentimetresPerSecond": (
            average_speed * 100 if average_speed is not None else None
        ),
        "goodSamplePercent": good_percent,
        "sampleCount": row_count,
        "attemptElapsedSeconds": optional_numbers["attemptElapsedSeconds"],
        "weldingActiveSeconds": optional_numbers["weldingActiveSeconds"],
        "blockedTriggerSeconds": optional_numbers["blockedTriggerSeconds"],
        "trackingInterruptionCount": tracking_interruptions,
        "invalidTrackingSeconds": optional_numbers["invalidTrackingSeconds"],
    }
    return report, errors, warnings


def format_optional(value: Any, suffix: str = "") -> str:
    return "not recorded" if value is None else f"{float(value):.1f}{suffix}"


def print_human_report(
    report: dict[str, Any], errors: list[str], warnings: list[str]
) -> None:
    if report:
        tracking_interruptions = report.get("trackingInterruptionCount")
        tracking_label = (
            "not recorded" if tracking_interruptions is None else str(tracking_interruptions)
        )
        print(f"Session: {report.get('sessionId')}")
        print(
            "Status: "
            + ("complete" if report.get("completed") else "incomplete")
            + f" ({report.get('finishReason')})"
        )
        print(f"Completion: {format_optional(report.get('completionPercent'), '%')}")
        print(
            "Average error: "
            f"{format_optional(report.get('averageErrorCentimetres'), ' cm')}"
        )
        print(
            "Average speed: "
            f"{format_optional(report.get('averageSpeedCentimetresPerSecond'), ' cm/s')}"
        )
        print(f"Good samples: {format_optional(report.get('goodSamplePercent'), '%')}")
        print(f"Samples: {report.get('sampleCount')}")
        print(
            "Timing: "
            f"attempt {format_optional(report.get('attemptElapsedSeconds'), ' s')}, "
            f"active {format_optional(report.get('weldingActiveSeconds'), ' s')}, "
            f"blocked {format_optional(report.get('blockedTriggerSeconds'), ' s')}"
        )
        print(
            "Tracking: "
            f"{tracking_label} interruptions, "
            f"{format_optional(report.get('invalidTrackingSeconds'), ' s')} invalid"
        )

    for warning in warnings:
        print(f"WARNING: {warning}", file=sys.stderr)
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    print("Validation: " + ("FAILED" if errors else "PASSED"))


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate and summarize a PrototypeSessions session directory."
    )
    parser.add_argument("session", type=Path, help="Session directory or summary.json path")
    parser.add_argument(
        "--json", action="store_true", help="Print the report and validation as JSON"
    )
    args = parser.parse_args()

    report, errors, warnings = validate_session(args.session)
    if args.json:
        print(
            json.dumps(
                {"valid": not errors, "report": report, "warnings": warnings, "errors": errors},
                indent=2,
            )
        )
    else:
        print_human_report(report, errors, warnings)
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
