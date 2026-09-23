# Deterministic Pulsed process

`PulseProfile` uses one canonical representation: period `P > 0`, duty `D` in `(0,1]`, and `Ton = D·P`. Every activation epoch starts at logged `t0` with pulse index zero. Window `k` is half-open: `[t0+kP, t0+kP+Ton)`. Exact OFF boundaries are OFF; exact period boundaries begin the next pulse. Duty one reduces to continuous timing.

`PulsedProcessKernel` intersects each valid, coherent, two-ended pose/activation interval with every overlapping ON window. Arc position is linearly interpolated only inside the configured maximum sample gap. Tracking/generation invalidity never bridges. Release, menu/interlock loss, and E-stop truncate at their timestamp, end the epoch, and leave no scheduled future work; a later activation restarts phase.

Each finite spot expands the intersected arc by half the configured spot diameter and enters #16’s unique seam interval union. OFF travel remains a hole unless adjacent spot footprints geometrically overlap. Repeated spots increase visit evidence but do not double-count unique length, and poor evidence cannot be overwritten by a good revisit.

Work is deterministic: profiles are rejected when `ceil(maximumSampleGap / period) + 2` exceeds `maximumWindowsPerInterval`. The kernel never silently drops pulse windows. Monotonic doubles are used with a declared `clockToleranceSeconds` only for negligible overlap rejection; the half-open state test itself is exact.

## Unity/#58 setup

Create one kernel from the frozen Pulsed profile and selected local seam. Feed timestamped #10/#50 snapshots, not render frames. Record profile ID/hash, period, duty, epoch start, pulse index, each intersected window, registration generation, quality, and interruption event; these fields reconstruct pulse state during replay. Render the returned #16 intervals as separate finite regions and never join OFF gaps. Continue #51 prospective safety evaluation during OFF periods.

This binary pulse/spot model is a training visualization. It does not model pulse-generator hardware, energy, peak power, absorption, penetration, metallurgy, or physical weld quality.
