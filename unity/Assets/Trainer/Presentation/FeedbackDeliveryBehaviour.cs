using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    /// <summary>Unity lifecycle bridge. The Meta right-controller adapter is injected by production composition.</summary>
    public sealed class FeedbackDeliveryBehaviour : MonoBehaviour
    {
        [SerializeField] MrFeedbackPresenter visual;
        [SerializeField] UnityAudioFeedbackSink audio;
        FeedbackDeliveryCoordinator coordinator;

        public void Initialize(IRightHapticFeedbackSink rightControllerHaptics)
        {
            coordinator?.Suspend();
            coordinator = new FeedbackDeliveryCoordinator(visual, audio, rightControllerHaptics ?? throw new ArgumentNullException(nameof(rightControllerHaptics)));
        }

        public void Present(FeedbackState state, double monotonicSeconds, bool trainingActive, bool trackingValid)
        {
            if (coordinator == null) throw new InvalidOperationException("Initialize the feedback delivery behaviour before presenting feedback.");
            coordinator.Update(state, monotonicSeconds, trainingActive, trackingValid);
        }

        public void Suspend() => coordinator?.Suspend();
        public void AttemptEnded() => coordinator?.AttemptEnded();
        void OnApplicationPause(bool paused) { if (paused) coordinator?.AppPaused(); }
        void OnDisable() => coordinator?.Suspend();
    }
}
