using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application.Tests
{
    public sealed class SemanticFeedbackTests
    {
        [TestCase(0,0)] [TestCase(50,.5)] [TestCase(100,1)]
        public void AssistanceScalesCoachingOnly(double percent,double expected)
        {var engine=new SemanticFeedbackEngine();FeedbackState state=engine.Evaluate(Input(0,offPath:true),percent);Assert.That(state.PrimaryCue,Is.EqualTo(FeedbackCue.OffPath));Assert.That(state.VisualStrength,Is.EqualTo(expected).Within(1e-9));Assert.That(state.AudioStrength,Is.EqualTo(expected).Within(1e-9));Assert.That(state.HapticStrength,Is.EqualTo(expected).Within(1e-9));}

        [TestCase(0)] [TestCase(50)] [TestCase(100)]
        public void MandatorySafetyRemainsAtEveryAssistanceLevel(double percent)
        {var engine=new SemanticFeedbackEngine();FeedbackState state=engine.Evaluate(Input(0,reasons:BlockReason.EmergencyStop,offPath:true),percent);Assert.That(state.PrimaryCue,Is.EqualTo(FeedbackCue.EmergencyStop));Assert.That(state.Mandatory,Is.True);Assert.That(state.VisualStrength,Is.EqualTo(1));}

        [Test]
        public void InvalidOrMismatchedInputCannotBeMetricsEligible()
        {var engine=new SemanticFeedbackEngine();Assert.That(engine.Evaluate(Input(0,inputValid:false),100).MetricsEligible,Is.False);engine.Reset();Assert.That(engine.Evaluate(Input(0,inputGeneration:2),100).MetricsEligible,Is.False);}

        [Test]
        public void SafetyImmediatelyOverridesRetainedCoaching()
        {var engine=new SemanticFeedbackEngine(new AssistanceProfile(1));engine.Evaluate(Input(0,offPath:true),50);FeedbackState result=engine.Evaluate(Input(.1,reasons:BlockReason.SafetyRejected,offPath:true),50);Assert.That(result.PrimaryCue,Is.EqualTo(FeedbackCue.ReflectionRisk));}

        [Test]
        public void CoachingCueUsesDeterministicMinimumDuration()
        {var engine=new SemanticFeedbackEngine(new AssistanceProfile(1));engine.Evaluate(Input(0,offPath:true),50);FeedbackState retained=engine.Evaluate(Input(.1,offPath:true,tooFast:true),50);Assert.That(retained.PrimaryCue,Is.EqualTo(FeedbackCue.OffPath));}

        [Test]
        public void AssistanceNeverChangesSemanticInputOrMetricEligibility()
        {var a=new SemanticFeedbackEngine().Evaluate(Input(0,tooSlow:true),0);var b=new SemanticFeedbackEngine().Evaluate(Input(0,tooSlow:true),100);Assert.That(a.ActiveCues,Is.EqualTo(b.ActiveCues));Assert.That(a.MetricsEligible,Is.EqualTo(b.MetricsEligible));Assert.That(a.Progress,Is.EqualTo(b.Progress));}

        [Test]
        public void NonMonotonicTimeIsRejected()
        {var engine=new SemanticFeedbackEngine();engine.Evaluate(Input(1),50);Assert.Throws<System.ArgumentException>(()=>engine.Evaluate(Input(.9),50));}

        static FeedbackInput Input(double time,bool inputValid=true,long inputGeneration=1,BlockReason reasons=BlockReason.None,bool offPath=false,bool tooSlow=false,bool tooFast=false)=>
            new(inputGeneration,1,time,.25,inputValid,true,reasons,offPath,tooSlow,!tooSlow&&!tooFast,tooFast,false,false,false,false,false);
    }
}
