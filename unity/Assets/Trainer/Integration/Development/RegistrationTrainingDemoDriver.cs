using UnityEngine;
using WeldingTrainer.Registration;

namespace WeldingTrainer.Integration
{
    /// <summary>
    /// Temporary device-side driver for RegistrationTrainingDemo.
    ///
    /// It exists only to exercise QuestRegistrationBridge before the real
    /// TrainingCoordinator/input composition is wired.
    ///
    /// Production Bootstrap must not use this A/B mapping.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class RegistrationTrainingDemoDriver : MonoBehaviour
    {
        [Header("Dependencies")]
        public QuestRegistrationBridge registration;
        public TextMesh statusText;

        [Header("Temporary smoke-test input")]
        [Tooltip("Demo only: right A = confirm/retry, right B = cancel.")]
        public bool smokeInput = true;

        private RegistrationState? lastState;
        private RegistrationReason? lastReason;

        private void LateUpdate()
        {
            if (!registration)
            {
                if (statusText)
                {
                    statusText.text =
                        "REGISTRATION INTEGRATION DEMO\n" +
                        "QuestRegistrationBridge is not assigned.";
                }

                return;
            }

            // Until TrainingCoordinator is connected, this call is what
            // advances RegistrationSession / Meta QR polling.
            RegistrationSnapshot snapshot = registration.Refresh();

            if (snapshot == null)
            {
                if (statusText)
                {
                    statusText.text =
                        "REGISTRATION INTEGRATION DEMO\n" +
                        "Bridge unavailable\n" +
                        registration.StartupError;
                }

                return;
            }

            if (smokeInput)
            {
                if (OVRInput.GetDown(
                        OVRInput.Button.Two,
                        OVRInput.Controller.RTouch))
                {
                    registration.CancelRegistration();
                }
                else if (OVRInput.GetDown(
                             OVRInput.Button.One,
                             OVRInput.Controller.RTouch))
                {
                    if (snapshot.State == RegistrationState.Preview)
                    {
                        registration.ConfirmAssembly(
                            correctPart: true,
                            secured: true,
                            plausibleOverlay: true);
                    }
                    else if (
                        snapshot.State == RegistrationState.Unregistered ||
                        snapshot.State == RegistrationState.Lost)
                    {
                        registration.BeginRegistration();
                    }
                }
            }

            snapshot = registration.Snapshot;

            if (snapshot == null)
                return;

            if (lastState != snapshot.State ||
                lastReason != snapshot.Reason)
            {
                lastState = snapshot.State;
                lastReason = snapshot.Reason;

                Debug.Log(
                    "INTEGRATION REGISTRATION: " +
                    $"t={snapshot.CapturedAt:F3} " +
                    $"generation={snapshot.Generation} " +
                    $"origin={snapshot.OriginGeneration} " +
                    $"state={snapshot.State} " +
                    $"reason={snapshot.Reason} " +
                    $"binding={snapshot.Binding?.Id} " +
                    $"observations={snapshot.ObservationCount} " +
                    $"confirmed={snapshot.AssemblyConfirmed} " +
                    $"anchor={snapshot.Anchor} " +
                    $"workpiecePose={snapshot.WorldFromWorkpiece.HasValue}",
                    this);
            }

            if (statusText)
            {
                statusText.text =
                    "REGISTRATION INTEGRATION DEMO\n" +
                    $"{snapshot.State}: {snapshot.Reason}\n" +
                    $"Generation {snapshot.Generation} / " +
                    $"origin {snapshot.OriginGeneration}\n" +
                    $"Observations {snapshot.ObservationCount}, " +
                    $"span {snapshot.ObservationSpanSeconds:F1}s\n" +
                    $"Binding {snapshot.Binding?.Id ?? "none"}\n" +
                    $"Anchor {snapshot.Anchor}\n" +
                    "WorldFromWorkpiece: " +
                    $"{(snapshot.WorldFromWorkpiece.HasValue ? "YES" : "NO")}\n\n" +
                    "TEMP DEMO INPUT\n" +
                    "A: confirm / retry\n" +
                    "B: cancel";
            }
        }
    }
}
