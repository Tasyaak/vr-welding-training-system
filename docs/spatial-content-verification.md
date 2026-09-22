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
| .NET 9 shared spatial suite | 43 scenarios passed, 0 failed |
| Unity 6000.3.24f1 EditMode | 3 tests passed, 0 failed; includes all 43 shared scenarios using JsonUtility |
| Unity imported meshes | Metre bounds, preserved outward winding, actual primitive/quaternion convention checked |
| Unity pre-build validator | Passed during preview export; unqualified-content warnings retained |
| Unity graphical preview export | Render succeeded; assembly, through holes and nominal QR recess inspected |
| Repository checks and diff whitespace | Passed before commit |

The preview is [committed with the authoring guide](spatial-content-authoring.md).
Raw local logs/XML are under ignored `artifacts/`; the suite and render command
are reproducible. No full Quest APK or device workflow is claimed by these tests.
Unity also reports existing Meta/package environment warnings; the spatial
assemblies compile and their tests pass. An unrelated editor-generated
DevAgentSettings change was removed rather than included in the PR.

## Content-input limitation

The supplied solid does not designate welding seams, direction or cleaning masks.
The production selection is explicitly missing and scored resolution rejects it.
Schema/bake support and tests for selected multiple seams, finite masks and
immutable export are implemented. An approved semantic selection remains needed;
this cannot be inferred from CAD edges or replaced by physical metrology.

## Physical work intentionally not performed

- #49: observed QR pose normalization, tracking/anchor lifecycle, registration workflow.
- #66: printed QR dimensions/quiet zone/thickness/placement, manufactured recess,
  bolted assembly deviations/repeatability, measured tool offsets, Quest error budget.
- #58: Group A/B mapping, production Bootstrap, complete training and device acceptance.

Before scoring, supply the semantic selection and qualified physical record.
The committed sample is useful for authoring/preview and unavailable for scored
registration. CAD identity does not certify the bolted part's physical pose.
