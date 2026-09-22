# Simulated back-reflection model

This is a conservative geometric training model, not an optical sensor, reflected-power model,
PPE assessment, Class 4 safety calculation, or approval for real laser operation.

For unit incident direction `d` pointing toward the finite impact and outward unit normal `n`, the
ideal center ray is `r = d - 2(d·n)n`. Normal incidence (`d=-n`) gives `r=n`. The tracked HMD is
represented by an inflated head sphere (0.14 m radius plus 0.03 m pose uncertainty and the frozen
profile margin). A forward angular cone around `r` intersects the full sphere volume; no distance
decay or unverified occlusion grants safety.

Results are Unknown, Low modeled risk, Warning, or High. Unknown and High inhibit immediately.
An ideal-ray/head hit latches immediately; marginal cone High latches after at most 50 ms while
remaining inhibited throughout. A latch clears only after acknowledgement, trigger release, and
0.2 seconds of stable Low evidence, followed by the common explicit re-arm workflow. E-stop and
reflection latches are independent.

`UnifiedSafetyAdapter` creates the production evaluator automatically when no custom prospective
risk provider is assigned. Optional `ReflectionHintRenderer` draws the ideal ray only when
assistance is above zero; essential semantic status, events, and safety behavior never depend on
assistance. In Unity, assign the safety adapter as its Risk Source and a LineRenderer as Ideal Ray.

Quest qualification must verify frame alignment, normal and oblique analytic cases, head sphere
placement, cone tangency, tracking loss, recenter, marginal hold timing, immediate ideal-ray latch,
acknowledgement/release/stable-Low recovery, and identical evidence at 30/72/120 Hz.
