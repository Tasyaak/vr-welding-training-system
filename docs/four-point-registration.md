# Four-point fixture registration

Registration is session-only and uses four labeled physical fixture points. It does not use QR,
fit scale, or calibrate the tool offset. The right-controller tool/tip transform must already be
validated.

## Unity setup

1. Open `Assets/Trainer/Scenes/Bootstrap.unity` after merging Issues #46–#48.
2. On `AppRoot/PlatformAdapters`, confirm exactly one `MetaSessionAnchorAdapter` and one
   `FourPointRegistrationAdapter`. The production bootstrap's **Registration Provider** must
   reference the latter.
3. Assign the same `QuestMvpCatalogAsset` to the production bootstrap, right-controller adapter,
   and registration adapter. Do not mix catalog versions.
4. Label the fixture's authored P1–P4 locations physically and use the same unambiguous order.
5. Optionally add `CalibrationGhostPreview` to a presentation object. Assign the registration
   adapter as **Workflow Source**, a transparent fixture model as **Fixture Ghost**, and a
   `LineRenderer` as **Residual Lines**. UI buttons call `AcceptPreview` or `Recapture(pointId)`.
6. Run `WeldingTrainer.Application.Tests` in EditMode before building.

## Operator flow

Hold the effective tip still on the prompted point. The capture window requires at least the
catalog sample count (minimum three), 0.15 seconds, speed below 0.03 m/s, and spread within the
catalog stability radius. Press A to confirm. Tracking loss clears only the active window;
origin-generation change invalidates the calibration.

All four representative points are fitted with an orientation-preserving fixed-scale rigid fit.
The candidate reports four residual vectors, RMS, maximum residual, pair-distance disagreement,
and planar conditioning. A well-spread planar rectangle is valid; tiny/collinear geometry,
dimension mismatch, unstable capture, or excessive residual is rejected. Rejected points require
explicit recapture. Numerical failure cannot be overridden.

After a passing candidate, inspect the ghost at the four labels and at independent fixture
locations, then explicitly accept. Registration becomes valid only after the unsaved Meta spatial
anchor reports Created and Localized. The runtime anchor is destroyed on session end and is never
saved, shared, or loaded.

## Quest qualification

Record residuals and independent check-point error for at least five complete calibrations. Test
wrong point order, one unstable touch, controller occlusion, recenter during capture, focus loss,
anchor timeout/loss, and the user-report fixture-moved action. Every loss must inhibit the process
in the same coordinator update. Thresholds must be qualified against the real tool attachment and
fixture before acceptance.
