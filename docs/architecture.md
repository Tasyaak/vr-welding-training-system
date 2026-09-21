# Quest-only architecture

## Dependency direction

```text
Presentation / Platform.Meta / Infrastructure.Local
                         |
                         v
                    Application
                         |
                         v
                       Domain
```

Domain is deterministic C# and must not depend on Unity scene objects, Meta XR,
storage or UI. Application owns process/session state through ports. Outer
modules translate Unity, Quest and persistence APIs at the boundary.

Forbidden runtime dependencies are QR recognition, network sockets, external
firmware, external sensors, magnets, coils and second-controller input.

## Responsibilities

| Module | Responsibility |
| --- | --- |
| `Trainer.Domain` | Fixture/surface geometry, seams, tool pose, modes, contact, reflection risk, coverage, scoring and results |
| `Trainer.Application` | Calibration workflow, process/session state, interlock, E-stop, replay journal and use cases |
| `Trainer.Platform.Meta` | Right Touch Plus input, tracking health, passthrough, session anchor and lifecycle |
| `Trainer.Infrastructure.Local` | Monotonic clock, IDs, crash-aware local recording and export |
| `Trainer.Presentation` | MR guidance, menu, feedback, Touch Plus haptics and review |
| `Trainer.Content` | Versioned fixture, surface, tool and process definitions |
| `Trainer.Scenes` | Composition roots only |

## Frame graph

```text
Tracking/World
    -> SessionAnchor
        -> Fixture
            -> Workpiece
                -> authored surfaces and seams

RightController
    -> Tool
        -> Tip
        -> virtual nozzle
```

Four measured fixture points solve `Fixture -> SessionAnchor`. Production
evaluation uses fixture/workpiece-local data; presentation converts results to
world space. Recenter or tracking-origin changes must not silently redefine the
calibrated fixture.

## Runtime flow

1. Bootstrap passthrough, right-controller tracking, local storage and UI.
2. Select versioned fixture, tool and process content.
3. Acquire four ordered fixture points and validate rigid-fit residuals.
4. Create a session-only anchor and freeze the accepted calibration generation.
5. Select nozzle/process settings, apply virtual clamp and explicitly arm.
6. Sample one right controller using a monotonic timestamp.
7. Evaluate surface contact, seam motion, process behavior and reflection risk.
8. Apply the unified fail-closed interlock and semantic feedback.
9. Record inputs, state transitions, configuration and outputs for replay.
10. Review, retry, export or end the session.

## Authoritative state

Application owns one explicit process/session state. Tracking loss, calibration
invalidity, contact loss, E-stop, latched reflection fault or missing mandatory
provider inhibits simulated activation. Recovery requires stable inputs, trigger
release and explicit re-arm; faults never clear merely because a frame became
valid again.

## Units and determinism

- metres, seconds and metres/second in Domain;
- radians in Domain and serialized numeric contracts;
- degrees only in authoring UI and trainee-facing presentation, with explicit
  conversion at the boundary;
- monotonic time for evaluation/replay, UTC only as metadata;
- versioned content, evaluator and export schemas.

## Testing

- Domain: pure deterministic tests for local geometry, modes and coverage.
- Application: scripted clocks/inputs for state, interlock, faults and replay.
- Unity: clean import, EditMode/PlayMode composition and missing-reference checks.
- Quest: passthrough, right-controller-only input, calibration, recenter/resume,
  E-stop, recording, thermal/frame-time and battery smoke tests.
