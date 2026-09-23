using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    public sealed class UnityAudioFeedbackSink : MonoBehaviour, IAudioFeedbackSink
    {
        [SerializeField] AudioSource source;
        [SerializeField] AudioClip informationClip;
        [SerializeField] AudioClip coachingClip;
        [SerializeField] AudioClip warningClip;
        [SerializeField] AudioClip mandatoryClip;
        [SerializeField] AudioClip completionClip;

        public void Play(FeedbackCue cue, double strength)
        {
            if (!source) return;
            AudioClip clip = Select(cue);
            if (!clip) return;
            source.Stop(); source.clip = clip; source.volume = Mathf.Clamp01((float)strength); source.Play();
        }

        public void Stop() { if (source) source.Stop(); }

        AudioClip Select(FeedbackCue cue)
        {
            if (cue == FeedbackCue.Complete) return completionClip;
            if ((cue & (FeedbackCue.EmergencyStop | FeedbackCue.ReflectionRisk | FeedbackCue.TrackingLost | FeedbackCue.RegistrationLost | FeedbackCue.ActivationBlocked | FeedbackCue.NozzleMismatch | FeedbackCue.ClampDisconnected | FeedbackCue.ContactInvalid)) != 0) return mandatoryClip;
            if (cue == FeedbackCue.ReverseMotion) return warningClip;
            if (cue == FeedbackCue.CorrectSpeed || cue == FeedbackCue.Progress) return informationClip;
            return coachingClip;
        }
    }
}
