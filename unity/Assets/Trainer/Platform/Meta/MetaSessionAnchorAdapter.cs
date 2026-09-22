using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Content;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Platform.Meta
{
    /// Session-only Meta spatial anchor. It never invokes save, share or load APIs.
    public sealed class MetaSessionAnchorAdapter : MonoBehaviour, ISessionAnchorPort
    {
        [SerializeField, Min(1f)] private float _localizationTimeoutSeconds = 10f;
        private GameObject _anchorObject;
        private Component _spatialAnchor;
        private Coroutine _creation;
        public bool IsLocalized => ReadBoolean("Localized");

        public void BeginCreate(RigidPose worldFromFixture, long generation,
            Action<long, AnchorCreationResult> completed)
        {
            DestroyAnchor();
            Type type = Type.GetType("OVRSpatialAnchor, Oculus.VR");
            if (type == null) { completed(generation,new AnchorCreationResult(false,false,"OVRSpatialAnchor type unavailable")); return; }
            UnityDomainConversion.ToUnityPose(worldFromFixture,out Vector3 position,out Quaternion rotation);
            _anchorObject=new GameObject("SessionOnlyFixtureAnchor");
            _anchorObject.transform.SetPositionAndRotation(position,rotation);
            _spatialAnchor=_anchorObject.AddComponent(type);
            _creation=StartCoroutine(WaitForLocalization(generation,completed));
        }

        private IEnumerator WaitForLocalization(long generation,Action<long,AnchorCreationResult> completed)
        {
            float deadline=Time.realtimeSinceStartup+_localizationTimeoutSeconds;
            while(_spatialAnchor!=null&&Time.realtimeSinceStartup<deadline)
            {
                if(ReadBoolean("Created")&&ReadBoolean("Localized"))
                { _creation=null;completed(generation,new AnchorCreationResult(true,true,null));yield break; }
                yield return null;
            }
            _creation=null;completed(generation,new AnchorCreationResult(false,false,"Session anchor creation/localization timed out"));
        }

        private bool ReadBoolean(string property)
        {
            if(_spatialAnchor==null)return false;
            PropertyInfo info=_spatialAnchor.GetType().GetProperty(property,BindingFlags.Instance|BindingFlags.Public);
            return info!=null&&info.PropertyType==typeof(bool)&&(bool)info.GetValue(_spatialAnchor);
        }

        public void DestroyAnchor()
        {
            if(_creation!=null){StopCoroutine(_creation);_creation=null;}
            if(_anchorObject!=null)Destroy(_anchorObject);
            _anchorObject=null;_spatialAnchor=null;
        }
        private void OnDisable()=>DestroyAnchor();
    }
}
