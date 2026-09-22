# Right Touch Plus production input

Production input is supplied only by `RightControllerInputAdapter`, using Unity Input System
controls exposed by OpenXR. The generic starter `Player` and `UI` maps contain no XR bindings,
so they cannot also consume the training trigger or B button.

| Right control | Training | Calibration | Menu |
| --- | --- | --- | --- |
| Index trigger | Analog process intent with press/release hysteresis | Inhibited | Inhibited; never UI click |
| B / secondary button | Global emergency-stop edge | Global emergency-stop edge | Global emergency-stop edge |
| A / primary button | Unused | Confirm labeled point | Submit selected item |
| Thumbstick click | Request menu/pause | Cancel/menu request | Close/back to safe suspended state |
| Thumbstick axis | Unused | Workflow navigation | Navigation/adjustment |
| Grip | Reserved | Reserved | Reserved |

The Meta/Oculus system button remains OS-owned. The left controller is never a fallback.
Entering or leaving any context requires trigger release before process intent can become true.
Button edges are retained until the application captures a snapshot, so a short B press between
coordinator ticks is not lost.

## Unity configuration

1. Merge Issues #46 and #47 first, then open `Assets/Trainer/Scenes/Bootstrap.unity`.
2. Select `AppRoot/PlatformAdapters`. Confirm there is exactly one
   `RightControllerInputAdapter` and that the production bootstrap's **Input Provider** points
   to it.
3. Assign the same `QuestMvpCatalogAsset` to both the production bootstrap and the adapter.
   Its versioned `controllerToTool` and `toolToEffectiveTip` poses are composed in that order.
4. Keep press threshold above release threshold. Defaults are 0.55 and 0.45.
5. In **Project Settings > XR Plug-in Management > OpenXR > Android**, confirm both Oculus
   Touch Controller Profile fallback and Meta Quest Touch Plus Controller Profile are enabled.
6. Do not add `PlayerInput`, OVRInput polling, a second input action asset, or another production
   component implementing `IInputSnapshotSource`.
7. Run Application EditMode tests, enter Play Mode, and confirm startup remains inhibited until
   the catalog and the other mandatory production providers are assigned.

## Quest 3 qualification

Build for Android and test with the left controller powered off. Record the adapter's
`ActiveControllerLayout`. Verify trigger analog/edges, a short B press in every context, A only
during calibration, thumbstick click/axis, held trigger across menu/focus loss, controller
occlusion/disconnect, headset tracking loss, and recenter. Missing device, position, or rotation
flags must produce invalid poses rather than a frozen last-known pose. After recenter, call
`NotifyTrackingOriginChanged`; its generation changes and process input requires release.
