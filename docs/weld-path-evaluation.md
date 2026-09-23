# Weld-path evaluation

`WeldPathEvaluator` is pure `WeldingTrainer.Domain` code. It has no Unity, Meta XR,
QR, controller, renderer, recorder, or activation dependency. It consumes schema-v1
samples that are already expressed in the Workpiece frame. Issue #58 owns the
validated mapping from real registration/right-tool data into this Workpiece-local
contract; the evaluator itself never grants process permission.

## Units and identity

All positions and distances are metres, monotonic time is seconds, speed is metres
per second, and angles are radians. Each `PathSample` carries session ID, attempt ID,
content hash, registration generation, and origin generation. They must match the
frozen `PathEvaluationContext`; unavailable input, mismatched identity/generations,
non-finite values, or invalid tracking fail closed and cannot produce valid metrics.

## Directed spline and projection

A `DirectedSpline` is a non-degenerate finite polyline. Arc length increases from
its first authored point toward its last. Projection searches near the previous arc
position once history exists, making normal forward traversal stable. Equal-distance
projections at the same adjacent-segment vertex are one location, not an ambiguity.
Equal-distance projections at different arc positions (for example, a
self-intersection) are `AmbiguousProjection` unless the prior arc position resolves
them.

`Projection.Progress` is current arc length divided by total spline length. The
value is instantaneous position, not accumulated coverage. Reverse traversal can
therefore decrease progress and is also reported explicitly with `MotionFlags.Reverse`.
Coverage ownership remains outside this evaluator.

## Seam-local error signs

For a valid projection:

- `tangent` is the directed spline tangent;
- `normal` is the authored outward Workpiece-local surface normal;
- positive lateral axis is `tangent x normal`;
- error vector is `tip - projectedPoint`;
- tangential, lateral, and normal errors are dot products against those axes;
- total error is the 3D Euclidean magnitude of the error vector.

At a clamped spline endpoint, tangential error therefore reports signed endpoint
overrun/underrun instead of being forced to zero.

## Speed, continuity, and filtering

Raw travel speed is signed directed-arc displacement divided by the actual monotonic
sample interval; no fixed rendering interval is assumed. `TooSlow`, `SpeedCorrect`,
and `TooFast` are classified from the absolute filtered speed against the frozen
policy limits. Reverse travel retains a negative signed speed and the explicit
`Reverse` flag.

Filtering is first-order exponential with
`alpha = 1 - exp(-dt / filterTimeConstant)`. The first valid derivative sample seeds
the filter directly, avoiding startup bias.

Tracking gaps, non-monotonic time, explicit `Reset()`, and large arc jumps reset
derivative/filter history. A large jump is reported as both `Discontinuity` and
`SkippedRegion`; the jump itself has invalid speed and becomes only the baseline for
the next sample, so velocity is never bridged across the discontinuity.

## Angles

Travel angle is the signed angle from the directed seam tangent to measured tool
motion about the authored outward surface normal:

`atan2(dot(cross(tangent, motion), normal), dot(tangent, motion))`.

It follows the right-hand rule about the outward normal. If motion magnitude is below
the configured minimum, `TravelAngleValid` is false.

Work angle is the acute unsigned angle between the tool-forward axis and the authored
surface normal:

`acos(abs(dot(normalizedToolForward, normalizedSurfaceNormal)))`.

A zero/undefined tool-forward vector makes only the work angle invalid; it does not
invalidate otherwise usable position metrics. An invalid surface normal invalidates
the seam-local metric sample because lateral/normal axes cannot be defined.

## Validity and authority

`PathMetrics.Valid` means the current geometrical path sample is coherent and
continuous. `SpeedValid`, `TravelAngleValid`, and `WorkAngleValid` are separate so a
first sample or an at-rest sample can still provide meaningful position error.
`PathMetrics` deliberately contains no activation/permission field. Process
activation remains owned by the authoritative training coordinator/interlock.

## Unity verification

Run `Window > General > Test Runner > EditMode` and execute the complete
`WeldingTrainer.Application.Tests` assembly, not only `WeldPathEvaluatorTests`.
The path tests cover straight/curved projection, shared vertices, self-intersection
ambiguity and prior-arc stability, endpoint/seam-local error signs, actual-time
speed/filtering and all three classes, reverse travel, skipped/discontinuous jumps,
gap/reset/non-monotonic history, identity/generation/input failure, non-finite data,
signed travel angles, work angles, and the absence of activation authority.

Actual Quest mapping and physical acceptance remain Issue #58. For this PR, Quest is
a build/regression smoke test only.
