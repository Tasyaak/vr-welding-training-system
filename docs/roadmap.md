# Quest-only implementation roadmap

The ordered issue index is authoritative. Each feature must remain inhibited
until its mandatory providers are present and tested.

1. **#45 Migration** — retire obsolete external-device assumptions and align the repository.
2. **#46 Content** — versioned fixture, finite surfaces, seams, tool/nozzle and five process modes.
3. **#47 Composition/state** — authoritative process/session state and production composition root.
4. **#48 Input** — one right Touch Plus input and calibrated rigid tool pose.
5. **#49 Calibration** — ordered four-point rigid fixture fit, residual validation and session anchor.
6. **#50 Contact/interlock** — virtual clamp, finite-surface contact and unified activation decision.
7. **#51 Reflection** — conservative back-reflection risk and latched training faults.
8. **#52 Emergency stop** — right-controller E-stop, latched reset and explicit re-arm.
9. **#53 Fusion** — shared seam evaluation and activated coverage.
10. **#54 Wobble** — time-based scan, footprint coverage and envelope visualization.
11. **#55 Pulsed** — deterministic pulse windows and exact ON-time coverage.
12. **#56 Cleaning** — pre/post-weld coverage and explicit virtual nozzle changes.
13. **#57 Menu** — right-controller MR process menu with safe input contexts.
14. **#58 Integration** — feedback, crash-aware recording, deterministic replay and Quest acceptance.

Retained core responsibilities: #10 seam evaluation, #16 coverage/bead, #14/#15
feedback, and #17 local persistence. Legacy QR/external-device Issues #7, #11,
#12, #13 and #18 are superseded or cancelled as recorded in
`quest-only-migration.md`.

## Acceptance stages

- Clean Unity import and compile with no missing references.
- Deterministic Domain/Application tests pass without Unity or a headset.
- PlayMode composition tests pass with fake platform providers.
- Standalone Quest 3 test procedure passes with passthrough and only the right
  controller; no network or external hardware is present.
- Study build is reproducible from a tag with frozen content/configuration.
