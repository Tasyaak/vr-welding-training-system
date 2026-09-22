# Unified activation interlock

Issue #50 provides the engine-independent decision used to gate every simulated process. It deliberately has no Meta, QR, CAD, physics-collider, or scene dependency. Production adapters provide validated inputs during #58; the Group B test composition uses synthetic finite patches.

## Decision boundary

`ActivationReducer.Evaluate` is the single pure reduction from current evidence to `ActivationDecision`. The decision includes the complete reason mask, a deterministic primary reason for UI, permission, requested state, and active state. Unknown registration, assembly, binding, tracking, system health, recorder, nozzle, reflection risk, or contact never defaults to safe.

The primary reason order puts emergency/fault and reflection hazards ahead of configuration and operator-correctable conditions. The full mask is retained for recording and diagnostics.

## Contact and clamp are different evidence

`FiniteContactEvaluator` evaluates the tool tip against explicitly bounded convex surface patches. A point must be inside the polygon, on the configured approach side, and within the entry distance. A larger exit distance applies only to the retained surface to prevent contact chatter without making neighboring surfaces ambiguous. Tracking loss immediately clears retained contact. Optional ray impact also rejects hits outside the finite polygon; an infinite plane is never sufficient.

Virtual contact confirms the simulated tool-to-workpiece relationship. `ClampState.Connected` records the separate virtual electrical-clamp preparation action; it does not confirm mechanical bolts or physical electrical contact. Both are mandatory; neither substitutes for bolts, fixture qualification, or the other signal.

## Recovery and nozzle rules

Fusion, wobble, and pulsed profiles require the welding nozzle. Pre-weld and post-weld cleaning require the cleaning nozzle. After inhibition or a latched fault, a held trigger cannot resume output: release must be observed and the coordinator must explicitly arm again. `FaultLatched` returns `ResetRequired`.

## Unity test setup

Run the EditMode tests in `WeldingTrainer.Application.Tests`, especially `ActivationInterlockTests`. For an isolated scene demonstration, construct `FinitePatch` values from synthetic vertices in Workpiece metres, feed contact/risk/prerequisite values through a fake safety port, and display `ActivationDecision.PrimaryReason`. Do not add this fake provider to production Build Settings and do not modify `Bootstrap.unity`; production composition belongs to issue #58.

## Rebased schema and recovery contract

Existing schema-v1 reason values (bits 0–18) are preserved; interlock reasons use bits
19–28. PrimaryReason uses the documented hazard-first list in ActivationDecision,
then the lowest set bit for any remaining or future flag, so a nonzero mask never
has a None primary reason. Snapshots expose the same primary reason.

Tick, Arm and E-stop reset all assemble prerequisites through BuildInterlockInputs
and call ActivationReducer. Prospective safety evidence is captured at most once per
tick. Application lifecycle commands may revoke activation but cannot grant it outside
the reducer. Suspended sessions require Resume and a separate Arm; a held trigger
cannot arm. Safety providers own their fault reset acknowledgements (#51/#52);
FaultLatched always yields ResetRequired until that provider clears the latch.

Finite patches reject non-finite, degenerate, non-planar, concave and self-intersecting
polygons. Both windings are supported; collinear authored vertices are unsupported.
Policy distances must be finite and satisfy 0 < Enter <= Exit. Ray queries respect
the permitted approach side and finite polygon. Hysteresis is retained only for the
same immutable patch object, and is cleared on invalid tracking/missing evidence.
The tracking-valid argument must include registration, origin and freshness validity.

The legacy four-argument SafetyInput constructor preserves rejection information but
cannot manufacture valid contact, assembly/binding confirmation or reflection evidence.
Tests and the Editor-only FakeTrainingComposition provide explicit synthetic evidence;
no production scene or release Build Settings changes are required.
