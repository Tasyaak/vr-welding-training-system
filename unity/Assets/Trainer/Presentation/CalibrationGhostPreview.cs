using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    /// Read-only registration preview. UI buttons may call AcceptPreview/Recapture.
    public sealed class CalibrationGhostPreview : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour _workflowSource;
        [SerializeField] private Transform _fixtureGhost;
        [SerializeField] private LineRenderer _residualLines;
        private ICalibrationWorkflow _workflow;
        public string Prompt { get; private set; }

        private void Awake()
        {
            _workflow=_workflowSource as ICalibrationWorkflow;
            if(_workflow==null)throw new InvalidOperationException("Workflow source must implement ICalibrationWorkflow.");
        }

        private void LateUpdate()
        {
            Prompt=_workflow.State==RegistrationWorkflowState.Capturing
                ? "Hold tip steady on "+_workflow.CurrentPointId+" and press A"
                : _workflow.State==RegistrationWorkflowState.Preview
                    ? "Inspect ghost and residuals; accept or recapture a labeled point"
                    : _workflow.State.ToString();
            var candidate=_workflow.Candidate;
            bool visible=candidate!=null&&(_workflow.State==RegistrationWorkflowState.Preview||
                _workflow.State==RegistrationWorkflowState.CreatingAnchor||_workflow.State==RegistrationWorkflowState.Registered);
            if(_fixtureGhost!=null){_fixtureGhost.gameObject.SetActive(visible);if(visible){ToUnityPose(
                candidate.WorldFromFixture,out Vector3 p,out Quaternion q);_fixtureGhost.SetPositionAndRotation(p,q);}}
            if(_residualLines!=null){_residualLines.gameObject.SetActive(visible);if(visible)DrawResiduals(candidate);}
        }

        private void DrawResiduals(WeldingTrainer.Domain.RegistrationCandidate candidate)
        {
            int count=candidate.Captures.Count*3;_residualLines.positionCount=count;
            for(int i=0;i<candidate.Captures.Count;i++){
                Vector3 measured=ToUnity(candidate.Captures[i].RepresentativeWorld);
                Vector3 residual=ToUnity(candidate.Quality.ResidualVectors[i]);
                int n=i*3;_residualLines.SetPosition(n,measured);_residualLines.SetPosition(n+1,measured-residual);
                _residualLines.SetPosition(n+2,new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity));}
        }

        public void AcceptPreview()=>_workflow.AcceptPreview();
        public void Recapture(string pointId)=>_workflow.Recapture(pointId);
        private static Vector3 ToUnity(Vector3d v)=>new((float)v.X,(float)v.Y,(float)v.Z);
        private static void ToUnityPose(RigidPose pose,out Vector3 p,out Quaternion q)
        { p=ToUnity(pose.PositionMetres);q=new Quaternion((float)pose.Rotation.X,(float)pose.Rotation.Y,(float)pose.Rotation.Z,(float)pose.Rotation.W); }
    }
}
