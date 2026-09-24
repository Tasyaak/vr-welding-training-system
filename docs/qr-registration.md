# Runtime fixture QR registration (#49)

This implementation builds on merged #46 / PR #77 (`b1296de` on main, including
the final `4d81ea9` content changes). It consumes that catalog without
changing its schema, CAD, nominal assembly or semantic selections. The pure
`WeldingTrainer.Registration` assembly owns the spatial workflow; `.Meta` owns
installed Core/MRUK 205 adapters; `.Preview` is a standalone composition. None
references training/process/scoring code. Bootstrap and its build list are unchanged.
#58 owns production composition, right-input arbitration, the training interlock,
recording and the rule that a new registration generation starts a new attempt.

## Identity and authored content

`QrPayload` accepts **5–68 bytes**, strict UTF-8, exactly
`LW1:[A-Za-z0-9][A-Za-z0-9_.-]{0,63}`. Case is significant. Whitespace, NUL,
URLs, transforms, extra fields and other versions are rejected. The Meta transport
adapter removes one terminal NUL only for MRUK StringQRCode; binary data gets no
such treatment. Payload and pose always come from the same trackable.

The already validated, immutable `SpatialCatalogSnapshot.ResolveForPreview`
selects the sole active binding. Catalog loading rejects duplicate active bindings.
Despite the method name, it is the correct identity-resolution boundary: scored
resolution additionally requires approved seam/cleaning semantics and must not
block fixture-level registration. #49 separately checks measured marker dimensions,
matching quiet-zone convention and qualified physical label plane/evidence.
`EligibleForScoring` additionally requires the upstream binding's scoring readiness.
No missing seams are manufactured by registration.

The supplied PART-001 revision 2 remains nominally `FixtureFromWorkpiece = identity`.
Its T-joint and PreWeld/PostWeld bands are approved upstream. MARKER-001 revision 2
has two alternatives for the single location: QR-PRINT-A / B, each a 90 × 90 × 0.1 mm
white label, with a centered 53 / 63 mm symbol **excluding quiet zone**. Both encode
`LW1:PART-001`; neither is selected or installation-qualified. The committed sample
therefore **cannot reach Registered yet**. Detection does not choose a print by size.
`PrintCandidateNotSelected` is separate from unknown dimensions or an unqualified
plane. A selected candidate's `LabelThicknessMetres` can be known while its installed
plane is still unknown; #49 requires installation evidence, not just thickness.

Synthetic installed-plane records exist only in Editor/.NET tests. Both candidates
are exercised separately after explicit test-only selection, including wrong-size
rejection. A separate missing-semantics regression fixture proves registration is
independent of welding selection; it does not describe the current supplied part.
Updating physical facts/versioned content requires real #66 evidence.

## Capability, permission and coordinate normalization

`MetaQrTracker` checks runtime `QRCodeTrackingSupported`, granted Scene permission,
both manifest permissions, requested **and applied** MRUK configuration, unit-scale
tracking origin and valid head tracking. A denied request remains visible; retry is
explicit. Applying a request is not assumed successful. The acquisition timeout
bounds configuration/permission waits as well as detection. The standalone build
also verifies Scene Support Required / Anchor Support Enabled in Meta project
configuration. No raw-camera permission, network service or left controller is used.

MRUK 205 source `MRUK.Shared.SetLocalTransform` already flips native Z and applies
the native-to-Unity Y rotation; `SetPlane` already flips the native plane X.
Trackable roots are unparented and provide a Unity world transform. MRUK receives
tracking-space pose through its native getter; do not multiply TrackingSpace again. The
standalone preview disables MRUK world lock; any TrackingSpace change invalidates
this implementation instead of attempting an unproven rebase.

The final #77 content preserves right-handed (RH) CAD coordinates. Numeric Unity
poses alone do not preserve physical chirality. All pure registration `RigidPose`
values use the same RH encoding. The platform boundary explicitly changes basis
with `S_z = diag(1,1,-1)`; for ordinary poses `p_RH=S_z p_Unity` and
`R_RH=S_z R_Unity S_z` (quaternion `(-x,-y,z,w)`). No reflection is represented
by a quaternion, nonunit transform scale, altered source CAD, or the editor camera.
The adapter rejects materially nonunit/nonfinite SDK quaternions; it renormalizes
only float roundoff (squared-length deviation ≤ 1e-5) in double precision before
constructing the stricter upstream rigid pose. Authored data is never normalized.

For the MRUK plane, source axes need a different change of basis, `S_x`:
MRUK's local X is reflected relative to the native physical marker's X.
The full normalization is:

```text
p_Marker_RH = S_z * (p_Mruk_Unity + R_Mruk_Unity * (rect.center.x, rect.center.y, 0))
R_Marker_RH = S_z * R_Mruk_Unity * S_x
           = (S_z * R_Mruk_Unity * S_z) * RotateY(pi)
Marker physical axes = -MRUK local X, +MRUK local Y, +MRUK local Z (front)
observed dimensions = PlaneRect.width, PlaneRect.height (metres)
```

The authored printed orientation must match these axes. Arbitrary transform fields
are not accepted in the adapter. Bounds origins need not be zero. Test vectors:
An already RH-encoded WorldFromMrukPlane at (1,2,3), +90° about Z and rect centre
(.1,.2) maps Marker centre to (.8,2.1,3), Marker +X to World -Y and +Z to World -Z.
Native identity QR pose represented by MRUK at Unity (1,2,-3) / Y-180°, with
rect centre (-.1,.2), normalizes to RH (1.1,2.2,3) / identity. Tests independently
construct SDK matrices for 0/90/180/270° markers with additional tilt, and verify
all four corners and the normal. The tests also use rotated production
marker offsets and a nonidentity synthetic part offset. Positive-facing checks use
the transformed +Z and the eye-to-marker vector; backside and grazing views fail.

Presentation copies reflect local mesh Z, normals and tangent parity and reverse
triangle winding; original imported meshes remain untouched. Unity root poses use
the inverse basis conversion and unit scale. This yields
`UnityPoint = S_z * WorldFromFixture_RH * FixturePoint_RH`. A real Unity camera
projection test confirms the supplied recess lower-left, holes north and +X seam
travel left-to-right, including a moved/rotated fixture. Session anchor read/write
and eye positions use the same boundary. #58 must apply it to other platform poses.

**A SDK pose is not sufficient evidence of printed-axis or quiet-zone semantics.**
[Meta's public QR guide](https://developers.meta.com/horizon/documentation/unity/unity-mr-utility-kit-qrcode-detection/)
does not specify those print conventions. The installed conversion code establishes
the math above, not the physical print orientation/extent on every OS.
`QrAdapterQualification` therefore records the exact OS/model/OVRPlugin/SDK tuple,
frame witness and dimension witness, plus IncludesQuietZone or ExcludesQuietZone.
These values are frozen on adapter construction. A different/unknown tuple fails
closed with `ConventionUnqualified`; it is not assigned a guessed convention.
The checked-in `UnqualifiedAdapter.asset` intentionally contains no approval.

To qualify a tuple under #66: record the preview's logged tuple, use an asymmetric
orientation witness on the real printed label, verify centre/axes/front normal at
0/90/180/270° and a nonidentity headset origin, measure the physical code and quiet
zone separately, compare repeated MRUK bounds, and reference that evidence in a
versioned profile. A convention mismatch requires an explicit adapter/test revision,
never a visual corrective offset. Qualify the label plane/placement separately in
the #46 catalog. These observations establish adapter interpretation; the full
station error budget still needs independent metrology. No tuple has been qualified
by this PR's host-only tests.

## Freshness and quality gates

MRUK exposes neither capture timestamps nor reprojection confidence nor a public
TrackableUpdated event. `SetPlane` clears/refills its public `PlaneBoundary2D` list
on every native update, including identical values. `MrukPlaneUpdateWitness` uses
the public List enumerator's mutation invalidation to observe these updates without
reflection or changing SDK objects. A Unity test covers identical refills, empty
lists and repeated polling. `Transform.hasChanged` is deliberately not used:
Unity's equal-pose setters do not reliably change it. Starting a generation seeds
witnesses from existing objects so old poses cannot count as new observations.
Polling cached data retains its sequence
and receive time; it cannot manufacture fresh samples. Removed/untracked objects
leave the batch. `SourceCapturedAt`, confidence and absolute accuracy remain null.
Receive time is a host monotonic timestamp, **not** a camera exposure timestamp.

`RegistrationQuality` / `qr-stability-v1` defines software gates:

| Gate | Value |
| --- | --- |
| Observations | at least 4 fresh updates over at least 2 s; maximum 32 in 6 s |
| Last QR age | at most 1.5 s; future/reordered samples rejected |
| Translation/angular scatter | max deviation from mean ≤ 3 mm / 2° |
| Observed dimensions | within 8% of each authored dimension, same convention |
| View | 0.15–2 m; front-normal cosine ≥ 0.5 |
| Platform/anchor sample age | ≤ 0.25 s |
| Acquisition / preview / anchoring timeout | 45 / 30 / 15 s |
| Later material pose conflict | > 15 mm or > 8° from anchored placement |

These conservative engineering defaults are **not measured physical accuracy or
an approved welding error budget**. Their identity is in every evidence snapshot;
changing them requires a policy version and regression tests. Quaternion averaging
aligns signs to the first sample then normalizes the vector sum. Scatter rejects
incoherent/outlier windows before confirmation; no Euler averaging or scale fitting.
Multiple visible QR objects, even identical payloads, are ambiguous. A selected
candidate cannot silently change trackable/part mid-acquisition.

## State, confirmation and anchor ownership

```text
Unregistered -> Acquiring -> Preview -> Anchoring -> Registered
                   |          |           |             |
                   +----------+-----------+-------------+-> Lost
Cancel -> Unregistered             End/disable -> Disposed
Lost -> explicit Start -> Acquiring (new generation)
```

Preview shows both metre-space meshes with the resolved part/fixture/mount revision.
Confirmation declares the correct physical part, secured bolts and plausible
overlay. This is operator evidence, not sensed authentication. `Confirm` rechecks
the gates and freshness before allocating any anchor. Retry/cancel never bypass
them. The adapter creates one `OVRSpatialAnchor` object and polls creation,
localization and tracked pose. It never calls Save, Load, Share or Erase.

```text
WorldFromFixture(candidate) = WorldFromMarker * inverse(FixtureFromMarker)
WorldFromWorkpiece          = WorldFromFixture * FixtureFromWorkpiece
AnchorFromFixture          = inverse(WorldFromAnchor) * WorldFromFixture(candidate)
WorldFromFixture(now)      = WorldFromAnchor(now) * AnchorFromFixture
```

All are the upstream validated rigid `RigidPose` type (metres, unit scale, named
directions). The single mm→m conversion remains in #46 authoring. The contract's
(1,2,3), (.1,0,0), (0,.008,0) example yields (.9,2.008,3).

`SessionAnchorPoll` captures the current SDK flags/pose in LateUpdate after
OVRSpatialAnchor; its timestamp advances even for a stationary anchor. A disabled
anchor is invalid, and a stopped polling component yields stale required evidence.
The preview runs later in LateUpdate. #58 must preserve this execution ordering.

The anchor operation is owned as soon as creation starts. Cancellation/timeout
destroys its object. Core 205's `OnSpatialAnchorCreateComplete` destroys native
spaces arriving after that component has gone away. The workflow has no asynchronous
callback capable of publishing a stale generation. Fake tests complete an abandoned
operation after a new generation starts and verify it cannot register.

After successful localization, only the anchor updates placement through frozen
offsets. QR disappearance alone is harmless. Later QR observations are diagnostics:
small noise does not move the workpiece; a fresh different ID, ambiguous markers,
dimension conflict or material pose discrepancy yields Lost. There is no snapping.
Anchor/head loss, stale required tracking, origin generation change, recenter,
pause/focus loss and explicit moved/rebolted/wrong-part/marker-moved declarations
invalidate immediately. This conservative implementation never automatically
recovers an old anchor. Restart deliberately acquires a new generation.

An unseen physical assembly movement cannot be detected by the anchor. The
stationary/secured assumption is continuously shown in the preview. Cancel before
moving/rebolting/changing label, then register again.

## Consumption and evidence

Call `RegistrationSession.Read()` on the owning thread to refresh required tracking
before consumption. `LastSnapshot` is immutable **history**, not a liveness query.
`IsUsableAt(now, expectedOriginGeneration)` additionally rejects a retained snapshot
after 250 ms, a future timestamp or origin mismatch. #58 must also check current
session/generation and its own authoritative interlock; reading historical data
cannot revoke an already retained object. No registration class grants process authority.

Snapshot schema 1 carries a fresh session ID, registration/origin generations,
capture/QR-receive/optional-source/confirmation/anchor times, named fixture/part
poses, state/reason, full immutable upstream binding (all revisions and hashes),
observed dimensions/convention, count/span/scatter, platform tuple/convention
evidence, confirmation and anchor state. Invalid states publish no world poses.
`IsValid`, `PhysicallyQualified` and `EligibleForScoring` are distinct.
`IRegistrationEvidenceSink` is the local recording port; the preview logs transitions
to the device log. #58 supplies the authoritative ordered journal.

## Running and verifying

Open `Assets/Trainer/Registration/Preview/RegistrationPreview.unity` via **Welding
Trainer → Registration → Open standalone preview**. It uses the real supplied
catalog, a transparent fixture/workpiece ghost, raw bounds diagnostics, and right
Touch controls. Red/green/blue diagnostic axes visualize normalized Marker X/Y/Z even before tuple
qualification; they are explicitly diagnostic and cannot confirm registration.
Right Touch A starts/confirms; B cancels. This is a smoke-test binding only: B is
not the production E-stop here; #58 must preserve the production global B/E-stop.
Public BeginRegistration/ConfirmAssembly/CancelRegistration/InvalidateAssembly
commands permit another input composition without #48 or any Group B class.

The editor authoring entry point `RegistrationPreviewBuilder.Create` rebuilds the
scene from the pinned rig prefab, retaining existing material/profile assets. The
`BuildAndroid` entry point builds only this scene to
`artifacts/registration-preview.apk`, without changing production build settings.
Use Unity 6000.3.24f1 with `-batchmode -projectPath unity -executeMethod
WeldingTrainer.Registration.Editor.RegistrationPreviewBuilder.BuildAndroid -quit`.

`python tests/repository_checks.py` runs both pure suites with the highest stable
installed .NET SDK ≥ 8, no NuGet dependencies, and checks Group A references,
metadata, preview separation and prohibited persistence calls. Unity EditMode
`WeldingTrainer.Registration.Tests` repeats the shared cases with Unity JSON and
checks the SDK transport/transform-notification assumptions.

Device acceptance remains explicit: grant/deny permission; unsupported tuple;
valid/invalid/duplicate codes; 4 rotated views; unstable/stale/dimension errors;
confirm/cancel; failed localization; cover QR with anchor still tracked; conflicting
QR; tracking loss/recenter/pause; deliberate fresh registration; exit/relaunch with
no anchor persistence. Run offline and with the left controller off. Record actual
hardware/OS/content/profile revisions and outcomes. #66 owns printed fixture/QR
and measured accuracy; #58 owns full training interruption, input and replay tests.
