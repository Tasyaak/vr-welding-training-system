# MR feedback adapters (issue #15)

This feature renders, sounds, and haptically delivers the authoritative `FeedbackState` from issue #14. It does not recompute path quality, activation permission, tracking validity, or safety state.

## Unity scene setup

1. Create a `Feedback` child under the registered `Workpiece` root. Keeping the presenter below this root makes all guides follow QR registration. Do not add an opaque workpiece mesh: passthrough must keep the physical fixture visible.
2. Add `MrFeedbackPresenter` to `Feedback`. Create child objects for the target seam, completed progress, position guide, start/end markers, travel/work angle guides, speed indicator, mandatory status, and completion status. Assign them in the inspector.
3. Use transparent unlit/additive materials with depth-tested thin lines. Set both seam `LineRenderer` components to local coordinates; the presenter also enforces `useWorldSpace = false`.
4. After Group A resolves the selected CAD seam, call `ConfigureWorkpieceLocal(Vector3[])` with its workpiece-local polyline. Do this again after a workpiece/session change.
5. Add one `AudioSource` and `UnityAudioFeedbackSink`. Disable play-on-awake and looping. Assign already-imported/preloaded clips for informational, coaching, warning, mandatory, and completion cues. No runtime asset loading is performed.
6. Add `FeedbackDeliveryBehaviour`, assign the visual and audio components, and initialize it from the #58 composition root with the Meta Core SDK adapter implementing `IRightHapticFeedbackSink`.
7. Feed every authoritative semantic state to `Present(state, monotonicSeconds, trainingActive, trackingValid)`. Call `Suspend()` on training suspension and `AttemptEnded()` before leaving the attempt. App pause and component disable stop output automatically.

## Haptics ownership

`FeedbackDeliveryCoordinator` is the sole owner/arbiter for training haptics. The production adapter must target only the Touch Plus right controller. It must stop the controller on `Stop()`; it must not drive ESP32 coils or any external actuator. The coordinator explicitly stops owned output when a cue changes, tracking is invalid, training suspends, an attempt ends, the component disables, or Android pauses the app.

The Meta adapter is intentionally created in issue #58 because that composition branch owns the installed Meta SDK and scene. Its `Play` implementation maps normalized strength and bounded duration to the current Meta Core haptics API; its `Stop` implementation immediately stops right-controller vibration.

## Assistance and cue mapping

At 0% assistance, optional coaching visuals/audio/haptics are hidden or silent. Mandatory tracking, registration, activation, interlock, reflection, and E-stop status remains visible and audible. All modalities consume the same `PrimaryCue`, strength, cadence, and progress values, preserving the priority and anti-spam behavior from issue #14.

Quest verification checklist:

- Confirm passthrough and the physical workpiece remain visible beneath every guide.
- Confirm target seam, endpoints, progress, position, speed, travel angle, work angle, mandatory status, and completion are distinguishable.
- Confirm audio clips are preloaded and do not overlap during rapid cue changes.
- Confirm only the right controller vibrates and vibration stops on tracking loss, suspension, attempt end, app pause, and scene disable.
- Repeat at 0%, 50%, and 100% assistance.
