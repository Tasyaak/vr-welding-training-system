using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Content;

namespace WeldingTrainer.Platform.Meta
{
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class FourPointRegistrationAdapter : MonoBehaviour,
        IRegistrationStateSource, IRegistrationInputConsumer, ICalibrationWorkflow
    {
        [SerializeField] private QuestMvpCatalogAsset _catalog;
        [SerializeField] private MetaSessionAnchorAdapter _anchor;
        private FixtureCalibrationCoordinator _coordinator;
        public FixtureCalibrationCoordinator Coordinator => _coordinator;
        public RegistrationWorkflowState State => _coordinator?.State ?? RegistrationWorkflowState.Unregistered;
        public string CurrentPointId => _coordinator?.CurrentPointId;
        public WeldingTrainer.Domain.RegistrationCandidate Candidate => _coordinator?.Candidate;

        private void Awake()
        {
            if(_anchor==null)_anchor=GetComponent<MetaSessionAnchorAdapter>();
            if(_anchor==null)throw new InvalidOperationException("MetaSessionAnchorAdapter is required.");
            _coordinator=new FixtureCalibrationCoordinator(_anchor);
        }

        public void UpdateInput(InputSnapshot input,double now)
        {
            if(_catalog==null)return;
            if(_coordinator.State==RegistrationWorkflowState.Unregistered)
                _coordinator.Begin(_catalog.FreezeForAttempt(),input.Generation,now);
            _coordinator.Tick(input,now);
        }

        public RegistrationSnapshot Capture(double monotonicSeconds)=>_coordinator?.Capture(monotonicSeconds)??default;
        public void AcceptPreview()=>_coordinator.AcceptPreview();
        public void Recapture(string pointId)=>_coordinator.Recapture(pointId,Time.realtimeSinceStartupAsDouble);
        public void ReportFixtureMoved()=>_coordinator.Invalidate("fixture-moved-by-user");
        public void Teardown()=>_coordinator?.Teardown();
        private void OnDisable()=>Teardown();
    }
}
