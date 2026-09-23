# Fixture CAD and QR registration specification

## Supplied source evidence

The user supplied two separate SolidWorks 2024 STEP exports on 2026-09-22.
The exact bytes are committed in `engineering/cad/source`, outside Unity Assets.
The shared coordinate system is the confirmed nominal assembly. #46 supplies
converted finite content; physical installation remains unqualified.
See [authoring and reproducibility](spatial-content-authoring.md).

| File | Schema | Solids | Explicit B-rep vertices | Length unit |
| --- | --- | --- | --- | --- |
| fixture.step | AP203 / CONFIG_CONTROL_DESIGN | 1 | 26 | millimetres |
| welded_part.step | AP214 / AUTOMOTIVE_DESIGN | 1 | 28 | millimetres |

SHA-256:
- fixture.step: `F4532E591B0D7150A2988378A24E992B930580CA06074AF60D434A7F6542D04C`
- welded_part.step: `647E8CE51CE0F02CDA545677B969D484517B526841DC4E88A75A8829103F1117`

Structural STEP inspection resolves VERTEX_POINT references to CARTESIAN_POINT;
the following are vertex extents, not certified curved-solid bounding boxes:

| File | X range mm | Y range mm | Z range mm |
| --- | --- | --- | --- |
| fixture.step | -150 to 150 | 0 to 8 | -150 to 150 |
| welded_part.step | -94.467466 to 90.278222 | 8 to 65.5 | -75.547106 to 14.452894 |

Both contain cylindrical surface radii approximately 3.25 mm. This does not
establish thread type, bolt specification, number of fasteners or seating fit.
The project owner confirms one prescribed bolted mounting in the shared export
coordinates. `FixtureFromWorkpiece` is nominal identity. Matching vertex heights
alone was not the basis for this decision. Pinned OpenCascade conversion validates
both B-rep solids and outward closed triangulations; it does not certify physical
manufacturing tolerance or repeatability.

The fixture has a 90 × 90 mm QR recess: X [-150,-60] mm, Z [60,150] mm,
floor Y=7.6 mm, surrounding top Y=8.0 mm. Its versioned origin is (-105,7.6,105)
mm; local axes are +X, -Z, +Y in Fixture. Nominal Marker lies on this floor.
Two alternative white labels are 90 × 90 mm and 0.1 mm thick, with centered
53 mm (A) or 63 mm (B) symbols excluding quiet zone. Both encode `LW1:PART-001`.
Neither is selected; artwork provenance is unavailable. Actual installed plane,
placement error and print performance remain unqualified under #66.

## Authoring ownership (#46)

The implemented reproducible CAD conversion/bake workflow keeps source hashes,
converter/version, mm-to-m conversion and CAD-to-authored axes/pivots.
Do not require runtime STEP parsing on Quest. Store visual mesh separately from
bounded surface/joint proxies, seam curves and cleaning masks used for evaluation.
Author actual weld seams: neither filenames nor generic CAD edges identify the
intended weld path automatically.

A versioned assembly binding contains:
- partId and part definition version, fixtureId/revision and mount revision;
- marker payload schema/binding ID, physical dimensions and print convention;
- FixtureFromMarker and FixtureFromWorkpiece, rigid and unit-scale;
- visual/proxy/normal/seam/cleaning asset hashes and setup instructions.

The QR is on the fixture and carries the welding-part ID. A reused fixture must
have a label/catalog binding matching the actual installed part and revision.
IDs do not authenticate hardware. Require explicit operator confirmation of the
mounted part and fasteners before accepting registration; changing parts,
loosening/rebolting, moving the fixture or relocating the QR invalidates it.

The bolts are mechanical attachment. Virtual WorkpieceClampState is a simulated
preparation/contact-circuit state; the project does not add a sensed physical
electrical clamp.

## QR registration (#49)

Use installed MRUK/Core 205.0.0 through a project-owned adapter. Validate actual
QRCodeTrackingSupported/configuration, spatial permission and Scene/Anchor setup
on standalone Quest. Do not transplant a vendor sample's left-controller UI.
No raw-camera computer-vision pipeline or URL execution is required.

Resolve payload and pose from the same trackable, normalize axes/origin,
collect stable multi-frame observations, reject stale/outlier/implausible data
and verify known printed dimensions. Average rotations correctly, not Euler
angles. Use configured translation/rotation scatter, observation age/count,
view/size gates and explicit rejection reasons. Never report unavailable SDK
confidence as a measured value.

Compute the rigid transform using the [frame equations](coordinate-conventions.md).
Show a ghost assembly plus part/fixture revision; confirm placement and mounted
part, then create/localize an unsaved anchor. Registration is not valid before
both checks succeed. No scale fitting, four-point tool touches or hidden manual
pose fallback are permitted.

After acceptance, anchor-based placement continues without QR visibility.
If a conflicting QR pose is observed, suspend rather than snap geometry.
QR read failures before registration block; after registration marker occlusion
alone does not. Anchor loss, origin uncertainty or reported assembly movement
always invalidates output. New session requires a fresh QR registration.

## Physical qualification (#66)

Determine and record, rather than invent:
- comparison and selection of QR-PRINT-A/B, verification of their specified
  dimensions/thickness and print quality, and actual installed plane/offset/orientation;
- physical deviations from nominal identity and repeatability after rebolting;
- repeatability and absolute error across independent check positions, viewing
  distances/angles, lighting and controller tip offsets;
- pinned Quest OS/package tuple, offline capability, drift/recenter/occlusion behavior.

The project owner selected the +Z-visible T-joint at Z=-44 mm, travel +X, and
15 mm cleaning bands on faces 4 (+Y) and 13 (+Z). The explicit selection is in
`engineering/cad/selections.json`; `semantic-selection.md` records its authority.
The bake derives the finite endpoints and clips both PreWeld/PostWeld regions.

Stable marker observations do not prove absolute spatial accuracy. Compare the
combined marker placement, assembly fit, anchor drift and tool-tip error budget
against scoring tolerances. Block qualification if that budget is insufficient;
do not widen scoring tolerances silently to make a test pass.

## Official references

[Meta QR tracking](https://developers.meta.com/horizon/documentation/unity/unity-mr-utility-kit-qrcode-detection/)
documents pose/payload, Scene/Anchor setup and spatial permission.
[Spatial data permission](https://developers.meta.com/horizon/documentation/unity/unity-spatial-data-perm/)
describes the permission lifecycle. Installed package code confirms
MRUK.TrackerConfiguration, QRCodeTrackingSupported and TrackableAdded/Removed;
qualify runtime behavior instead of assuming the SDK version guarantees accuracy.
