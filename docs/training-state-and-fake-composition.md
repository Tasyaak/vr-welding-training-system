# Authoritative training state and fake composition

Group B owns one `TrainingCoordinator`, the sole authority for session, attempt, activation,
virtual nozzle/clamp, registration generation, faults and semantic event order. Presentation and
effects consume immutable snapshots; Group A data is mapped only by Issue #58.

Lifecycle is `Idle -> Initializing -> Registering -> Selecting -> Ready -> Running ->
Suspended/Saving -> Reviewing -> Completed`, with `Aborted` and `Faulted` paths. Attempts freeze
one versioned configuration/profile hash; retry uses a new ID and preserves prior recording.

Each tick captures monotonic input, prioritizes E-stop, sorts commands by timestamp/priority/
sequence, validates registration/origin/tracking, evaluates safety, reduces activation, emits
events, records, then publishes. Missing safety or recorder ports inhibit.

## Unity verification

1. Open `Assets/Trainer/Scenes/TrainingTest/FakeTraining.unity` directly; do not add it to release
   Build Settings.
2. Confirm exactly one `FakeTrainingComposition`, enter Play Mode, and observe its synthetic-port
   warning.
3. Run `Window > General > Test Runner > EditMode > WeldingTrainer.Application.Tests`.
4. Keep production `Bootstrap.unity` unchanged. Issue #58 owns production wiring and must never
   install fake-valid providers in a device build.

The fake composition does not parse QR, create anchors, read Touch Plus, or claim physical
registration validity.
