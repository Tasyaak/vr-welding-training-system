# Parallel development contract v1

Status: structural contract v1. #46 implements the spatial content subset;
registration, input and training records remain owned by their respective issues.
This document fixes interoperability now so two groups can implement and merge
independently. Class names below describe data roles; they are not claims about
classes already in main.

## Independent groups and file ownership

**A: #46, #48, #49, #66.** Own spatial content/catalog authoring,
CAD import/bake, Meta QR/anchor/input adapters and their standalone preview scene.
A is tested without the training coordinator, evaluator, interlock or renderer.

**B: #47, #10, #50–#57, #14–#17.** Own engine-independent training math/state,
process profiles/kernels, feedback, coverage, recording and UI. B uses committed
synthetic geometry, input/registration streams and fake output ports.
No A issue or real QR/CAD asset is an implementation/merge blocker for B.

**Join: #58.** Own production Bootstrap wiring, spatial-to-training adapter,
real content/profile resolution and end-to-end/device acceptance.

A works in Content/Spatial and Platform/Meta with a dedicated spatial test
composition. B works in Domain, Application, Content/Process, Infrastructure/Local
and Presentation with a dedicated training test composition. Each owns its test
fixtures. Do not simultaneously rewire Bootstrap; #58 owns its production switch.
The existing demo remains explicitly isolated.

This is independence of delivery, not separate sources of runtime truth.
Only B's coordinator grants simulated process activation; A never grants it.
A supplies measurement validity; a fake valid provider is test-only and excluded
from the real training build.

## Boundary representation

Use a versioned structural contract (schemaVersion=1), not a cross-group source
dependency before integration. A may expose SpatialSnapshot DTOs; B consumes
TrainingInput values through its own port. #58 supplies a thin validated mapping.
Both test against the examples below. Neither group copies the other's rules.
Prefer the same contract types later if practical, but do not create a shared
implementation prerequisite or parallel mutable session state.

| Record | Required fields / meaning |
| --- | --- |
| RigidPose | destination/source frame IDs, position metres, quaternion x/y/z/w, scale fixed 1 |
| RegistrationSnapshot | state, assemblyBindingId/version/hash, partId, fixtureId, WorldFromWorkpiece, registrationGeneration, originGeneration, observed/captured monotonic time, validity/reasons |
| RegistrationEvidence | marker ID/dimensions, normalized pose convention, observation count/time span/translation and rotation scatter, anchor lifecycle status, assembly confirmation; unavailable diagnostics explicitly null |
| ToolHeadSnapshot | right grip/tool/tip and head poses, per-pose position/rotation validity, capture time, origin generation, trigger analog and edges |
| Command | sequence, monotonic time, inputContext generation, type; EStopPressed/trigger/menu/A/navigation/system-invalid |
| GeometrySnapshot | content hash/version, finite surface patches with outward normals, directed seams/baked arc lengths, cleaning region IDs in Workpiece metres |
| ProcessConfiguration | schema/hash, mode, quality/contact/risk/timing/footprint parameters; B owns defaults and validation |

No vendor object, scene Transform, QR URL command, clock in unspecified units or
quietly defaulted-valid pose may cross the boundary. Registration state is
Unregistered/Acquiring/Preview/Anchoring/Registered/Lost/Disposed.
Marker payload is bounded versioned UTF-8 with a required partId; the local
catalog maps it to exactly one approved mounting configuration per partId for
this MVP. Duplicate active bindings invalidate the catalog; reject ambiguity,
never add a mount key or pick the first match. This does not authenticate parts.

A owns tracking-to-world and marker normalization. B transforms tip/head into
the workpiece frame using the same coherent snapshot. Reject mismatched origin
generation, unsupported schema, non-finite transforms, duplicate/reordered
commands and stale mandatory data. Domain uses monotonic double seconds (UTC
metadata only), metres and radians. Preserve capture-vs-source timestamp labels.

Input map: right trigger=requested process; B=global E-stop; A=submit/registration
preview confirmation; thumbstick=navigate; thumbstick click=menu/pause request.
Grip is reserved. No left action or Meta system button is required. Registration
and menu contexts suppress process activation; leaving either does not arm.
The demo's B/reset and generic trigger/UI-click bindings must not coexist with
production mappings.

## Conformance examples

1. All rotations identity: WorldFromMarker translation=(1,2,3),
   FixtureFromMarker=(0.1,0,0), FixtureFromWorkpiece=(0,0.008,0).
   Expected WorldFromWorkpiece=(0.9,2.008,3). This is a synthetic test, not CAD data.
2. A +90 degree rotation about +Z maps (1,0,0) to (0,1,0) in the chosen
   mathematical convention; adapters must document any Unity/CAD conversion.
3. Registration Registered/gen=7 with tool origin gen=8 vs registration origin
   gen=9 is unusable: output inactive with OriginMismatch.
4. EStopPressed and trigger press at identical time: E-stop wins; no coverage.
5. QR trackable disappears after anchor localization: same registration remains
   valid while anchor evidence is valid. Anchor loss immediately yields Lost.
6. A new part/assembly binding while Running never replaces active geometry;
   inhibit, finalize interrupted attempt and require explicit fresh registration.
7. Pause/recenter/rebolting cannot replay an old Armed state or old pulse epoch.

Each group commits tests instantiating these values and synthetic finite geometry.
#58 verifies actual A output maps to B input without unit/frame/ID changes.
Breaking schema changes require editing this document and both conformance suites;
unilateral field reinterpretation is not permitted.

## Implemented Group A content encoding

[Spatial authoring v1](spatial-content-authoring.md) specifies the concrete
JSON/immutable C# representation. Geometry carries source/content identity,
finite outward triangle patches, directed seam points/cumulative metre lengths
and triangulated pre/post masks linked to finite supports. Missing semantic
selection blocks scored resolution. Nominal poses remain separate from station
qualification. Lossless JSON and typed snapshots permit #58 mapping without
requiring Group B to reference Group A assemblies now.

## Release distinction

A and B can finish their own PRs using fakes and schema fixtures. The complete
MVP cannot ship without both real adapter qualification and the #58 safety,
recording and one-controller device tests. Independence must never become a
production bypass for missing QR, valid geometry or safety providers.
