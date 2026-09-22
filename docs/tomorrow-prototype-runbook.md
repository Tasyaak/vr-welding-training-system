# Presentation prototype runbook

This describes the integrated demo on main, not the target architecture.
PrototypeWeldingDemo generates a camera-relative straight seam with simple
distance/speed scoring, a growing line and right-controller haptics.
It has no production QR registration, session anchor, five-mode engine or recorder.

## Unity and Editor

Use Unity 6000.3.24f1 and the existing unity project/Bootstrap scene.
Confirm OVRCameraRig, MRUK, OVRPassthroughLayer and PrototypeDemo have no missing
references. Validate Stage origin, passthrough underlay, skybox/background alpha
and Meta setup findings; never commit .utmp.

J/L, I/K and U/O move the simulated tip; Space is the trigger, Left Shift changes
speed and R resets. This is Editor simulation, not measured device accuracy.

## Quest demo

Build/run with the right controller; verify passthrough, trigger, status, bead,
haptics and reset. **In this demo B resets; in production B is a latched E-stop
and reset moves into the menu.** Never run both input/evaluation owners together.

The demo does not prove that the physical fixture or bolted part is registered.
Do not present its generated seam as QR alignment or a welding-physics result.

## Production transition

Group A implements fixture QR/assembly registration and right input; group B
implements processes and safety against fake spatial data. #58 performs the
production switch and full device test. The intended flow is QR scan → verify
part/fastening/ghost → session anchor → virtual nozzle/clamp → arm/train →
review/save. Four-point registration is not part of that flow.

Record a backup demo video and clearly state which production features remain
unimplemented. Follow setup.md and the roadmap for production qualification.
