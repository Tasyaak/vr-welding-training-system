using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    public sealed class ReflectionHintRenderer : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour _riskSource;
        [SerializeField] private LineRenderer _idealRay;
        [SerializeField,Range(0,100)] private float _assistancePercent=100;
        private IReflectionRiskSource _source;
        public ReflectionRiskLevel EssentialStatus=>_source?.LastResult?.Level??ReflectionRiskLevel.Unknown;
        private void Awake()=>_source=_riskSource as IReflectionRiskSource??throw new InvalidOperationException("Risk source is required.");
        private void LateUpdate(){ReflectionRiskResult r=_source.LastResult;bool show=_assistancePercent>0&&r!=null&&r.Level!=ReflectionRiskLevel.Unknown;
            if(_idealRay==null)return;_idealRay.gameObject.SetActive(show);if(show){_idealRay.positionCount=2;_idealRay.SetPosition(0,ToUnity(r.Impact));_idealRay.SetPosition(1,ToUnity(r.Impact+r.Reflected*.5));}}
        private static Vector3 ToUnity(Vector3d v)=>new((float)v.X,(float)v.Y,(float)v.Z);
    }
}
