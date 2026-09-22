# Quest-only architecture with fixture-mounted QR registration

## Dependency direction

```text
Presentation / Platform.Meta / Infrastructure.Local / Content
                            |
                            v
                       Application
                            |
                            v
                          Domain
```

Domain is deterministic engine-independent C#. Application owns session/process
and activation state; adapters translate Unity/Meta/storage types. QR tracking
belongs in Platform.Meta, not Domain. No external firmware, sensor, magnet,
coil, runtime socket peer or second-controller dependency is introduced.

## Responsibilities

| Module | Responsibility |
| --- | --- |
| Domain | Seam/surface math, process timing, contact/risk, coverage, scoring |
| Application | Session/process state, interlock, ordered commands/events and ports |
| Platform.Meta | QR observations, unsaved anchors, right input/HMD, lifecycle |
| Content | Local marker/assembly catalog, CAD bake, finite geometry and profiles |
| Infrastructure.Local | Clocks, IDs, bounded journals, recovery/export |
| Presentation | MR menu/guidance, audio/haptics, results; no activation decisions |

[The parallel contract](parallel-development-contract.md) fixes the boundary
between independently delivered spatial and training modules. Integration #58
owns the final production scene composition and maps the two module boundaries.
#47 owns the training coordinator and a fake-platform composition, not a mandatory
dependency of group A.

## Spatial ownership

```text
QR payload -> local assembly binding -> fixture/part definitions
QR observed pose + FixtureFromMarker -> candidate WorldFromFixture
accepted candidate -> session anchor -> Fixture -> bolted Workpiece
right grip pose -> Tool -> Tip -> Workpiece-local evaluation
```

[Coordinate equations](coordinate-conventions.md) are authoritative.
QR registration replaces four-point touch acquisition entirely. Tool-offset
setup and station metrology are still required.

Collect multiple stable observations of one known marker, verify dimensions,
front side, timestamps and pose consistency, then show a ghost assembly and
require explicit mounted-part/placement confirmation. Payload identity does not
prove that the intended part is actually bolted in place. Create/localize an
unsaved anchor before publishing Registered. Do not store anchors across sessions.

RegistrationSnapshot reports marker/assembly versions, generations, validity and
observation scatter, not fictitious rigid-fit residuals. Repeatability is not
absolute accuracy; station qualification measures the entire error budget.

## Runtime flow

1. Initialize passthrough, right input, QR capability/permission and recorder.
2. Resolve a known local part/assembly ID from a fixture QR.
3. Validate stable marker pose and authored offsets; confirm the secured part
   and ghost placement; create/localize the session anchor.
4. Select seam/process/profile; freeze configuration and prepare virtual nozzle/clamp.
5. Require released trigger and explicit arm with every prerequisite valid.
6. Capture coherent tool/head/registration/input snapshots.
7. Evaluate finite contact, seam geometry and prospective reflection risk.
8. Evaluate the single activation decision before process timing and coverage.
9. Publish semantic feedback and ordered replay evidence; render independently.
10. Review/finish/save; retry with a new attempt or end/dispose the anchor.

QR disappearance after successful registration is allowed while the anchor stays
valid. Tracking/anchor loss, untrusted recenter, moved/rebolted assembly or
conflicting marker evidence inhibits immediately. Never auto-replace the active
part or silently resume while the trigger is held.

## Authoritative training state

The concrete Group B state contract and isolated synthetic composition are documented in
[training-state-and-fake-composition.md](training-state-and-fake-composition.md). Production
Bootstrap mapping remains exclusively owned by Issue #58.

All modes share one session/process snapshot and activation reducer. Virtual
clamp Connected is a user preparation state, separate from mechanical bolts
and from finite-surface contact. Both modes of cleaning require CleaningNozzle;
welding modes require WeldingNozzle. Nozzle change is explicit and disarmed.

Right B is a short-press, global, latched software E-stop. Reset requires release,
valid prerequisites and acknowledgement; separate re-arm/fresh trigger is needed.
Reflection uses incident direction, actual normal and a conservative hazard cone
against a tracked head volume. Unknown inhibits; confirmed trips latch.
No reflected watts or real industrial safety approval is claimed.

Pulse windows and wobble phase use monotonic timestamps, not rendering FPS.
Assistance changes presentation only. Invalid intervals and interruptions remain
visible in metrics; output/coverage cannot bridge unknown samples.

## Testing and implementation state

Main ee8999e contains only the demo code and Quest scene; production logic,
authoring assets and model tests are still to be implemented. Current CI checks
repository hygiene, not device functionality.

Group A runs adapter/transform/catalog tests and Quest registration qualification.
Group B runs pure math/state tests, fake-port integration and PlayMode presentation
with synthetic spatial data. #58 adds real-adapter contract tests, full Quest
acceptance, persistence/replay parity and sustained performance evidence.
