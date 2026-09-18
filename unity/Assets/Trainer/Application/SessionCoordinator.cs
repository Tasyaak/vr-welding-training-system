using System;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum SessionState
    {
        Idle,
        Registering,
        SelectingSeam,
        Ready,
        Running,
        Suspended,
        Reviewing,
        Saving,
        Faulted,
    }

    /// Coordinates the full lifecycle of one training session.
    ///
    /// Boot → Registering → SelectingSeam → Ready → Running ↔ Suspended → Reviewing → Saving → Idle
    ///   └──────────────────────────── Faulted ────────────────────────────────────────────────────┘
    public sealed class SessionCoordinator
    {
        private readonly IToolTracker _toolTracker;
        private readonly IRegistrationSource _registration;
        private readonly ISessionStorage _storage;

        private string _sessionId;
        private string _attemptId;
        private WorkpieceDefinition _workpiece;
        private int _selectedSeamIndex;
        private StraightSeam _activeSeam;
        private WeldEvaluator _evaluator;
        private AttemptMetrics _metrics;
        private float _lastProgress;
        private bool _attemptComplete;
        private AttemptSummary _lastSummary;
        private string _faultMessage;

        public SessionState State { get; private set; } = SessionState.Idle;
        public EvaluationResult LastResult { get; private set; }
        public AttemptSummary LastSummary => _lastSummary;
        public string FaultMessage => _faultMessage;

        public event Action<SessionState> StateChanged;
        public event Action<EvaluationResult> SampleEvaluated;
        public event Action<AttemptSummary> AttemptFinished;

        public SessionCoordinator(
            IToolTracker toolTracker,
            IRegistrationSource registration,
            ISessionStorage storage)
        {
            _toolTracker = toolTracker ?? throw new ArgumentNullException(nameof(toolTracker));
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public void StartSession()
        {
            if (State != SessionState.Idle)
                return;
            _sessionId = Guid.NewGuid().ToString("N");
            _registration.BeginScanning();
            Transition(SessionState.Registering);
        }

        /// Call each frame while in any active state.
        public void Tick(float timestampSeconds)
        {
            switch (State)
            {
                case SessionState.Registering:
                    TickRegistering();
                    break;
                case SessionState.Running:
                    TickRunning(timestampSeconds);
                    break;
                case SessionState.Suspended:
                    TickSuspended(timestampSeconds);
                    break;
            }
        }

        public void SelectSeam(int seamIndex)
        {
            if (State != SessionState.SelectingSeam)
                return;
            if (_workpiece == null || seamIndex < 0 || seamIndex >= _workpiece.Seams.Length)
            {
                Fault($"Seam index {seamIndex} is out of range.");
                return;
            }
            _selectedSeamIndex = seamIndex;
            _activeSeam = _workpiece.BuildWorldSeam(seamIndex, _registration.LocalToWorld);
            _evaluator = new WeldEvaluator(
                _activeSeam,
                _workpiece.Seams[seamIndex].Tolerance,
                _workpiece.Seams[seamIndex].LocalToolForwardAxis);
            Transition(SessionState.Ready);
        }

        public void BeginAttempt()
        {
            if (State != SessionState.Ready && State != SessionState.Reviewing)
                return;
            _attemptId = Guid.NewGuid().ToString("N");
            _metrics = new AttemptMetrics();
            _evaluator.BeginAttempt();
            _lastProgress = 0f;
            _attemptComplete = false;
            _storage.BeginSession(_sessionId, _workpiece, _selectedSeamIndex);
            Transition(SessionState.Running);
        }

        public void EndAttempt(string reason = "user-ended")
        {
            if (State != SessionState.Running && State != SessionState.Suspended)
                return;
            BuildAndStoreSummary(false, reason);
        }

        public void RetryAttempt()
        {
            if (State != SessionState.Reviewing)
                return;
            BeginAttempt();
        }

        public void EndSession()
        {
            _registration.StopScanning();
            Transition(SessionState.Idle);
        }

        private void TickRegistering()
        {
            if (_registration.State == RegistrationState.Acquired)
            {
                _workpiece = _registration.WorkpieceDefinition;
                Transition(SessionState.SelectingSeam);
            }
        }

        private void TickRunning(float timestampSeconds)
        {
            ToolSample sample = _toolTracker.GetSample(timestampSeconds);
            if (!sample.IsTracked)
            {
                _metrics.RecordSample(
                    new EvaluationResult(
                        _lastProgress, 0, 0, 0, 0,
                        false, false, false, false, WeldingStatus.Suspended),
                    timestampSeconds);
                Transition(SessionState.Suspended);
                return;
            }

            EvaluationResult result = _evaluator.Evaluate(sample);
            _metrics.RecordSample(result, timestampSeconds);
            _storage.RecordSample(sample, result, timestampSeconds);
            LastResult = result;
            _lastProgress = result.Progress;
            SampleEvaluated?.Invoke(result);

            if (!_attemptComplete && _evaluator.IsComplete(result.Progress))
            {
                _attemptComplete = true;
                BuildAndStoreSummary(true, "completed");
            }
        }

        private void TickSuspended(float timestampSeconds)
        {
            if (_registration.State == RegistrationState.Lost)
            {
                Fault("Workpiece registration was lost during the attempt.");
                return;
            }
            ToolSample sample = _toolTracker.GetSample(timestampSeconds);
            if (sample.IsTracked && !sample.TriggerPressed)
                Transition(SessionState.Running);
        }

        private void BuildAndStoreSummary(bool completed, string reason)
        {
            _lastSummary = new AttemptSummary(
                _sessionId, _attemptId, completed, reason,
                _lastProgress, _metrics, _workpiece, _selectedSeamIndex);
            _storage.FinishAttempt(_lastSummary);
            AttemptFinished?.Invoke(_lastSummary);
            Transition(SessionState.Reviewing);
        }

        private void Fault(string message)
        {
            _faultMessage = message;
            Debug.LogError($"[SessionCoordinator] Fault: {message}");
            Transition(SessionState.Faulted);
        }

        private void Transition(SessionState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
