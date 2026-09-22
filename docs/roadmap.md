# Parallel implementation roadmap

Baseline: main ee8999eb4562c9f0c77e66b5b38acdfab1a7dea2.
The QR revision supersedes four-point registration while preserving the other
Quest-only decisions. Existing unmerged PRs are not implementation dependencies.

## Group A — spatial setup and physical assembly

| Issue | Deliverable | Blocked by (implementation) |
| --- | --- | --- |
| #46 | CAD/baked spatial content, marker/part binding, installed transforms | none |
| #48 | Single right-controller/HMD input and tool-offset adapter | none |
| #49 | Fixture QR registration, preview and unsaved anchor | #46 |
| #66 | QR installation and bolted assembly/device qualification | #46, #49 |

A owns a standalone spatial preview/test composition. It does not depend on
the training coordinator or process kernels. #48 uses a small adapter test
definition conforming to the contract rather than waiting for all authoring UI.

## Group B — training engine and experience

| Issue | Deliverable | Blocked by (implementation) |
| --- | --- | --- |
| #47 | Training contracts/profiles, coordinator and fake-platform composition | none |
| #10 | Directed seam evaluation | #47 |
| #50 | Virtual clamp, finite contact, unified activation | #47 |
| #51 | Reflection geometry and latched risk semantics | #50 |
| #52 | E-stop reducer/recovery through fake command port | #50 |
| #16 | Domain coverage and bead presentation | #10, #50 |
| #53 | Fusion integration | #10, #16, #50 |
| #54 | Wobble kernel/envelope | #53 |
| #55 | Pulsed kernel/windows | #53 |
| #56 | Cleaning coverage and virtual nozzle transaction | #53 |
| #14 | Shared semantic feedback/assistance | #47 |
| #15 | MR/audio/right-haptic sinks with fake device port | #14 |
| #17 | Local journals/results and storage-fault handling | #47 |
| #57 | Menu/input contexts using fake input and registration | #47, #50, #52, #56, #14 |

No A issue blocks B. Tests use synthetic finite geometry, marker-independent
registration snapshots and recorded/fake input commands. Device bindings and
full physical acceptance are not hidden prerequisites for those PRs.

## Join and release — #58

Blocked by: #46, #48, #49, #66, #51, #52, #54, #55, #56, #15, #17, #57.
Transitive dependencies cover the remaining core tasks.

#58 maps real spatial snapshots/content to training ports, switches Bootstrap
from the isolated demo to production, verifies schema/frame/time conformance,
and runs end-to-end QR→pre-clean→weld→post-clean→save/export/replay acceptance.
Only this integration task may claim the complete physical workflow is ready.

## Common contract, not a serial implementation gate

[Contract v1](parallel-development-contract.md) already defines data shapes,
units, generations, input mapping, ownership and conformance examples. Groups
may implement their own boundary DTOs and map them once in #58; there is no
mandatory shared-code issue delaying both groups. This does not permit duplicate
runtime activation authority or a production default-valid fake adapter.

## Issue consolidation

#6 → #46, #8 → #47 and #9 → #48 are redundant legacy trackers and are closed
with cross-links. #49 is edited in place from four-point to fixture-mounted QR;
#7 stays historical to avoid a second QR implementation tracker.
#45 remains the completed previous migration; only its QR decision is superseded.
#11/#12/#13/#18 remain retired hardware-era tasks. #10/#14–#17 retain their useful
scope with revised bodies and dependencies.

## Evidence

Every issue needs its own tests/PR evidence. Hardware-independent tests can
finish before Quest qualification; successful Editor/CI runs cannot claim
physical registration precision. Final acceptance requires both group suites,
real adapter contract tests, right-only controls, loss/recovery, export/replay,
sustained frame/memory budget and a reproducible content/OS/package record.
