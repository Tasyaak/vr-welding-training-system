# Development and Unity setup

## Pinned environment

Unity Hub + Unity 6000.3.24f1, Android Build Support/OpenJDK/SDK/NDK,
Core/MRUK 205.0.0, OpenXR 1.16.1, Input System 1.20.0, Git and a C# editor.
Use a developer-enabled Quest 3, right Touch Plus mock tool, stationary fixture,
bolted part and the correctly installed fixture QR for device acceptance.

Open the existing unity directory and Bootstrap scene. Restore locked packages,
select Android/OpenXR, run Meta Project Setup Tool and inspect missing references.
Run `python tests/repository_checks.py` from the repository root.

## Current main versus required production wiring

Bootstrap already contains OVRCameraRig, MRUK, OVRPassthroughLayer and
PrototypeDemo. It is a demo composition; the presence of MRUK does not prove
that production QR registration is enabled or implemented.
The #46 implementation changes no production scene/package/input asset.

#49 must inspect current Scene/Anchor settings and spatial permission, enable
MRUK QR tracker configuration and check actual QRCodeTrackingSupported state.
USE_SCENE and USE_ANCHOR_API are present in the current Android manifest.
Follow the pinned package APIs and [Meta QR setup](https://developers.meta.com/horizon/documentation/unity/unity-mr-utility-kit-qrcode-detection/);
do not add raw-camera access solely to decode QR through MRUK.
Vendor sample left-controller controls are not part of the production UI.

## Assets and independent test scenes

A (#46/#48/#49) supplies versioned fixture/part/marker bindings, import scale,
finite proxies and a standalone QR/input preview. B supplies process profiles,
synthetic surfaces and a fake-platform training test composition.
Both implement [contract v1](parallel-development-contract.md); neither waits
for the other group's scene or source types.

#58 wires the real adapters into one production root, disables prototype
evaluation, routes A/menu navigation without trigger click-through, preserves
global B/E-stop and connects one semantic feedback/recording pipeline.

For the supplied fixture, #46 authors the CAD recess and nominal identity part
transform and the selected T-joint/cleaning bands. Both QR print specifications
are known (90 mm labels, 0.1 mm thick, 53/63 mm symbols excluding quiet zone).
#66 compares/selects one and qualifies its installed plane, placement and
manufactured assembly deviations. Do not infer measurements from filenames.
Use [spatial authoring](spatial-content-authoring.md) for local conversion,
independent preview, validation and tests. Confirm the actual part
and fastening, validate ghost placement and obtain a localized unsaved anchor
before enabling preparation/arming. There is no four-touch-point workflow.

## Device procedure and evidence

1. Record Unity/package/Quest OS/build/content versions and station binding.
2. Launch offline; grant/deny/regrant spatial permission and test unsupported QR.
3. Read the known QR, verify selected part/revision, reject unknown/ambiguous IDs.
4. Validate overlay and assembly confirmation, then accept/localize the anchor.
5. Occlude QR: registered placement remains valid while the anchor is valid.
6. Lose anchor/tracking or recenter incoherently: output inhibits, with explicit
   recovery; fixture movement/rebolting requires fresh registration.
7. Test all modes, nozzle/clamp, menu, B/E-stop and re-arm with left controller off.
8. End session: dispose anchor; next session must register afresh.
9. Export/replay journals and measure sustained performance and geometric error.

#49 owns adapter tests, #66 owns station registration/metrology, and
#58 owns complete training acceptance. Record unperformed tests as pending.
