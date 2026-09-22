using UnityEngine;
using WeldingTrainer.Application;

namespace WeldingTrainer.Scenes
{
    [DisallowMultipleComponent]
    public sealed class UnifiedSafetyAdapter : MonoBehaviour, IActivationSafetyPort
    {
        [SerializeField] private MonoBehaviour _prospectiveRiskProvider;
        [SerializeField] private MonoBehaviour _processPrerequisiteProvider;
        private UnifiedActivationSafety _safety;
        private void Awake()=>_safety=new UnifiedActivationSafety(
            _prospectiveRiskProvider as IProspectiveRiskPort,
            _processPrerequisiteProvider as IProcessPrerequisitePort);
        public SafetyDecision Evaluate(EvaluationRequest request)=>_safety.Evaluate(request);
    }
}
