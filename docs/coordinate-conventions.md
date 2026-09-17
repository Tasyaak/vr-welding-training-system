# Coordinate conventions

All runtime poses must name their coordinate space. Do not pass an unlabelled
`Vector3` or `Pose` across subsystem boundaries.

## Spaces

- **Tracking**: Quest/OpenXR tracking space. It may change after recentering.
- **World**: the active Unity scene space.
- **Workpiece**: a stable frame registered to the physical workpiece.
- **Tool**: the tracked controller or instrument frame.
- **Tip**: the welding tip frame used for seam evaluation.

The authoritative evaluation input is `Tip -> Workpiece`. Never score directly
in tracking or world space.

## Axes and units

Unity's left-handed convention is used inside Unity:

- `+X`: right
- `+Y`: up
- `+Z`: forward
- distance: metres
- time: seconds
- speed: metres per second
- angles shown to trainees: degrees

Workpiece origin and orientation must be defined by the workpiece asset. The
recommended convention is origin at the registration marker, `+Y` along the
workpiece normal, and `+Z` in the nominal seam-forward direction.

## Pose composition

For a point expressed in tool space:

```text
p_workpiece = T_workpiece_from_world
            * T_world_from_tool
            * T_tool_from_tip
            * p_tip
```

Name transforms as `destination_from_source`. Calibrate and persist
`T_tool_from_tip` per physical tool setup. At session start, capture the
registration transform and its quality metadata.

## Seam direction

A seam is an ordered curve in workpiece space. Progress increases from its
first control point to its last. Reversing a seam creates a distinct training
configuration; it is not inferred from the first motion sample.

Store the expected tool-forward vector and work angle relative to the local
seam tangent. Handle angular wrap-around explicitly and use quaternions for
pose composition.
