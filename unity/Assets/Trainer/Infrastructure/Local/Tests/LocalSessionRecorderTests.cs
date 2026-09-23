using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Infrastructure.Local.Tests
{
    public sealed class LocalSessionRecorderTests
    {
        string root;
        [SetUp] public void Setup(){root=Path.Combine(Path.GetTempPath(),"welding-recorder-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);}
        [TearDown] public void Down(){if(Directory.Exists(root))Directory.Delete(root,true);}

        [Test]
        public void CompletedAttemptCommitsManifestLastWithChunkIntegrity()
        {using var recorder=Create("session-a");AttemptConfiguration attempt=Attempt("attempt-a");recorder.Begin(attempt);Assert.That(recorder.TryAppend(Sample(0,.02,true)),Is.True);Assert.That(recorder.TryAppend(Sample(.02,.02,true)),Is.True);Assert.That(recorder.Finish(attempt.AttemptId,false,out string error),Is.True,error);string attemptRoot=Path.Combine(root,"Sessions","session-a","attempts","attempt-a");Assert.That(File.Exists(Path.Combine(attemptRoot,"manifest.json")),Is.True);Assert.That(File.ReadAllText(Path.Combine(attemptRoot,"summary.json")),Does.Contain("\"scored\":true"));Assert.That(Directory.GetFiles(attemptRoot,"*.partial",SearchOption.AllDirectories),Is.Empty);Assert.That(File.ReadAllText(Path.Combine(attemptRoot,"manifest.json")),Does.Contain("sha256"));}

        [Test]
        public void InvalidEvidenceCannotProduceSuccessfulScore()
        {using var recorder=Create("session-b");var attempt=Attempt("attempt-b");recorder.Begin(attempt);recorder.TryAppend(Sample(0,1,false));recorder.TryAppend(Sample(1,1,false));Assert.That(recorder.Finish(attempt.AttemptId,false,out _),Is.True);string summary=File.ReadAllText(Path.Combine(root,"Sessions","session-b","attempts","attempt-b","summary.json"));Assert.That(summary,Does.Contain("\"scored\":false"));Assert.That(summary,Does.Contain("\"invalidSeconds\":2"));}

        [Test]
        public void SmallChunkPolicyRotatesAppendOnlyFiles()
        {using var recorder=Create("session-c",new RecordingOptions(128,8,1024,.01,20));var attempt=Attempt("attempt-c");recorder.Begin(attempt);for(int i=0;i<4;i++)recorder.TryAppend(Sample(i*.02,.02,true));Assert.That(recorder.Finish(attempt.AttemptId,false,out _),Is.True);Assert.That(Directory.GetFiles(Path.Combine(root,"Sessions","session-c","attempts","attempt-c"),"records-*.jsonl").Length,Is.GreaterThan(1));}

        [Test]
        public void InterruptedActiveAttemptIsNotReportedComplete()
        {var recorder=Create("session-d");recorder.Begin(Attempt("attempt-d"));recorder.TryAppend(Sample(0,.02,true));recorder.Dispose();SessionInspection inspection=LocalSessionRecorder.Inspect(Path.Combine(root,"Sessions","session-d"));Assert.That(inspection.Complete,Is.False);Assert.That(inspection.ManifestPresent,Is.False);}

        [Test]
        public void RetentionProtectsCurrentSessionAndRemovesOldEligibleOnes()
        {Directory.CreateDirectory(Path.Combine(root,"Sessions","old-a"));Directory.CreateDirectory(Path.Combine(root,"Sessions","old-b"));using var recorder=Create("active",new RecordingOptions(maximumSessions:2));Assert.That(Directory.Exists(Path.Combine(root,"Sessions","active")),Is.True);Assert.That(Directory.GetDirectories(Path.Combine(root,"Sessions")).Length,Is.EqualTo(2));}

        [Test]
        public void SimulatedFullDiskFailsClosedWithoutCompletionManifest()
        {bool reject=false;using var recorder=new LocalSessionRecorder(root,new SessionMetadata("full","app","eval","binding","hash","synthetic"),writeGuard:_=>{if(reject)throw new IOException("disk full");});var attempt=Attempt("attempt-full");recorder.Begin(attempt);reject=true;recorder.TryAppend(Sample(0,.02,true));Assert.That(SpinWait.SpinUntil(()=>!recorder.Available,1000),Is.True);Assert.That(recorder.Finish(attempt.AttemptId,false,out string error),Is.False);Assert.That(error,Does.Contain("disk full"));Assert.That(File.Exists(Path.Combine(root,"Sessions","full","attempts","attempt-full","manifest.json")),Is.False);reject=false;}

        LocalSessionRecorder Create(string id,RecordingOptions options=null)=>new(root,new SessionMetadata(id,"app-1","eval-1","binding","hash","synthetic"),options);
        static AttemptConfiguration Attempt(string id)=>new(id,new ProcessConfiguration("workpiece","seam",new ProcessProfile("profile",2,ProcessMode.Fusion,"profile-hash",.1)),7);
        static AttemptSample Sample(double time,double delta,bool valid)=>new(time,delta,new Vec3(.1,.2,.3),new Quat(0,0,0,1),valid,valid,7,7,.5,.002,.1,RecordedSpeedClass.InRange,.01,.02,.5,true,true,true,true,false,true,BlockReason.None);
    }
}
