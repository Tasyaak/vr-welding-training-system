# Wobble process semantics

Wobble is a simulated scanner footprint around the trainee’s tracked center path. It is not a hand-zigzag exercise, and it does not correct or widen acceptable hand-path tolerances.

For activation-epoch start `t0`, half-amplitude `A`, frequency `f`, and initial phase `phi0`, scanner offset is `A sin(2πf(t-t0)+phi0)` along the stable local lateral axis `normalize(surfaceNormal × seamTangent)`. Phase is derived from timestamps, never accumulated by Update, render frames, or fixed delta. Each fresh epoch resets to `phi0`; inhibition closes it immediately.

Continuous interval extrema are solved analytically. Any interval spanning at least one cycle uses the exact full sine range; partial cycles use endpoint and enclosed extrema. Runtime work is constant with frequency and does not silently skip cycles. Profiles reject frequencies above 10 kHz and coverage grids above the configured cell bound. The grid’s declared spatial error is at most one `coverageCellMetres` along each axis.

The swept rectangle includes spot radius and is clipped to authored seam/lateral target bounds. Cells preserve attempted/acceptable/poor evidence with worst-ever quality. This is a versioned training coverage rule, not optical, thermal, or metallurgical simulation. Reflection must evaluate conservative worst-case supported footprint normals through #51; missing footprint safety evidence remains Unknown.

## Unity setup

Create one `WobbleProcessKernel` per frozen attempt and feed timestamped #10 path plus #50 activation data, not presentation ticks. Keep the returned actual center error for scoring. Attach `WobbleEnvelopeRenderer` beneath the Workpiece root and configure it with the selected local seam/profile; it renders full width `2A + spotWidth` as an explanatory envelope. Do not animate that display as measured hand movement or use its phase for scoring. #58 owns production root, profile, and reflection wiring.
