# QR lifecycle investigation and repeatable Quest acceptance

Audit baseline: PR #84 commit `5b1fe8ac06911ffa4c1344ea4c23c005b5ed5266`.
Its `StopScanning` change clearing `LastFrame` is retained, as is the corrected
Build Settings GUID. Production catalog, print selection and #66 evidence are unchanged.

## Evidence and diagnosis

The owner reported reaching Preview with a separate local 53 mm smoke catalog and
temporary adapter qualification. Reported valid-update intervals were 0.214–5.752 s
(mean 1.427 s, p95 3.028 s); another run with an experimental 3.5 s freshness limit
still behaved poorly. The 63 mm print performed better. These are reported test
observations, not new qualification measurements made by this change.

Official sample inspected at commit
[`1d625d5347ed911e34049407cb77a69eb035e588`](https://github.com/oculus-samples/Unity-MRUtilityKitSample/tree/1d625d5347ed911e34049407cb77a69eb035e588/Assets/MRUKSamples/QRCodeDetection).
Its manifest pins Core/MRUK 205.0.0 and OpenXR 1.16.1:

- `QRCodeManager` creates a child visual at `TrackableAdded`, destroys the root
  at `TrackableRemoved`, and does not remove a visual for `IsTracked == false`.
- `QRCode.SetTrackingStateText` displays the current `IsTracked` flag every Update.
- `Bounded2DVisualizer` keeps the rectangle between updates; it does not impose our
  freshness, stability, dimensions or registration/anchor gates.

Installed SDK `MRUK.Trackers.cs` confirms that `GetTrackables` returns all objects
in the registry, including temporarily untracked objects. Only
`HandleTrackableRemoved` removes registry membership. `MRUK.Shared.SetPlane`
mutates the boundary list on SDK updates; the mutation witness is not a camera
timestamp or a confidence measure. Setting the same requested tracker configuration
each Read does not restart the native tracker: MRUK compares requested/applied
configurations before configuring it. No evidence justifies replacing polling or
relaxing the freshness threshold.

Confirmed defects in our previous integration:

1. Filtering out untracked objects before membership reconciliation deleted their
   cache/witnesses. A one-poll dropout reset the stability window and Preview.
   Polling-based "lost/discovered" logs therefore did not prove SDK Removed/Added.
2. Reappearance of the same object constructed a new witness and treated a non-null
   existing boundary as an update. That could fabricate a new sequence/receive time.
3. Losing the plane component was not reported as a witness change, potentially
   leaving the previous good observation until its age expired.
4. Registered-state ambiguity counted stale cached observations alongside fresh
   ones before the freshness filter.

This explains software amplification, misleading lifecycle counts and part of the
visual difference. It does **not** prove why the actual native IsTracked flag was
less stable in the owner's app. The sample and preview are not identical builds:
the sample also has Interaction and Meta OpenXR packages and enables world lock;
our isolated scene disables world lock to preserve the origin contract. No package
or world-lock change is justified by this evidence. If comparable raw flag/update
logs remain worse, investigate device load, camera/view, rendering and runtime
configuration in a further controlled A/B test. The reported intervals also exceed
1.5 s: real gaps will still invalidate an unanchored candidate. This fix cannot make
that evidence fresh, and hardware resolution is not claimed.

## Changed semantics

`MrukQrTrackableCache` separates object lifetime, tracked state, update witness and
immutable last observation. MRUK events manage membership; enumeration reconciles
missed membership changes without treating discovery as a fresh pose. Temporary
untracking retains the same lifetime identity. Repeated polling and tracked-state
changes alone cannot advance sequence or timestamp. Updates while untracked are
consumed but cannot later be promoted to tracked evidence. Origin/reset seeds
existing objects without carrying evidence forward. Invalid plane data replaces
old evidence with a rejected observation.

`RegistrationSession` retains a candidate/window across a same-lifetime temporary
dropout only until the existing 1.5 s deadline from the last accepted observation.
It reports `AwaitingTrackedQr` and blocks Confirm until a new valid tracked update.
Preview's frozen candidate ghost is diagnostic, not Registered output. Removal,
expiry, invalid data or replacement identity discards the candidate/window.
No thresholds changed. After localization, the anchor remains authoritative;
untracked statuses and stale QR diagnostics cannot move or invalidate it. Fresh
conflicts and anchor/origin/assembly failures still fail closed.

## Diagnostic meanings

Enable `diagnosticQrLogging` on the standalone `RegistrationPreview` component
for the comparison build. It defaults off; logging exceptions cannot change runtime
semantics. Keep the same choice for both print runs. Lines start with `QR`, host
monotonic time, Unity frame, origin generation and requested scanning state:

| Log | Meaning |
| --- | --- |
| `event=TrackableAdded` / `event=TrackableRemoved` | Actual subscribed MRUK events |
| `registry-add` / `registry-seed` | New local lifetime from an event / pre-existing enumerated object |
| `tracking ... from=True to=False` and reverse | Same object's sampled IsTracked transition, not native removal/addition |
| `sdk-plane-update ... witness=boundary-mutation` | SDK boundary mutation observed since last poll; multiple callbacks may coalesce |
| `witness=TrackableAdded` | Initial SDK Added event supplied the update |
| `observation ... seq=... received=... pose=... size=...` | Captured tracked update, including invalid data; no promise it passes gates |
| `registry-missing` | Enumeration reconciliation, explicitly not an MRUK event |
| `scan-start`, `scan-stop`, `origin-reset` | Application lifecycle boundaries |

The UI now distinguishes MRUK object Tracked/Untracked, awaiting a tracked update,
and **last SDK plane (history)** with its unchanged sequence and growing age.
Missing diagnostic axes are not equivalent to a native TrackableRemoved event.
Do not infer camera capture cadence or physical accuracy from these logs.

## Next Quest 3 test: fixed sequence and PASS/FAIL

1. Record PR commit, Quest OS/runtime tuple, both app versions, lighting, distance,
   viewing angle and print identity. Build this branch's standalone scene with
   diagnostics enabled. Reuse separate local smoke catalog/profile copies with
   the tested dimensions/plane and exact tuple; do not edit production authoring
   or promote smoke evidence to qualification. Keep `QrMaxAge = 1.5`.
2. Capture Unity logs (for example `adb logcat -v threadtime Unity:I '*:S'` to a
   local file). Run the official QRCodeDetection sample and this scene in turn
   with one 53 mm print, then one 63 mm print, each front-on for 60 seconds and
   again at the same oblique angle. Do not show both simultaneously. Compare
   actual tracked transitions and native events, not just ghost visibility or
   accepted-observation counts. Sample text persistence alone is not freshness.
3. Acquire Preview. Briefly cover only the QR, then reveal it before the last
   observation is 1.5 s old. If the SDK retains the object, PASS means the lifetime
   identity, last sequence/time and accumulated history survive the gap;
   `AwaitingTrackedQr` blocks A/Confirm. Only a subsequent tracked SDK update can
   resume confirmation. FAIL: false Removed/Added, new lifetime on a flag toggle,
   sequence/time advancing without an update, or anchoring while confirmation is
   blocked. An actual SDK Removed event legitimately discards Preview.
4. Repeat with QR hidden for at least 3 seconds. PASS: the unanchored candidate
   expires, poses/window clear and Confirm cannot create an anchor. Reappearance
   requires accumulation again; a genuine new object cannot inherit the old
   window even if its payload matches. FAIL: each poll/dropout renews a grace
   deadline, retained stale pose confirms, or history transfers across lifetimes.
5. In separate runs present malformed/unknown payload, the wrong print size for
   the selected local catalog, and two visible codes. PASS: no registration;
   explicit rejection. If invalid/nonfinite bounds recur, they must be logged
   and rejected, never rendered as a valid pose or hidden behind an older sample.
6. Reacquire valid Preview, declare part/fastening/overlay and press A. PASS:
   Anchoring precedes Registered; only localized tracked anchor permits valid
   output. Cover QR for 10–20 seconds while keeping headset/anchor tracking valid.
   PASS: registration persists with no QR-driven movement. Stale diagnostics
   must not cause QrConflict or AmbiguousQr.
7. In a separate diagnostic run introduce a fresh, materially displaced test
   marker or a different payload after registration. PASS: Lost on fresh
   conflicting evidence, no snap. Test recenter, headset tracking loss/pause and
   explicit assembly invalidation: each must clear valid output. Deliberate retry
   must start a new generation; exit/relaunch requires fresh registration.
8. Repeat the normal front-on acquisition/confirmation three times per print.
   PASS for the reported hardware usability issue requires repeatable acquisition
   and confirmation with the intended view conditions, alongside all lifecycle
   and safety checks above. If actual tracked updates still have multi-second
   gaps or raw IsTracked remains materially worse than the sample, report that
   hardware comparison as **unresolved/FAIL**, even if deterministic regressions
   pass. Do not lengthen thresholds to label the comparison successful.

Keep these results separate from #66 print selection and station error-budget
qualification. #58 composition, #48, Group B and Bootstrap are unchanged.

## Host verification of this correction � 2026-09-24

- Repository checks passed: 48 spatial cases and 77 registration cases (.NET
  9.0.201), including 11 new workflow regressions.
- Unity 6000.3.24f1 EditMode `WeldingTrainer`: 83/83 passed (65 Application,
  four Spatial, 14 Registration). Three new Unity tests exercise the actual cache,
  lifetime/reset behavior, plane invalidation and diagnostic isolation; the shared
  workflow test repeats all 77 cases using Unity JSON.
- Standalone Android ARM64 IL2CPP development APK built successfully; Unity exited
  with code 0. Local ignored outputs: `artifacts/registration-preview.apk`,
  `artifacts/49-lifecycle-android-build.log`, `artifacts/49-lifecycle-editmode.xml`.
- `git diff --check` passed. No threshold, production content, #66 evidence,
  Group B/#48/#58 or Bootstrap composition changes.
- ADB reported no attached devices. The corrected revision has **not** been
  re-tested on Quest. Owner-reported tests above concern the earlier implementation.
