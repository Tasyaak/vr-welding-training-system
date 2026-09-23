# Pre/post-weld cleaning and virtual nozzle workflow

Cleaning is a geometric training-coverage process, not a contaminant-removal, ablation, chemistry, optical-safety, or metallurgy simulation. It uses continuous simulated output gated by the common interlock; it does not inherit Pulsed timing.

Pre-clean targets are authored weighted finite-surface cells in Workpiece-local metres. Post-clean targets either name an explicit authored area or are derived from the actual attempted weld footprint and record its source attempt. `TryCreateDerived` returns unavailable for an empty/missing weld; it never substitutes a full seam.

`CleaningProcessKernel` rasterizes the swept circular footprint into the frozen grid and clips to its finite bounds. The declared spatial error is at most one cell. It separately reports target, attempted, acceptable, missed, over-target, visits, repeat visits, dwell, and invalid time. Speed, standoff, and orientation determine acceptable coverage. A later acceptable pass may improve a previously poor cell, while visit/repetition evidence remains auditable. Invalid tracking/generation/surface or excessive gaps create no coverage.

`NozzleChangeWorkflow` is an explicit virtual transaction. Begin is allowed only while disarmed, trigger released, and fault-free. Pending makes the effective nozzle Unknown/incompatible. Confirmation requires the matching transaction and remains disarmed; cancel restores the installed nozzle. A fault cancels pending work, so stale confirmation cannot commit. Mode selection never changes the nozzle, and this workflow never alters the calibrated physical tip transform or claims physical detection.

## Unity/#57/#58 setup

Provide menu commands for Begin/Confirm/Cancel and record each transaction ID/event. Feed `Current.Effective` to #50; add `NozzleChangePending` when pending. Link pre-clean, weld, and post-clean as separate attempts through session/source IDs, freezing each target/profile. Render cell overlays beneath the Workpiece root with distinct attempted/acceptable/missed/over-target colors. Continue prospective #51 footprint risk evaluation before permission. No scored-mode “clean all” command is permitted.
