# Quest MVP content authoring

Issue #46 introduces two compiler-enforced layers:

- `WeldingTrainer.Domain`: immutable engine-independent definitions and validation;
- `WeldingTrainer.Content`: Unity ScriptableObject authoring and explicit conversion.

## Create the sample catalog in Unity

1. Open the existing `unity` project in Unity `6000.3.24f1`.
2. Wait for compilation and open **Tools → Welding Trainer → Create or Replace
   Sample Quest MVP Catalog**.
3. Select
   `Assets/Trainer/Content/Workpieces/SampleQuestMvpCatalog.asset`.
4. Run **Tools → Welding Trainer → Validate Selected Quest MVP Catalog**.
5. The Console prints both the complete content hash and evaluation-only hash.
6. Commit the generated `.asset` and `.meta` together after reviewing dimensions
   against the physical fixture.

The generated sample contains four ordered planar fixture points, one bounded
rectangular surface, two directed seams, paired pre/post-clean regions, one
right-controller tool, and exactly one profile for every process mode.

## Inspector rules

- All positions, dimensions, tolerances and footprints are metres.
- All durations are seconds and frequencies are hertz.
- Inspector angle fields are explicitly labelled degrees. Baking converts them
  to Domain radians.
- `Authored Scale` must remain `(1,1,1)`. Apply imported model scale before
  authoring geometry.
- Surface boundaries are finite ordered convex polygons with unit outward
  normals and approach side `+1` or `-1`.
- Seam baked points are directed, non-duplicated, lie on/inside their referenced
  finite surface, and include a declared approximation error below the tightest
  scoring tolerance.
- A surface normal is not inferred from seam direction or joint bisector.
- Tool contact-tip geometry is independent of virtual nozzle labels.
- Profiles are training-model settings, not real machine commands.

## Attempt startup

Production composition must call `QuestMvpCatalogAsset.FreezeForAttempt()` once
after catalog selection. Store the returned `ContentSnapshot`, complete content
hash and evaluation hash with the attempt. Never retain mutable authoring lists
as runtime state.

Changing assistance changes the complete content hash but not the evaluation
hash. Changing geometry, calibration, tool or process evaluation settings
changes both.

## Validation failures

`Bake()` and `FreezeForAttempt()` throw `ContentValidationException` containing
field-level paths. Do not catch and continue with defaults: show the reasons to
the operator and keep session/process activation inhibited.

Common failures include duplicate IDs/points, non-finite values, collinear or
insufficiently spread fixture points, non-unit normals, unknown surface/seam
references, zero seam tangents, invalid arc length, out-of-bounds seam samples,
unsupported scale, missing process modes, and unknown schema versions.
