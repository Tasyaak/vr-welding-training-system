# Fusion process kernel

Fusion is the baseline continuous simulated process. `FusionProcessKernel` consumes #10 path metrics, the authoritative #50 activation decision, and #16 seam coverage. It never projects its own path, grants permission, or fills to maximum progress.

The frozen `FusionProfile` identifies the seam/profile version and sets the start/end gates, attribution step and time gap, speed range, lateral/normal and orientation tolerances, completion gap tolerance, and visualization footprint width. These are project-authored training parameters. Assistance and renderer settings do not modify them.

An interval contributes only when both timestamped endpoints are valid, coherent, continuous, inside the start policy, and authoritative output remains on. Release, inhibition, generation change, invalid projection, excessive time gap, or excessive arc jump closes the epoch and creates no bridge. Trigger requests while blocked accumulate separately. Poor but attributable intervals add attempted/poor coverage; acceptable intervals additionally pass all frozen quality checks. #16’s worst-ever policy prevents a later good revisit from erasing poor evidence.

Completion requires the end gate plus contiguous acceptable coverage from the seam start within the declared gap tolerance. Merely touching progress 1 is insufficient. Deltas include attempt, Fusion mode, profile, seam, registration generation, activation epoch, arc/time bounds, and quality. Metrics preserve actual time-weighted speed, lateral, normal, travel-angle, and work-angle errors independently from rendering.

## Unity/#58 setup

Construct one kernel per attempt using the selected Workpiece-local seam and immutable Fusion profile. Feed it exactly once per consumed timestamped evaluator/interlock snapshot. Send returned coverage snapshots to `ProgressiveBeadRenderer`, record deltas/metrics, and call `Stop` on release, suspension, E-stop, attempt completion, or teardown. Do not tick the kernel from presentation interpolation frames.

The bead and footprint are training/process visualizations only. They do not predict energy deposition, heat, melting, keyhole formation, penetration, metallurgy, or real weld quality.
