# Spatial content authoring and schema v1

Issue #46 supplies independent Group A content. These assemblies do not reference
Group B, Meta tracking, the prototype, a coordinator or process profiles.
Bootstrap remains unchanged. #49 consumes catalog data; #58 maps immutable
snapshots into training; #66 supplies physical installation evidence.

## Source, authoring and derived assets

| Location | Purpose |
| --- | --- |
| `engineering/cad/source/*.step` | Original engineering exports, outside Unity Assets |
| `engineering/cad/authoring.json` | Versioned fixture/part/marker/binding/tool definitions |
| `engineering/cad/selections.json` | Explicit semantic selection tied to source identity |
| `tools/cad/requirements.txt` | Pinned authoring-only OpenCascade dependencies |
| `tools/cad/inspect_step.py` | B-rep validity, units, solid and face inspection |
| `tools/cad/bake.py` | Tessellation, seam arc-length bake, metre export and provenance |
| `unity/Assets/Trainer/Content/Spatial/Generated` | Visual JSON, finite geometry JSON and `.spatial` catalog |
| `Content/Spatial/Runtime` | Engine-neutral records, validation, rigid math, immutable snapshots |
| `Content/Spatial/Unity` | Imported ScriptableObject and Unity serialization adapter |
| `Content/Spatial/Editor` | Importer, build validation and independent orbit preview |
| `Content/Spatial/Tests/Editor` | Shared schema cases and Unity import/serialization tests |
| `tests/Spatial.Tests` | Same pure C# cases on .NET 9 without Unity or NuGet dependencies |

The STEP files total less than 80 KB: ordinary Git is appropriate, without LFS.
`.gitattributes` treats STEP as opaque engineering bytes (no text conversion or
line merge) and fixes LF for hashed text.
Never rewrite source headers or line endings. SHA-256 identifies bytes; it does
not authenticate a physical part, author or approval.

## Reproduce the bake

Baseline: Windows x64, CPython 3.12, `cadquery-ocp-novtk==7.9.3.1.1`
(OpenCascade 7.9.3 bindings), pinned proxy package, Unity 6000.3.24f1.
No CAD leaves the machine. Quest never loads STEP, Python or OpenCascade.

```text
python -m venv .venv
.venv/Scripts/python -m pip --isolated install -r tools/cad/requirements.txt
.venv/Scripts/python tools/cad/inspect_step.py
.venv/Scripts/python tools/cad/bake.py
.venv/Scripts/python tools/cad/bake.py --check
dotnet run --project tests/Spatial.Tests -- .
python tests/repository_checks.py
```

Use the actual Python executable when `python` is not on PATH. The local Windows
rebake compares every output byte. Other platforms/kernel versions may
change tessellation ordering: review geometry/provenance before a baseline change.

Import verifies one valid solid and explicit millimetres per source. Meshing is
single-threaded, with absolute chord deflection 0.05 mm and angular deflection
0.1 rad. Normals follow B-rep face orientation. Closed-solid validation checks
paired edge orientation and positive signed volume. Metre components round to
10 decimals (at most 0.05 nanometres per component); unit normal tolerance is
1e-6. Mesher deflection is a requested approximation tolerance, not a measured
physical accuracy or certified Hausdorff bound. Hole walls are faceted; later
scoring tolerances must account for proxy error.

Visual and evaluation artifacts are separate, currently from the same complete
surface triangulation. Holes/recesses are retained instead of infinite planes or
speculative simplified collision proxies. No CAD edge automatically becomes a seam.

The catalog records source SHA-256/revisions, converter/version, axes/pivot,
import poses, unit/deflection settings and visual/geometry hashes. `bakeInputs`
also hashes scripts, requirements, authoring and selection files. Snapshot
SHA-256 covers exact UTF-8 catalog bytes including those transitive identities;
geometry hashes cover exact geometry bytes. There is no self-referential hash.
Changes require a rebake and intentional revision review. Editor/build validation
checks original sources; runtime checks packaged artifacts without source CAD.
Repository checks catch changed inputs outside Unity Assets and run the pure
C# suite through the existing GitHub Actions entry point. This requires a stable
.NET SDK 8 or later; it selects the highest installed SDK without adding package
dependencies. The standalone project defaults to the locally verified .NET 9.
Full OpenCascade rebaking is an explicit authoring check, not a claimed CI run.

## Coordinates and marker region

The shared CAD origin remains the Fixture and Workpiece origin. Numeric `(x,y,z)`
components remain unchanged. Identity import rotation/pivot and one multiplication
by `0.001` during vertex export convert mm to m. No GameObject scale is involved.
Wire rigid scale must equal 1; runtime poses have no scale degree of freedom.
Quaternion order is `(x,y,z,w)`.

The structural convention is `AFromB`, `q*v*q^-1`, ordinary component cross
products, and +90 degrees about +Z mapping +X to +Y. Tests compare this with
Unity's numerical vector/quaternion and primitive mesh conventions. Import
preserves components, outward winding and normals without an additional axis
reflection. #49 separately normalizes MRUK marker axes/origin/dimensions.

For the supplied part, `FixtureFromWorkpiece` is nominal identity based on the
confirmed design, not an inference from matching Y extents. General nonidentity
poses have separate composition/inversion tests.

The region and nominal Marker origin are `(-0.105,0.0076,0.105)` m, with local
+X = Fixture +X, +Y = Fixture -Z, +Z = Fixture +Y. The quaternion is
`(-sqrt(0.5),0,0,sqrt(0.5))`; width/height are 0.09 m. This maps onto pinned
fixture face 14, X [-0.15,-0.06], Z [0.06,0.15]. Surrounding top Y is 0.008 m.
The in-plane orientation is an authored nominal choice, not evidence of an
installed printed label.

Initial `FixtureFromMarker` is that floor pose, explicitly unqualified. #66 must
incorporate measured label/plate thickness and installation offset/orientation
in a new revision. Recess size is not QR print size. Unknown wire measurements
use explicit known flags and zero sentinels; immutable marker/tool snapshots
expose unknown values as null, never usable identity offsets or zero dimensions.

## Actual content and unavailable semantic input

Both supplied bodies are fully tessellated in their nominal assembly, with
versioned source face/triangle IDs, finite surfaces and outward normals. The CAD
recess, one active PART-001 binding, versions, confirmation and invalidation
instructions are authored.

**The fused T-shaped STEP solid and current repository do not establish selected
weld joints, pass direction, or pre/post-cleaning masks.** `selections.json`
therefore contains empty selections and `MissingAuthoritativeSelection` with a
specific explanation. No guessed fillet path or whole-part mask is approved.
This is missing semantic source input, separate from #66 physical metrology.
Obtain an approved drawing or explicit content-author selection before scoring.

To author an approved seam, supply points in Workpiece metres, one `surfaceId`
per segment and `authoringEvidence`. Split at triangle boundaries; bake computes
cumulative arc lengths from zero. The true supporting outward normal remains
separate from any joint bisector. Multiple directed seams are supported.
Cleaning masks contain oriented triangles, one support ID per triangle,
`PreWeld`/`PostWeld` phase and evidence. All vertices must lie in the convex finite
support, which also ensures edge/interior containment. Re-author selections after
source topology changes. `Approved` requires seams and both mask phases.
Synthetic cases test these capabilities independently of the unapproved specimen.

## Authoring, preview and fail-closed use

Edit source authoring/selections, bump relevant revisions, bake, then open Unity.
The `.spatial` importer validates and creates `SpatialCatalogAsset` with packaged
JSON and metre Mesh subassets. Unity generates the committed `.meta` files.
Invalid content fails import/build with field/ID diagnostics, without normalization.

Use **Trainer → Spatial → Validate all source and baked content** and
**Trainer → Spatial → Inspect supplied CAD assembly**. The independent preview
uses no scene, coordinator or QR observation. Drag to orbit: grey fixture, gold
workpiece, cyan nominal recess perimeter. The perimeter gets a display-only
0.05 mm lift against z-fighting. No unknown printed QR rectangle is drawn.
Compare holes, floor/top height, common origin and axes against the source report.

![Nominal CAD assembly](images/spatial-assembly.png)

This Unity render shows the supplied nominal CAD only, including the three
through holes and the cyan recess perimeter. It is not a photographed or
physically qualified installation. Reproduce it with **Trainer → Spatial →
Export nominal CAD preview PNG** (output under ignored `artifacts/`).

`SpatialCatalogAsset.Freeze()` returns a catalog with read-only, defensively
copied collections and vector/quaternion value copies. `ResolveForPreview`
allows inspection of valid unqualified content; `ResolveForScoredRegistration`
rejects unknown/malformed IDs, missing station approval, unknown marker dimensions
or plane, and missing workpiece semantics. Duplicate active part bindings reject
the entire catalog. This MVP has one mount per `partId`; no mount key, arbitrary
pose or downloaded configuration is in QR. #49 owns `LW1:partId` parsing/tracking.

Qualification strings are local evidence references, not cryptographic approval.
Physical controller/tool/tip setup is independent of virtual nozzle labels;
unknown offsets cannot be used. #48/#66 supply their measurements.

Valid unqualified assets may build for inspection; the scored-resolution gate
stays closed. #58 must use it, freeze hashes per attempt and retain Group B's
activation gates. This module never grants activation or publishes Registered.

## Verification and physical limits

Run repository/pure tests above. Unity Test Runner can run the
`WeldingTrainer.Content.Spatial.Tests` EditMode assembly, or:

```text
Unity -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testFilter WeldingTrainer.Content.Spatial.Tests -testResults artifacts/spatial-editmode.xml -logFile artifacts/spatial-unity.log
```

Tests cover actual source/mesh/proxy hashes and bounds, Unity serialization and
import, contract translations, nontrivial rotations/translations, frame direction,
scale/nonfinite data, IDs/revisions/duplicates, unknown metadata, recess support,
outward normals/closed solids, finite seams/masks, immutable exports and corruption.
The synthetic test assembly is Editor-only, excluded from runtime content.

No automated result measures manufactured dimensions, printed label placement,
thickness/quality, rebolting, controller tip or Quest/anchor errors. #66 requires
physical evidence using #49; #58 owns complete training and Group A/B mapping.
