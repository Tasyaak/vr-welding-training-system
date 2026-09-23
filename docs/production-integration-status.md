# Production integration and release gate (#58)

The Group B integration branch assembles the authoritative coordinator, evaluation, unified interlock, reflection and E-stop safety, five process kernels, progressive coverage, semantic/presentation feedback, crash-aware recording, deterministic replay, and the right-controller menu. It deliberately does not edit `Bootstrap.unity` until the real Group A adapters are available.

## Replay contract

`ReplayRecord` is the lossless ordered evidence consumed by `DeterministicReplay`. Production composition must record every consumed evaluator tick, not a display-rate sample. Each record carries a strict sequence, monotonic sample time, registration/input generations, activation epoch, process mode, permission/active state, E-stop and reflection state, and pulse/wobble timing evidence. Replay rejects decreasing sequence/time/epochs, output while inhibited, malformed pulse timing, pulse-window mismatch, and non-finite wobble phase. Summary comparison uses an explicitly configured numeric tolerance.

Existing schema-1 exports from #17 remain integrity-valid but are labelled unsupported for lossless deterministic replay because they do not contain all replay fields. They are never silently upgraded or advertised as replayable.

## Remaining cross-group join

Before the release gate can close, merge and map Group A #46, #48, #49, and #66. The #58 composition root must then:

1. Map the validated CAD/fixture binding and QR registration generations into `ProcessConfiguration`, local seam/target geometry, and `RegistrationInput`.
2. Map the single right Touch Plus adapter into coherent `TrainingInput`, tool pose, menu edges, and the right-only Meta haptic sink.
3. Instantiate one `TrainingCoordinator`, process-kernel router, semantic feedback engine, menu controller, and `LocalSessionRecorder(Application.persistentDataPath, ...)`.
4. Route every authoritative tick to feedback, coverage, results, and lossless replay evidence; recorder loss immediately inhibits and makes the attempt unscored.
5. Replace the prototype component in `Bootstrap.unity` only after the production graph is complete; retain passthrough, OVRCameraRig, and MRUK.

## Device release evidence

Run fresh QR registration for each session and capture the complete pre-clean → nozzle change → Fusion/Wobble/Pulsed → post-clean → review/export workflow with the left controller off. Exercise registration/tracking loss, occlusion, moved/rebolted part, clamp/nozzle/contact, reflection, E-stop/reset/re-arm, app pause, queue/storage failure, and torn-copy recovery. Export with ADB, run the offline validator and C# replay, and attach Unity/package/Quest OS versions plus measured frame time, memory, recording rate, and pose stability. Device acceptance cannot be inferred from Editor or CI.
