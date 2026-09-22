# Architecture migration record

## Completed previous migration

Main ee8999eb4562c9f0c77e66b5b38acdfab1a7dea2 includes #59 completing #45.
It removed firmware/protocol placeholders, ESP32 metadata, HallSensorFrame and
tracked .utmp output, and documented four-point registration. These are
historical facts; the earlier four-point/QR-ban decision is no longer current.

## Approved QR revision — 2026-09-22

Restore only fixture-mounted QR identification/pose registration. The welding
part is bolted to the fixture, and cataloged marker/assembly transforms establish
its frame. QR carries the part ID; its pose comes from MRUK, not from the ID.
Four-point touch registration is replaced, not retained as a fallback.

| Dependency / concept | Current disposition |
| --- | --- |
| Meta Core / MRUK 205.0.0 | Retain for rig, passthrough, QR and anchors |
| OpenXR 1.16.1 / right Touch Plus | Retain; one production input adapter |
| USE_SCENE / USE_ANCHOR_API | Retain; #49 validates permission/config/runtime support |
| Four-point solver/capture UI | No production implementation planned |
| QR payload, pose and marker→fixture binding | Required local registration path |
| Bolted fixture/part attachment | Required repeatable mechanical setup, explicitly confirmed |
| Virtual electrical clamp / geometric contact | Separate training states, no sensed hardware circuit |
| ESP32/Hall/magnets/coil/network protocol | Remain removed; do not restore old folders |
| .utmp | Remains ignored and rejected by hygiene checks |
| Persistent/cloud anchors or backend | Not required; session-only local registration |

No runtime code/config is changed by the documentation revision.

## Issue ownership

- #45 remains closed as the completed historical migration. Its QR exclusion is
  superseded by #49 and this record, not by restoring the original hardware design.
- #46 owns spatial content and assembly binding; #49 owns QR registration.
- #6/#8/#9 are closed as duplicates of #46/#47/#48 after explanatory comments.
- #7 remains closed historical; #49 is the only active QR implementation issue.
- #10/#14–#17 retain core responsibilities with current bodies.
- #11/#12/#13/#18 stay closed; #50–#52 provide current software safety.
- #66 covers the actual marker placement, mounting repeatability and
  station error budget.
- #58 joins the independent groups described in [roadmap](roadmap.md).

Existing unmerged PRs are intentionally excluded from planning. No adoption,
rework or merge dependency is assigned to them, and this task does not delete
them. The current main commit is the implementation baseline.

## Preserved invariants

Engine-independent Domain, one activation authority, right-controller-only
workflow, no runtime external device/network peer, deterministic timing,
versioned geometry and local replay remain unchanged. Mechanical bolts do not
reintroduce a physical electrical clamp. QR visibility is not continuously
required after a valid anchor is established, but moved/rebolted parts invalidate
registration. Unknown evidence fails closed.

Repository hygiene checks do not ban the word QR; they reject retired directory
prefixes, the Hall scene frame, generated files and unsafe local settings.
Keep these checks intact. Qualification must separately test QR permission,
tracking, accuracy and physical assembly conditions on Quest.
