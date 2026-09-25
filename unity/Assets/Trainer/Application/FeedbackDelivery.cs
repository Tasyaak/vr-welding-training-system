using System;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public interface IVisualFeedbackSink
    {
        void Apply(FeedbackState state);
        void Clear();
    }

    public interface IAudioFeedbackSink
    {
        void Play(FeedbackCue cue, double strength);
        void Stop();
    }

    public interface IRightHapticFeedbackSink
    {
        void Play(FeedbackCue cue, double strength, double durationSeconds);
        void Stop();
    }

    public sealed class FeedbackDeliveryCoordinator
    {
        readonly IVisualFeedbackSink visual;
        readonly IAudioFeedbackSink audio;
        readonly IRightHapticFeedbackSink haptic;
        double nextAudio, nextHaptic, lastTime = double.NegativeInfinity;
        FeedbackCue audioCue, hapticCue;
        bool stopped = true;
        public FeedbackDeliveryCoordinator(IVisualFeedbackSink visual, IAudioFeedbackSink audio, IRightHapticFeedbackSink haptic)
        {
            this.visual = visual ?? throw new ArgumentNullException(nameof(visual));
            this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
            this.haptic = haptic ?? throw new ArgumentNullException(nameof(haptic));
        }

        public void Update(FeedbackState state, double timeSeconds, bool trainingActive, bool trackingValid)
        {
            if (!double.IsFinite(timeSeconds) || timeSeconds < lastTime)
                throw new ArgumentException("Feedback delivery time must be monotonic");
            lastTime = timeSeconds;
            visual.Apply(state);
            if (!trainingActive || !trackingValid)
            {
                StopTransient();
                return;
            }

            stopped = false;
            FeedbackCue cue = state.PrimaryCue;
            bool changed = cue != audioCue;
            if (changed)
            {
                audio.Stop();
                haptic.Stop();
            }

            if (state.AudioStrength > 0 && cue != FeedbackCue.None && (changed || timeSeconds >= nextAudio))
            {
                audio.Stop();
                audio.Play(cue, state.AudioStrength);
                audioCue = cue;
                nextAudio = timeSeconds + state.CadenceSeconds;
            }

            if (state.HapticStrength > 0 && cue != FeedbackCue.None && (cue != hapticCue || timeSeconds >= nextHaptic))
            {
                haptic.Stop();
                double duration = Math.Max(.02, Math.Min(.25, state.CadenceSeconds * .5));
                haptic.Play(cue, state.HapticStrength, duration);
                hapticCue = cue;
                nextHaptic = timeSeconds + state.CadenceSeconds;
            }

            if (state.AudioStrength <= 0)
                audio.Stop();
            if (state.HapticStrength <= 0)
                haptic.Stop();
        }

        public void Suspend()
        {
            StopTransient();
            visual.Clear();
        }

        public void AttemptEnded()
        {
            StopTransient();
            visual.Clear();
        }

        public void AppPaused()
        {
            StopTransient();
        }

        void StopTransient()
        {
            if (stopped)
                return;
            audio.Stop();
            haptic.Stop();
            audioCue = hapticCue = FeedbackCue.None;
            nextAudio = nextHaptic = lastTime;
            stopped = true;
        }
    }
}
