#!/usr/bin/env python3
"""Offline integrity check for USB/ADB-copied production training sessions."""
from __future__ import annotations
import argparse, hashlib, json
from pathlib import Path

def validate(session: Path) -> dict:
    errors, attempts = [], []
    session_file = session / "session.json"
    if not session_file.exists(): errors.append("missing session.json")
    else:
        metadata = json.loads(session_file.read_text(encoding="utf-8"))
        if metadata.get("schemaVersion") != 1: errors.append("unsupported session schema")
        if not metadata.get("completed"): errors.append("session is incomplete")
    partials = [str(p.relative_to(session)) for p in session.rglob("*.partial")]
    if partials: errors.append("partial files remain")
    attempts_root = session / "attempts"
    for attempt in sorted(attempts_root.iterdir()) if attempts_root.exists() else []:
        if not attempt.is_dir(): continue
        result = {"attemptId": attempt.name, "integrity": True}
        manifest_path = attempt / "manifest.json"
        summary_path = attempt / "summary.json"
        if not manifest_path.exists() or not summary_path.exists():
            errors.append(f"{attempt.name}: missing completion files"); result["integrity"] = False
        else:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            summary = json.loads(summary_path.read_text(encoding="utf-8"))
            if not manifest.get("complete") or not summary.get("complete"):
                errors.append(f"{attempt.name}: marked incomplete"); result["integrity"] = False
            for chunk in manifest.get("chunks", []):
                path = attempt / chunk["file"]
                if not path.exists(): errors.append(f"{attempt.name}: missing {chunk['file']}"); result["integrity"] = False; continue
                data = path.read_bytes()
                if len(data) != chunk.get("bytes") or hashlib.sha256(data).hexdigest() != chunk.get("sha256"):
                    errors.append(f"{attempt.name}: integrity mismatch {chunk['file']}"); result["integrity"] = False
        attempts.append(result)
    return {"ok": not errors, "errors": errors, "partialFiles": partials, "attempts": attempts}

def main() -> int:
    parser = argparse.ArgumentParser(); parser.add_argument("session", type=Path); args = parser.parse_args()
    report = validate(args.session); print(json.dumps(report, indent=2)); return 0 if report["ok"] else 1
if __name__ == "__main__": raise SystemExit(main())
