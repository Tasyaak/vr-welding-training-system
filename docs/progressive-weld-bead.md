# Progressive virtual weld bead

`SeamCoverageTracker` is the renderer-independent authority for bead evidence. It accepts only contiguous, attributable seam projections while the common #50 `ActivationDecision` is Active. Trigger release, inhibition, invalid tracking/registration generation, ambiguous projection, time gaps, and discontinuities close the current segment before any later traversal can continue.

Coverage is stored as exact seam-arc interval unions in Workpiece metres. Gaps remain absent rather than being stretched over. Each interval records visit count and quality. The explicit aggregation policy is **worst-ever quality wins**: an acceptable revisit increases visits but cannot erase an earlier poor interval. Attempted, acceptable, and poor lengths/fractions remain separate and can feed #17 metrics and #53 process behavior.

`ProgressiveBeadRenderer` is a replaceable Unity adapter. It rebuilds bounded, separate `LineRenderer` chunks from immutable snapshots and uses `useWorldSpace=false`. Attach it below the registered Workpiece root so anchor/root movement carries all bead vertices without rewriting domain coverage. Acceptable and poor materials are independent. The renderer never decides activation, quality, scoring, or coverage.

## Unity setup

1. Add `ProgressiveBeadRenderer` to an empty child of the session Workpiece root—not the QR marker or world root.
2. Assign transparent/opaque training materials for acceptable and poor coverage, set width in metres, and keep `maximumChunks` at or above the profile’s bounded interval limit.
3. Call `Configure` with the selected seam in Workpiece-local metres after binding the attempt.
4. Feed each evaluator result plus the authoritative activation decision into `SeamCoverageTracker.Update`; pass the immutable returned snapshot to `Apply`.
5. Call `Stop` on attempt finish, E-stop, teardown, or process epoch cancellation. Do not synthesize points from a maximum-progress scalar.

This bead visualizes training coverage and quality only. It does not model molten metal, penetration, heat, flow, or material physics. Production root binding and Quest anchored-alignment acceptance belong to #58.
