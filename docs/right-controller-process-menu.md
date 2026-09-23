# Right-controller process menu (issue #57)

The process menu is a neutral, snapshot-driven view over authoritative training state. It never stores an independent selected process, clamp, nozzle, fault, or activation state. Every displayed change comes from an acknowledged `ProcessMenuSnapshot` returned by `IProcessMenuPort`.

## Right-controller mapping and contexts

| Control | Closed/training context | Open menu context |
| --- | --- | --- |
| Thumbstick click | Request menu; first pause/disarm | Close menu disarmed |
| Thumbstick axis | Training adapter use | Navigate or adjust focused bounded parameter |
| A | Training adapter use | Submit focused operation |
| B | Emergency stop | Emergency stop, even while a widget has focus |
| Trigger | Process activation only | No UI binding |

Opening increments a context generation, requests authoritative pause/disarm, and keeps widgets disabled until a non-Armed/non-Active snapshot is observed. Controls must return to neutral before the new context accepts an edge. Closing also requires neutral release and leaves activation disarmed; a fresh explicit arm is required. Stale command responses are rejected.

## Required operations

The production `IProcessMenuPort` in #58 maps menu operations to the authoritative services for fixture/workpiece and seam selection, QR registration/re-registration, mode and profile selection, bounded parameters with visible units, virtual clamp, the explicit nozzle transaction, acknowledgement/reset, arm/start/finish/retry/end-session, result/saving status, and assistance. It must reject profile/mode edits while Armed or Active and mark missing kernels or cleaning targets unavailable instead of presenting them as usable.

Nozzle replacement is not a label edit. Dispatch `BeginNozzleChange`, wait for `ChangePending`, then dispatch `ConfirmNozzleChange`; faults cancel the transaction. Clamp and E-stop commands similarly become visible only through the next authoritative snapshot.

## Unity setup

1. Create a small head-relative or safely placed world-space `ProcessMenu` panel that does not cover the workpiece.
2. Add `ProcessMenuPanel`; assign TextMesh objects for heading, content, mandatory safety status, and command response plus a project-owned translucent background.
3. The #58 composition root creates `ProcessMenuController` with its adapter-backed `IProcessMenuPort`, converts only right-controller edge events from #48 into `UpdateInput`, and calls `Render` after input or snapshot changes.
4. Do not bind the trigger to UI submit and do not add a left-hand ray or controller dependency. B must call `EmergencyStop` directly before any widget/back handling.
5. Keep mandatory operation and safety text visible at 0% assistance. On lost pose, hide stale spatial guidance but retain readable nonspatial status.

## Quest acceptance evidence

Record screenshots/video showing open-during-active pause acknowledgement; all five modes; bounded profile values and units; clamp and nozzle workflows; complete interlock list; E-stop while a field is focused; reset then explicit re-arm; saving/results/retry/end-session; trigger non-click-through; left controller powered off; and readable placement without obscuring the real fixture.
