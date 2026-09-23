# Session metrics, recording, and export

Issue #17 adds a production recording core independent of Unity and Group A adapters. `AttemptMetricsAccumulator` computes time-weighted validity, blocked-trigger time, position and speed quality, angle error, progress, reverse motion, and coverage. Invalid or generation-mismatched samples accumulate invalid time and cannot yield a successful score.

`LocalSessionRecorder` receives an injected storage root. In Unity #58 must pass `Application.persistentDataPath`; no Android path is embedded in the core. Each random session ID is stored under `Sessions/<sessionId>`, with attempt metadata and append-only JSONL chunks. Defaults rotate at 2 MiB or five minutes. The bounded queue reserves capacity for critical events; dropped samples make the attempt incomplete and unscored.

Chunks are written as `.partial`, atomically renamed, and listed with byte length and SHA-256 in `manifest.json`. The summary and manifest are committed only after queued evidence is flushed. Missing manifests, partial files, checksum failures, interrupted attempts, and unfinished sessions remain explicitly incomplete. Retention defaults to 20 sessions, never deletes the active session, and removes only older directories when a new session starts.

## Unity setup for #58

1. Generate a cryptographically random session ID and construct `SessionMetadata` from the selected binding/configuration and registration evidence.
2. Create `LocalSessionRecorder(Application.persistentDataPath, metadata)` and provide it as both `IRecorderPort` and `IAttemptDataRecorder`.
3. Append lifecycle events through the coordinator and one immutable `AttemptSample` per consumed evaluator snapshot. Preserve monotonic seconds, metres, radians, generations, validity, assistance, activation reasons, and coverage.
4. Finish every attempt before presenting results. Treat `Finish == false`, dropped samples, or recorder unavailability as unscored/incomplete.
5. Dispose during orderly shutdown. Do not present a directory lacking a valid completion manifest as a completed attempt.

Quest files can be copied without a backend using Android File Transfer or `adb pull <persistent-data-path>/Sessions`. Run `python tools/validate_training_session.py <copied-session-directory>` after copying; it recomputes every chunk checksum and returns nonzero for incomplete or modified evidence.
