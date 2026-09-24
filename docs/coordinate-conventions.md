# Coordinate and unit conventions

## Named rigid frames

Use `AFromB` to map coordinates from B to A. All runtime transforms are rigid:
unit scale, normalized rotation, finite translation. Domain uses metres,
monotonic seconds and radians; UI may show millimetres/degrees with explicit
conversion. Convert STEP millimetres to metres exactly once during authoring.

| Frame | Meaning |
| --- | --- |
| Tracking | XR origin with a generation that may change on recenter |
| World | Unity presentation space derived from Tracking |
| Marker | Normalized QR frame with documented origin, axes and printed dimensions |
| Fixture | Authored stationary fixture frame |
| Workpiece | Authored bolted-part frame; all seams/surfaces/coverage live here |
| Anchor | Unsaved session anchor managed by the platform |
| Controller | Right Touch Plus grip-pose convention |
| Tool / Tip | Rigid tool and effective tip, with versioned measured offsets |

Marker normalization is performed once in the Meta adapter. Document MRUK
origin/axes, corner order, normal direction and whether dimensions include the
quiet zone. Never guess these from a visually plausible overlay.

The [#49 adapter](qr-registration.md#capability-permission-and-coordinate-normalization)
encodes World and other pure poses in the RH basis used by authored content.
Unity presentation is a distinct numeric encoding of the same physical space:
`S_z = diag(1,1,-1)` changes vector basis and `S_z R S_z` changes ordinary poses.
MRUK marker normalization additionally accounts for its reflected local X. Runtime
visual copies convert vertices/normals/winding; source content and rigid scale stay
unchanged. Consumers must not pass raw Unity vectors into this RH contract.

## Registration composition

The catalog contains `FixtureFromMarker` and `FixtureFromWorkpiece`.
The latter describes the part's installed pose, not an arbitrary mesh pivot.

```text
WorldFromFixture(candidate) = WorldFromMarker(observed) * inverse(FixtureFromMarker)
WorldFromWorkpiece          = WorldFromFixture * FixtureFromWorkpiece
AnchorFromFixture          = inverse(WorldFromAnchor) * WorldFromFixture(candidate)
WorldFromFixture(now)      = WorldFromAnchor(now) * AnchorFromFixture
WorldFromTip               = WorldFromController * ControllerFromTool * ToolFromTip
WorkpieceFromTip           = inverse(WorldFromWorkpiece) * WorldFromTip
```

Marker payload selects a versioned assembly binding; it does not supply a
trusted arbitrary transform or scale. Require payload/pose association from
the same trackable. Do not fit scale or perform a four-touch-point solve.

QR candidate observations must be coherent in time/origin. After acceptance,
the anchor owns the placement. Do not chase QR updates during an active pass.
A newly observed conflicting placement invalidates/suspends rather than snaps
the scored geometry. Marker occlusion alone is not anchor loss.

Changing/rebolting the part, moving the fixture or changing the marker requires
explicit invalidation and a new registration generation/attempt. A session anchor
does not detect workpiece movement. Recenter may preserve the frame only through
a verified coherent origin transformation; otherwise re-register.

## Geometry and axes

Keep CAD-import-to-authored-frame transforms explicit and versioned. The supplied
exports have confirmed nominal identity `FixtureFromWorkpiece`; physical
deviations still require #66. #46 preserves numeric CAD axes/origin and converts
millimetres exactly once in the bake. The recess defines nominal Marker origin
(-0.105,0.0076,0.105) m, axes (+Fixture X, -Fixture Z, +Fixture Y), and footprint
0.09 × 0.09 m. Two candidate labels are 90 × 90 × 0.1 mm, with centered
53/63 mm symbols excluding quiet zone. Neither is selected; installed plane and
placement remain unqualified. Known thickness does not move the nominal floor pose.

The CAD inspector uses an explicit right-handed view matrix: from +Y with -Z
north, screen-right is +X and the recess is lower-left. Unity default LookAt
uses the opposite screen-right for this view. The inspector corrects view and
rasterizer parity together; source coordinates, mesh data and rigid poses stay
unchanged. Numeric quaternion/cross-product agreement alone does not prove
physical screen chirality. #49 must test the physical MRUK-to-authored basis,
including marker corner order, outward normal and asymmetric recess placement;
a reflection cannot be hidden inside a rigid quaternion.
See [concrete import/normal conventions](spatial-content-authoring.md).

A seam frame uses directed unit tangent t, authored outward surface normal n
and lateral b=normalize(n cross t), with degeneracies rejected. Retain the real
surface normal separately from any joint bisector. Define the tool axis back
toward the tool body; incident beam direction points toward the surface.
Store quaternion order (x,y,z,w), transform direction, units and validity in the
boundary schema. Test handedness conversions with known poses.

Never score Unity world coordinates directly or let display smoothing change
recorded evidence. Each dependent sample carries registration/origin generation.
