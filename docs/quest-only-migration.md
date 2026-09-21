# Quest-only migration record

Baseline audited: `main` at `6cf6c54f6c24540c58532dd7c4c829d4d1d35f21`.

## Dependency inventory

| Item | Decision | Reason / consumer |
| --- | --- | --- |
| Meta XR Core 205.0.0 | Retain | `OVRCameraRig`, controller tracking, passthrough and anchor APIs |
| MR Utility Kit 205.0.0 | Retain | `MRUK` exists in `Bootstrap.unity`; room/scene MR support remains useful |
| OpenXR 1.16.1 | Retain | Android Quest XR loader/input |
| `USE_SCENE` permission | Retain for now | live MRUK scene consumer; #47 must re-audit when production composition replaces the demo |
| Anchor permission | Retain | #49 session-only fixture anchor |
| Hand-tracking feature/permission | Retain as optional package/scene configuration | not an input requirement; right Touch Plus remains authoritative |
| Unity networking modules | Retain | no broad package deletion is justified by retiring one subsystem |
| QR requirements/adapters | Remove | replaced by ordered four-point fixture calibration (#49) |
| External firmware/protocol directories | Remove | no external device exists in the approved MVP |
| Sensor/magnet/coil/electromagnetic concepts | Remove | replaced by local geometric contact and Quest haptics |
| `HallSensorFrame` scene object | Remove | dead legacy scene frame |
| tracked `unity/.utmp` | Untrack and ignore | generated Android/Unity build state |

The Android manifest has no Internet/network permission. Passthrough, anchor and
scene permissions are retained for the consumers above. Core anchors do not
imply QR support.

## Issue disposition

| Issue | Disposition |
| --- | --- |
| #5 | Closed baseline only; #47 owns production composition |
| #6 | Preserve versioned content; re-owned by #46 |
| #7 | Superseded by #49; preserve session anchor, validity and local catalog concepts only |
| #8 | Preserve lifecycle; re-owned by #47/#57 |
| #9 | Preserve rigid tool transform and tracking health; re-owned by #48 |
| #10 | Retain core seam evaluator; extended by #53–#56 |
| #11 | Cancel: external firmware is outside approved MVP |
| #12 | Cancel: network protocol/connection manager is outside approved MVP |
| #13 | Superseded by #50–#52; retain fail-closed activation, reason reporting and release/re-arm |
| #14 | Preserve semantic feedback; integrate under #58 |
| #15 | Preserve visual/audio/Touch Plus feedback; remove external actuator assumptions; #58 |
| #16 | Retain deterministic coverage/bead responsibility; extend per process mode |
| #17 | Preserve local persistence; complete crash/replay requirements under #58 |
| #18 | Cancel: electromagnetic feedback is outside approved MVP |

## Open PR adoption/rework

| PRs | Decision |
| --- | --- |
| #22–#39 | Historical prototype stack. Keep useful rehearsal ideas only; do not promote two-point placement, old paths or legacy reset/recovery assumptions into production. |
| #40 | Rework. Keep geometry/evaluator intent; Domain must be pure/local, use radians and avoid UnityEngine/world-space ownership. |
| #41 | Rework. Keep explicit lifecycle/interfaces; replace scanning semantics and continuously validate calibration while running. |
| #42 | Candidate only. Preserve atomic local-write ideas but add event journal, crash recovery, replay and storage-exhaustion behavior under #58. |
| #43 | Adopt conceptually. Keep one semantic feedback source; integrate current safety/process reasons under #58. |
| #44 | Rework under #16. Coverage must derive from authoritative activation/process timing in local space, preserve gaps/chunks and not infer quality from presentation state. |

No PR is merged or closed by this migration. Owners must rebase/reconcile and
remove stale auto-close language before merge.

## Required validation evidence

- repository checks pass and reject `.utmp` plus retired runtime vocabulary;
- clean Unity import/compile and Bootstrap missing-reference inspection;
- Quest passthrough startup with no network service and only right controller;
- device evidence remains explicitly pending until performed.
