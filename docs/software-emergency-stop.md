# Right-controller software emergency stop

This is a software training E-stop for simulated output. It is not a physical emergency-stop circuit, industrial safety controller, or protection for a real laser.

`EmergencyStopReducer` owns the latch independently of UI focus and reflection-fault identity. A timestamped `EmergencyStop` command has highest coordinator priority, inhibits in the current update, closes the active epoch, stops haptic/audio/process sinks, and records an immutable fault ID. Repeated presses are idempotent for latch state. Starting another session or attempt does not clear the latch.

Release of B or the process trigger never resets automatically. Reset requires observed B and trigger release; valid coherent registration, head, tool and system; connected virtual clamp; geometric contact; compatible nozzle; reset-eligible Low reflection evidence; and recorder health. Rejection emits typed reasons. A successful acknowledgement clears only the E-stop latch and returns to disarmed. Resume, explicit arm, and a fresh trigger press are separate actions and create a new activation epoch, preventing delayed pulse/wobble/cleaning output from an old epoch.

## Unity/#58 setup

Map the right Touch Plus secondary/B press and release to global timestamped `EmergencyStop` and `EmergencyStopReleased` commands before focused-menu routing. Do not bind B to reset, back, cancel, or jump in the production input context. The process sink supplied to `TrainingCoordinator` should implement both `IHapticStop` and `IProcessStopSink`, cancel every pending pulse/envelope/coverage job whose epoch is stopped, and display latched status at all assistance levels. Actual short-press receipt latency and operation with the left controller off are Quest acceptance tests in #58.
