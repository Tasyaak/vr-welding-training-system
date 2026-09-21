using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Content;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Scenes
{
    /// Production composition root. Missing providers are deliberately inhibiting.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class ProductionTrainerBootstrap : MonoBehaviour
    {
        [SerializeField] private bool _productionEnabled = true;
        [SerializeField] private GameObject _prototypeDemo;
        [SerializeField] private QuestMvpCatalogAsset _selectedCatalog;
        [SerializeField] private MonoBehaviour _inputProvider;
        [SerializeField] private MonoBehaviour _registrationProvider;
        [SerializeField] private MonoBehaviour _safetyProvider;
        [SerializeField] private MonoBehaviour _recorderProvider;
        [SerializeField] private MonoBehaviour _snapshotSink;
        [SerializeField] private MonoBehaviour _hapticsProvider;

        private ProcessCoordinator _coordinator;
        public ProcessSnapshot Current => _coordinator?.Current;
        public QuestMvpCatalogAsset SelectedCatalog => _selectedCatalog;

        private void Awake()
        {
            if (!_productionEnabled) return;
            if (_prototypeDemo != null) _prototypeDemo.SetActive(false);

            ReportMissing(_inputProvider, nameof(_inputProvider));
            ReportMissing(_registrationProvider, nameof(_registrationProvider));
            ReportMissing(_safetyProvider, nameof(_safetyProvider));
            ReportMissing(_recorderProvider, nameof(_recorderProvider));
            ReportMissing(_snapshotSink, nameof(_snapshotSink), required: false);
            ReportMissing(_hapticsProvider, nameof(_hapticsProvider), required: false);

            IInputSnapshotSource input = Resolve<IInputSnapshotSource>(_inputProvider, new MissingInput());
            IRegistrationStateSource registration = Resolve<IRegistrationStateSource>(_registrationProvider, new MissingRegistration());
            IActivationSafetyPort safety = Resolve<IActivationSafetyPort>(_safetyProvider, new MissingSafety());
            IProcessRecorder recorder = Resolve<IProcessRecorder>(_recorderProvider, new MissingRecorder());
            IProcessSnapshotSink sink = Resolve<IProcessSnapshotSink>(_snapshotSink, new LoggingSnapshotSink());
            IHapticLifecyclePort haptics = Resolve<IHapticLifecyclePort>(_hapticsProvider, new MissingHaptics());

            _coordinator = new ProcessCoordinator(new UnityClock(), new GuidIds(), input,
                registration, safety, recorder, sink, haptics);
            _coordinator.StartSession();
            if (_selectedCatalog == null)
                Debug.LogError("Production content catalog is missing. Process remains inhibited.", this);
        }

        private void Update() => _coordinator?.Tick();
        private void OnDisable() { _coordinator?.Dispose(); _coordinator = null; }

        public ContentSnapshot FreezeSelectedContent()
        {
            if (_selectedCatalog == null) throw new InvalidOperationException("No local catalog is selected.");
            return new ContentResolver().ResolveForAttempt(_selectedCatalog.Bake());
        }

        private static T Resolve<T>(MonoBehaviour component, T missing) where T : class
        {
            if (component == null) return missing;
            if (component is T result) return result;
            throw new InvalidOperationException($"{component.name} must implement {typeof(T).Name}.");
        }

        private void ReportMissing(MonoBehaviour component, string field, bool required = true)
        {
            if (component == null)
                Debug.LogWarning($"Production provider {field} is missing; " +
                    (required ? "process activation remains inhibited." : "the fallback is non-operational."), this);
        }

        private sealed class UnityClock : IMonotonicClock { public double Seconds => Time.realtimeSinceStartupAsDouble; }
        private sealed class GuidIds : IIdentifierSource { public string NewId() => Guid.NewGuid().ToString("N"); }
        private sealed class MissingInput : IInputSnapshotSource
        { public InputSnapshot Capture(double now) => new(false, false, false, false, false, 0, now); }
        private sealed class MissingRegistration : IRegistrationStateSource
        { public RegistrationSnapshot Capture(double now) => new(false, false, 0); public void Teardown() { } }
        private sealed class MissingSafety : IActivationSafetyPort
        { public SafetyDecision Evaluate(EvaluationRequest request) => SafetyDecision.Missing; }
        private sealed class MissingRecorder : IProcessRecorder
        {
            public RecorderHealth Health => RecorderHealth.Unavailable;
            public void Append(ProcessEvent processEvent) { }
            public void BeginAttempt(AttemptConfiguration configuration) { }
            public bool FinishAttempt(string attemptId, bool interrupted, out string error)
            { error = "Production recorder provider is missing."; return false; }
            public void Teardown() { }
        }
        private sealed class MissingHaptics : IHapticLifecyclePort { public void StopImmediately() { } }
        private sealed class LoggingSnapshotSink : IProcessSnapshotSink
        {
            private SessionLifecycle _last = (SessionLifecycle)(-1);
            public void Publish(ProcessSnapshot snapshot)
            {
                if (snapshot.Lifecycle == _last) return;
                _last = snapshot.Lifecycle;
                Debug.Log($"Production trainer: {snapshot.Lifecycle}; activation={snapshot.Activation}; inhibit={snapshot.InhibitReasons}");
            }
        }
    }
}
