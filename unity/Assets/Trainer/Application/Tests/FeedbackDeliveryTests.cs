using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class FeedbackDeliveryTests
    {
        [Test]
        public void AllModalitiesConsumeSameSemanticState()
        {var visual=new Visual();var audio=new Audio();var haptic=new Haptic();var coordinator=new FeedbackDeliveryCoordinator(visual,audio,haptic);FeedbackState state=State(FeedbackCue.OffPath,.5);coordinator.Update(state,0,true,true);Assert.That(visual.Last.PrimaryCue,Is.EqualTo(FeedbackCue.OffPath));Assert.That(audio.Cue,Is.EqualTo(FeedbackCue.OffPath));Assert.That(haptic.Cue,Is.EqualTo(FeedbackCue.OffPath));Assert.That(audio.Strength,Is.EqualTo(.5));Assert.That(haptic.Strength,Is.EqualTo(.5));}

        [Test]
        public void ZeroAssistanceSuppressesCoachingButMandatoryStillPlays()
        {var visual=new Visual();var audio=new Audio();var haptic=new Haptic();var coordinator=new FeedbackDeliveryCoordinator(visual,audio,haptic);coordinator.Update(State(FeedbackCue.OffPath,0),0,true,true);Assert.That(audio.Plays,Is.Zero);Assert.That(haptic.Plays,Is.Zero);coordinator.Update(State(FeedbackCue.EmergencyStop,1,true),.1,true,true);Assert.That(audio.Plays,Is.EqualTo(1));Assert.That(haptic.Plays,Is.EqualTo(1));}

        [Test]
        public void CooldownPreventsOverlappingWarningSpam()
        {var visual=new Visual();var audio=new Audio();var haptic=new Haptic();var coordinator=new FeedbackDeliveryCoordinator(visual,audio,haptic);FeedbackState warning=State(FeedbackCue.OffPath,1);coordinator.Update(warning,0,true,true);coordinator.Update(warning,.1,true,true);Assert.That(audio.Plays,Is.EqualTo(1));Assert.That(haptic.Plays,Is.EqualTo(1));coordinator.Update(warning,1,true,true);Assert.That(audio.Plays,Is.EqualTo(2));}

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void SuspendTrackingLossPauseAndEndStopRightHaptics(int condition)
        {var visual=new Visual();var audio=new Audio();var haptic=new Haptic();var coordinator=new FeedbackDeliveryCoordinator(visual,audio,haptic);coordinator.Update(State(FeedbackCue.OffPath,1),0,true,true);if(condition==0)coordinator.Update(State(FeedbackCue.TrackingLost,1,true),.1,true,false);else if(condition==1)coordinator.AppPaused();else coordinator.AttemptEnded();Assert.That(haptic.Stops,Is.GreaterThan(0));Assert.That(audio.Stops,Is.GreaterThan(0));}

        [Test]
        public void HigherPriorityCueStopsPreviousOwnedCue()
        {var visual=new Visual();var audio=new Audio();var haptic=new Haptic();var coordinator=new FeedbackDeliveryCoordinator(visual,audio,haptic);coordinator.Update(State(FeedbackCue.TooFast,1),0,true,true);coordinator.Update(State(FeedbackCue.ReflectionRisk,1,true),.01,true,true);Assert.That(audio.Cue,Is.EqualTo(FeedbackCue.ReflectionRisk));Assert.That(haptic.Cue,Is.EqualTo(FeedbackCue.ReflectionRisk));Assert.That(audio.Stops,Is.GreaterThanOrEqualTo(2));}

        static FeedbackState State(FeedbackCue cue,double strength,bool mandatory=false)=>new(cue,cue,mandatory?FeedbackSeverity.Mandatory:FeedbackSeverity.Coaching,strength,strength,strength,strength,1,.5,mandatory,true);
        sealed class Visual:IVisualFeedbackSink{public FeedbackState Last;public int Clears;public void Apply(FeedbackState s)=>Last=s;public void Clear()=>Clears++;}
        sealed class Audio:IAudioFeedbackSink{public FeedbackCue Cue;public double Strength;public int Plays,Stops;public void Play(FeedbackCue c,double s){Cue=c;Strength=s;Plays++;}public void Stop()=>Stops++;}
        sealed class Haptic:IRightHapticFeedbackSink{public FeedbackCue Cue;public double Strength;public int Plays,Stops;public void Play(FeedbackCue c,double s,double d){Cue=c;Strength=s;Plays++;}public void Stop()=>Stops++;}
    }
}
