# Coordinate and unit conventions

Unity world coordinates are a presentation/platform concern. Deterministic
evaluation uses fixture/workpiece-local coordinates.

## Frames

- `Tracking/World`: current Quest tracking space.
- `SessionAnchor`: session-only stable anchor owned by the platform adapter.
- `Fixture`: solved by ordered four-point rigid calibration.

The solver publishes `world_from_fixture`; authored `workpiece_from_fixture` is then composed to
produce `world_from_workpiece`. Scale is exactly one. See
[four-point-registration.md](four-point-registration.md) for acquisition and acceptance gates.
- `Workpiece`: authored offset under the fixture.
- `Tool`: rigid pose derived from the right controller.
- `Tip`: calibrated tool-local offset used for contact and seam evaluation.

Transforms are named `AFromB`: they convert coordinates expressed in B into A.
Persist the calibration generation with each sample or event that depends on it.

## Units

- distance: metres;
- time: seconds from a monotonic clock;
- velocity: metres per second;
- Domain angles and serialized numeric angle fields: radians;
- Inspector and trainee-facing UI angles: degrees, explicitly converted once at
  the boundary.

Do not infer a frame from scene hierarchy, use Euler angles in Domain, or apply
presentation smoothing to stored evaluator metrics.

The calibrated tool's backward axis follows the legacy tool-angle convention.
The incident beam direction used by reflection training points from the head
toward the contacted surface and is therefore the opposite directional concept;
neither is inferred from a seam tangent or surface normal.
