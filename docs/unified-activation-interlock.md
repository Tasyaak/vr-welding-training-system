# Unified simulated-process interlock

This system controls training simulation only; it is not an industrial laser safety circuit.
One application decision authorizes every process. Renderers, process kernels, audio, coverage,
and haptics consume the immutable process snapshot and must never reconstruct permission.

Permission requires a running eligible attempt, current registration and tracking generations,
valid head/tool/tip poses, a supported process, compatible virtual nozzle, explicitly connected
virtual clamp, bounded geometric contact, known-safe prospective reflection, process-specific
prerequisites, healthy recorder, clear faults/E-stop, observed trigger release, and explicit arm.
The full reason mask and deterministic primary reason are published for UI and logging.

The virtual clamp records an operator preparation action. It is not physical or electrical
contact and never bypasses geometric contact. Disconnecting while running suspends immediately.
A new session or changed registration disconnects it.

Contact projects the registered effective tip onto authored finite convex surface patches. The
query reports patch ID, projected point, outward normal, signed standoff, edge distance, approach
side, finite bounds, and ambiguity. Enter uses the profile's tight contact band; an established
contact remains only within the wider exit band. Tracking/registration/normal invalidity overrides
hysteresis immediately. A point near an infinite plane but outside its polygon is not contact.

## Unity setup

`Bootstrap.unity` contains one `UnifiedSafetyAdapter` assigned as the production bootstrap's
Safety Provider. Its risk and process-prerequisite fields intentionally remain unassigned until
Issues #51 and #53–#56 provide real adapters. Missing providers return Unknown/missing and inhibit;
never add an always-safe device fallback. Run Application EditMode tests after import.

## Device checks

After calibration, test the physical tip inside the plate, just beyond every edge, on the forbidden
side, and across enter/exit distances. Verify clamp-only and contact-only cases remain blocked,
disconnect suspends in the same update, and recovery requires release, resume, and explicit re-arm.
