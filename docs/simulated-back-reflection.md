# Simulated back-reflection training model

This subsystem is a conservative geometric coaching and interlock model. It is **not** an optical sensor, reflected-power estimate, BRDF/material simulation, hazard-distance calculation, PPE substitute, or authorization to operate a Class 4 laser.

For incident unit direction `d` pointing toward an authored finite-surface impact, and outward unit normal `n`, the ideal reflected direction is `r = d - 2(d·n)n`. Normal incidence (`d=-n`) gives `r=n`. Front-side incidence, common frame/generation, finite vectors, surface identity, timestamps, head/tool validity, and model envelope are mandatory; missing evidence returns `Unknown` and inhibits through issue #50.

The ideal ray is expanded into a configurable forward cone. Each head/fixed hazard sphere is position-inflated. The evaluator checks exact half-ray/sphere intersection and conservative angular-volume overlap: `beta <= coneHalfAngle + asin(radius/distance)`. It uses the worst target; no averaging or unverified mesh occlusion can turn a head intersection into Low. Low means only low modeled risk.

`High` inhibits output in the same update. Direct-ray, apex, or severe inner-margin cases latch immediately. A marginal cone High may use at most 50 ms confirmation, but remains inhibited throughout. Confirmed faults survive angle correction and require trigger release, acknowledged reset after stable Low evidence, and explicit re-arm through the common interlock. Reflection and E-stop fault identities remain separate.

## Unity/#58 mapping

Build `ReflectionInput` from the prospective finite impact, incident direction toward that impact, authored outward normal, tracked HMD head sphere plus documented head-center offset, optional authored fixed spheres, coherent generations, and monotonic capture/evaluation times. Evaluate while disarmed, armed, active, and pulse-OFF. Feed `ReflectionRiskResult.Level` and `ReflectionFaultState.Latched` into #50; render optional cone hints according to assistance, but never alter risk using assistance. Record the complete evidence/result without fictitious watts.
