using UnityEngine;
using WeldingTrainer.Application;

namespace WeldingTrainer.Scenes
{
    [DisallowMultipleComponent]
    public sealed class UnifiedSafetyAdapter : MonoBehaviour, IActivationSafetyPort, IReflectionRiskSource
    {
        [SerializeField] private MonoBehaviour _prospectiveRiskProvider;
        [SerializeField] private MonoBehaviour _processPrerequisiteProvider;
        private UnifiedActivationSafety _safety;
        private ReflectionRiskService _defaultReflection;
        private IReflectionRiskSource _riskSource;
        private void Awake()
        {
            IProspectiveRiskPort risk=_prospectiveRiskProvider as IProspectiveRiskPort;
            if(risk==null)risk=_defaultReflection=new ReflectionRiskService();
            _riskSource=risk as IReflectionRiskSource;
            _safety=new UnifiedActivationSafety(risk,_processPrerequisiteProvider as IProcessPrerequisitePort);
        }
        public SafetyDecision Evaluate(EvaluationRequest request)=>_safety.Evaluate(request);
        public void AcknowledgeReflectionFault()=>_defaultReflection?.RequestAcknowledgement();
        public WeldingTrainer.Domain.ReflectionRiskResult LastResult=>_riskSource?.LastResult;
        public bool ReflectionLatched=>_riskSource?.ReflectionLatched??false;
    }
}
