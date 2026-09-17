# Implementation roadmap

Milestones are ordered to retire the highest technical risks early. A milestone
is complete only when its acceptance evidence is committed or linked from its
pull request.

## 0. Secure, reproducible baseline

- Remove tracked local credentials and station configuration.
- Make the intended Bootstrap scene the build entry point.
- Set stable application identity and document setup and boundaries.
- Define coordinate and protocol conventions.
- Run repository invariants in CI.

Acceptance: repository checks pass from a clean clone. Rotate any credential
that existed in Git history. Opening the pinned Unity version restores packages
without modifying tracked manifests.

## 1. Quest MR bootstrap and workpiece registration

- Compose an XR origin and passthrough in `Bootstrap.unity`.
- Add explicit permission/availability checks and actionable error UI.
- Implement a QR/fiducial registration adapter behind an application interface.
- Visualize the workpiece origin and report registration quality.

Acceptance: a Quest 3 device test can register, recenter, lose, and reacquire a
stationary workpiece without silently changing the evaluation frame.

## 2. Seam model and deterministic evaluator

- Define versioned workpiece and seam assets.
- Sample tool-tip pose in workpiece space.
- Compute closest seam position, progress, distance, speed, and orientation.
- Add edit-mode tests for straight, curved, reversed, sparse, and degenerate
  seams plus timestamp and tracking-quality edge cases.

Acceptance: golden trajectories produce stable metrics independent of frame
rate and scene hierarchy.

## 3. Guided training vertical slice

- Add the session state machine and one complete seam exercise.
- Present visual, audio, and controller-haptic feedback with hysteresis.
- Pause safely on registration/tracking loss.
- Produce a review summary from the same metrics used during training.

Acceptance: one trainee can complete the exercise end to end on Quest 3 and the
recorded summary agrees with replayed samples.

## 4. ESP32 integration

- Select transport and finalize protocol v1 framing and status bits.
- Implement firmware watchdog, calibration, telemetry, and bounded haptics.
- Implement the Quest adapter, reconnect behavior, and connection health.
- Share golden protocol vectors between Unity and firmware tests.

Acceptance: disconnects, malformed packets, stale commands, and application
termination all stop haptic output within the documented watchdog time.

## 5. Session logging and analysis

- Define a versioned, privacy-minimized export schema.
- Persist sessions atomically and handle storage exhaustion.
- Build validation and analysis scripts for trajectory and cohort metrics.

Acceptance: a recorded session can be exported, validated, replayed, and
analyzed without relying on Unity scene objects or participant identity.

## 6. Hardening and study readiness

- Profile thermals, frame time, memory, tracking quality, and battery impact.
- Add accessibility, onboarding, recovery flows, and operator diagnostics.
- Freeze configurations used in studies and document calibration procedures.

Acceptance: repeatable device test protocol passes across representative Quest
3 devices and the study build can be reproduced from a tagged commit.
