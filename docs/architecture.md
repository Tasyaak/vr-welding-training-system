# Architecture

## Goals

The application runs standalone on Meta Quest 3. It registers a stationary
workpiece in passthrough, tracks a handheld welding tool, evaluates motion along
a configured seam, presents feedback, exchanges telemetry and haptic commands
with an ESP32, and stores session results locally.

The implementation uses dependency direction as its main boundary:

```text
Presentation / Platform / Infrastructure
                  |
                  v
             Application
                  |
                  v
                Domain
```

`Domain` must not depend on Unity scene objects, Meta XR, networking, storage,
or UI. `Application` coordinates use cases through interfaces. The outer layers
adapt Unity and device APIs to those interfaces.

## Unity modules

| Directory | Responsibility |
| --- | --- |
| `Trainer/Domain` | Seam geometry, samples, tolerances, scoring, and session results |
| `Trainer/Application` | Calibration, session state machine, evaluation orchestration |
| `Trainer/Platform/Meta` | Quest tracking, passthrough, anchors, QR registration, haptics |
| `Trainer/Infrastructure/ESP32` | Transport, protocol encoding, connection health |
| `Trainer/Infrastructure` | Local logs, configuration, clocks, identifiers |
| `Trainer/Presentation` | HUD, audio, visual guidance, results |
| `Trainer/Content` | Workpiece and seam definitions; no runtime secrets |
| `Trainer/Scenes` | Composition roots only; start with `Bootstrap.unity` |

Use assembly definitions as code is introduced so the dependency direction is
enforced by the compiler. Scene components should translate Unity types at the
boundary; domain calculations should use explicit value objects and SI units.

## Runtime flow

1. Bootstrap services and verify permissions/device availability.
2. Register the physical workpiece and establish `Workpiece` coordinates.
3. Load a versioned workpiece/seam definition.
4. Start a session and sample the tool pose at a monotonic timestamp.
5. Transform each sample into `Workpiece` coordinates.
6. Evaluate position, orientation, speed, continuity, and trigger state.
7. Publish feedback and optional ESP32 haptic commands.
8. Persist raw samples, events, configuration version, and summary atomically.

## State machine

```text
Boot -> Ready -> Registering -> Calibrated -> Training -> Review
  |       |          |              |            |
  +------ Error <----+--------------+------------+
```

Transitions are explicit and logged. Losing registration or required device
connectivity pauses evaluation instead of silently producing invalid scores.

## Data and safety rules

- Store distances in metres, angles in degrees, time in seconds, and speed in
  metres per second. Display-layer conversions must be explicit.
- Use a monotonic clock for durations and UTC only for wall-clock metadata.
- Version seam definitions, protocol messages, and exported session schemas.
- Never store credentials, tokens, station IPs, or participant identifiers in
  tracked Unity assets.
- Treat this as a training aid, not a real-welder safety interlock.

## Testing strategy

- Domain: deterministic edit-mode tests for geometry and scoring.
- Application: state-machine and failure-path tests with fake adapters.
- Protocol: golden vectors and malformed-message tests shared with firmware.
- Device: Quest smoke test covering permissions, passthrough, registration,
  tracking loss, reconnect, logging, and thermal/performance behavior.
