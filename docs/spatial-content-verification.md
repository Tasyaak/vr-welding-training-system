# Issue #46 verification record

Implementation baseline: `24ff28d75b1a2903fb5716cbf4be6e7e52a2f119` (latest main
fetched before branch creation). Branch: `codex/46-spatial-cad-content`.
Current #46/#49/#66/#58 and boundary #47 were read through GitHub CLI. No old
unmerged implementation was used as the spatial architecture.

Verified locally on Windows x64, 2026-09-22/23:

| Check | Result |
| --- | --- |
| Exact supplied STEP SHA-256 compared with repository source | Both match the documented originals |
| OpenCascade 7.9.3 B-rep inspection | One valid solid in each file, explicit mm |
| Deterministic bake and `bake.py --check` | Byte-identical outputs with pinned Windows/Python 3.12 dependencies |
| Finite outward triangulation | Fixture: 1,548 triangles; workpiece: 1,552 triangles |
| Source/bake/derived content identity chain | Pass, including converter and authoring files |
| .NET 9 shared spatial suite | 48 scenarios passed, 0 failed |
| Unity 6000.3.24f1 EditMode | 4 tests passed, 0 failed; includes all 48 shared scenarios using JsonUtility |
| Unity imported meshes | Metre bounds, preserved outward winding, actual primitive/quaternion convention checked |
| Unity pre-build validator | Passed during preview export; unqualified-content warnings retained |
| Unity graphical preview export | Regenerated and visually inspected: holes north, actual recess/cyan outline lower-left, +Z joint side and green cleaning bands |
| Repository checks and diff whitespace | Passed before commit |

The preview is [committed with the authoring guide](spatial-content-authoring.md).
Raw local logs/XML are under ignored `artifacts/`; the suite and render command
are reproducible. No full Quest APK or device workflow is claimed by these tests.
Unity also reports existing Meta/package environment warnings; the spatial
assemblies compile and their tests pass. An unrelated editor-generated
DevAgentSettings change was removed rather than included in the PR.

## PR #77 correction audit

This update starts from PR HEAD `1c0adf59240d97290ca8c498c6ac2ef52e2749dc`,
not from the interrupted #49 branch. Its preserved commit
`bd9a02e3c01d8a5181d8840821e9d112f586f9f8` is unchanged and excluded.
The owner supplied the seam/direction/cleaning selection and physical print
specifications on 2026-09-23, recorded in `engineering/cad/semantic-selection.md`.
Production tests assert exact ordered B-rep endpoints, both adjacent supports,
full finite L bands, two unselected print alternatives and immutable metadata.
The workpiece now passes semantic validation; physical qualification still blocks scoring.

The camera correction preserves every fixture/visual coordinate and pose. The
old default view began on the mounting-hole side, and Unity LookAt reverses CAD
screen chirality for a +Y/-Z-north view. The explicit RH CAD camera and matched
culling convention fix both interactive/export views. Projection tests detect
left/right regressions independently of ordinary quaternion arithmetic tests.

## Physical work intentionally not performed

- #49: observed QR pose normalization, tracking/anchor lifecycle, registration workflow.
- #66: compare/select QR-PRINT-A/B; verify known print specifications and artwork,
  measure actual installed plane/placement and manufactured recess, bolted assembly
  deviations/repeatability, tool offsets and Quest/station error budget.
- #58: Group A/B mapping, production Bootstrap, complete training and device acceptance.

Before scoring, select one print and supply its qualified physical record.
The committed sample is useful for authoring/preview and unavailable for scored
registration. CAD identity does not certify the bolted part's physical pose.
