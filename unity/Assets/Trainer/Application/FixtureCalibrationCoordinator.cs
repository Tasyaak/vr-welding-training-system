using System;
using System.Collections.Generic;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum RegistrationWorkflowState
    { Unregistered, Capturing, Solving, Preview, CreatingAnchor, Registered, Lost, Disposed }

    public sealed class FixtureCalibrationCoordinator : IRegistrationStateSource
    {
        private readonly ISessionAnchorPort _anchor;
        private readonly List<CalibrationPointCapture> _captures = new();
        private readonly List<Vector3d> _window = new();
        private ContentSnapshot _content;
        private RegistrationCandidate _candidate;
        private RegistrationSnapshot _snapshot;
        private AnchorCreationResult? _pendingAnchor;
        private long _pendingAnchorGeneration, _generation, _originGeneration;
        private int _pointIndex;
        private double _windowStarted, _lastSampleTime;
        private Vector3d _lastTip;
        private Quaterniond _firstTipRotation;
        private bool _hasLastTip;

        public const double MinimumCaptureSeconds = 0.15;
        public const double MaximumTipSpeedMps = 0.03;
        public const double MaximumOrientationSpreadRadians = Math.PI / 60.0;
        public RegistrationWorkflowState State { get; private set; } = RegistrationWorkflowState.Unregistered;
        public RegistrationCandidate Candidate => _candidate;
        public string CurrentPointId => State == RegistrationWorkflowState.Capturing && _content != null
            ? _content.Entry.Fixture.ReferencePoints[_pointIndex].Id : null;

        public FixtureCalibrationCoordinator(ISessionAnchorPort anchor)
        { _anchor=anchor ?? throw new ArgumentNullException(nameof(anchor)); Publish(false,"not-started"); }

        public void Begin(ContentSnapshot content, long originGeneration, double now)
        {
            if(content==null)throw new ArgumentNullException(nameof(content));
            _anchor.DestroyAnchor();_content=content;_originGeneration=originGeneration;_generation++;
            _captures.Clear();_pointIndex=0;_candidate=null;ResetWindow(now);
            State=RegistrationWorkflowState.Capturing;Publish(false,"capture-"+CurrentPointId);
        }

        public void Tick(InputSnapshot input, double now)
        {
            if(_pendingAnchor.HasValue){var result=_pendingAnchor.Value;_pendingAnchor=null;
                if(_pendingAnchorGeneration==_generation && State==RegistrationWorkflowState.CreatingAnchor){
                    if(result.Success&&result.Localized&&_anchor.IsLocalized){State=RegistrationWorkflowState.Registered;Publish(true,null);}
                    else {State=RegistrationWorkflowState.Lost;Publish(false,result.Error??"anchor-not-localized");}}}
            if(State==RegistrationWorkflowState.Registered && !_anchor.IsLocalized){State=RegistrationWorkflowState.Lost;_generation++;Publish(false,"anchor-lost");}
            if(State!=RegistrationWorkflowState.Capturing)return;
            if(input.Generation!=_originGeneration){Invalidate("origin-generation-changed");return;}
            if(!input.Available||!input.Tip.IsValid){ResetWindow(now);Publish(false,"tip-tracking-invalid");return;}
            Vector3d tip=input.Tip.Pose.PositionMetres;
            if(input.SourceTimestampSeconds<=_lastSampleTime)return;
            Quaterniond rotation=input.Tip.Pose.Rotation;
            if(_window.Count==0)_firstTipRotation=rotation;
            else if(QuaternionAngle(_firstTipRotation,rotation)>MaximumOrientationSpreadRadians)
            {ResetWindow(now);Publish(false,"tool-orientation-unstable");return;}
            if(_hasLastTip && Vector3d.Distance(tip,_lastTip)/(input.SourceTimestampSeconds-_lastSampleTime)>MaximumTipSpeedMps)
            {ResetWindow(now);Publish(false,"tip-moving");return;}
            _window.Add(tip);_lastTip=tip;_lastSampleTime=input.SourceTimestampSeconds;_hasLastTip=true;
            if((input.CommandEdges&InputCommandEdges.Confirm)!=0)ConfirmCurrent(now);
        }

        private void ConfirmCurrent(double now)
        {
            CalibrationPolicy policy=_content.Entry.Fixture.Calibration;
            if(now-_windowStarted<MinimumCaptureSeconds){Publish(false,"capture-window-too-short");return;}
            try{_captures.Add(CalibrationCaptureMath.Build(CurrentPointId,_window,Math.Max(3,policy.SamplesPerPoint),
                policy.StabilityRadiusMetres,_originGeneration));}
            catch(ArgumentException e){ResetWindow(now);Publish(false,e.Message);return;}
            ResetWindow(now);
            if(_captures.Count<4){_pointIndex=NextUncapturedPoint();Publish(false,"capture-"+CurrentPointId);return;}
            State=RegistrationWorkflowState.Solving;
            _candidate=FourPointRigidSolver.Solve(_content.Entry.Fixture,_captures);
            State=RegistrationWorkflowState.Preview;
            Publish(false,_candidate.Quality.Reason.ToString());
        }

        public void AcceptPreview()
        {
            if(State!=RegistrationWorkflowState.Preview||_candidate==null||!_candidate.Quality.Accepted)
                throw new InvalidOperationException("Only a numerically accepted preview can be confirmed.");
            State=RegistrationWorkflowState.CreatingAnchor;Publish(false,"creating-session-anchor");
            long requested=_generation;
            _anchor.BeginCreate(_candidate.WorldFromFixture,requested,(generation,result)=>
            { if(generation==_generation){_pendingAnchorGeneration=generation;_pendingAnchor=result;} });
        }

        public void Recapture(string pointId,double now)
        {
            if(State!=RegistrationWorkflowState.Preview&&State!=RegistrationWorkflowState.Capturing)
                throw new InvalidOperationException("Recapture is not available in this state.");
            _pointIndex=IndexOfPoint(pointId);_captures.RemoveAll(x=>x.PointId==pointId);
            State=RegistrationWorkflowState.Capturing;ResetWindow(now);Publish(false,"recapture-"+pointId);
        }

        public void Invalidate(string reason)
        { _generation++;_anchor.DestroyAnchor();State=RegistrationWorkflowState.Lost;Publish(false,reason); }
        public RegistrationSnapshot Capture(double monotonicSeconds)=>_snapshot;
        public void Teardown(){_generation++;_anchor.DestroyAnchor();State=RegistrationWorkflowState.Disposed;Publish(false,"disposed");}
        private void ResetWindow(double now){_window.Clear();_windowStarted=now;_lastSampleTime=double.NegativeInfinity;_hasLastTip=false;}
        private static double QuaternionAngle(Quaterniond a,Quaterniond b)
        {double dot=Math.Abs(a.X*b.X+a.Y*b.Y+a.Z*b.Z+a.W*b.W);return 2*Math.Acos(Math.Min(1,Math.Max(-1,dot)));}
        private int IndexOfPoint(string id){for(int i=0;i<4;i++)if(_content.Entry.Fixture.ReferencePoints[i].Id==id)return i;throw new ArgumentException("Unknown point.");}
        private int NextUncapturedPoint(){for(int i=0;i<4;i++){string id=_content.Entry.Fixture.ReferencePoints[i].Id;if(!_captures.Exists(x=>x.PointId==id))return i;}return 0;}
        private void Publish(bool valid,string reason){_snapshot=new RegistrationSnapshot(true,valid,_generation,State,
            _candidate?.WorldFromFixture??default,_candidate?.WorldFromWorkpiece??default,reason);}
    }
}
