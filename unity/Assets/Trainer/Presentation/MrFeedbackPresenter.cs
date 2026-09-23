using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    /// <summary>Maps authoritative semantic feedback to workpiece-local, non-occluding MR guides.</summary>
    public sealed class MrFeedbackPresenter : MonoBehaviour, IVisualFeedbackSink
    {
        [Header("Workpiece-local guides")]
        [SerializeField] LineRenderer seamGuide;
        [SerializeField] LineRenderer completedGuide;
        [SerializeField] LineRenderer positionGuide;
        [SerializeField] GameObject startMarker;
        [SerializeField] GameObject endMarker;
        [SerializeField] GameObject travelAngleGuide;
        [SerializeField] GameObject workAngleGuide;
        [SerializeField] Renderer speedIndicator;
        [Header("Status")]
        [SerializeField] TextMesh mandatoryStatus;
        [SerializeField] TextMesh completionStatus;
        [SerializeField] Color normalColour = new Color(0.15f, 0.85f, 1f, 0.8f);
        [SerializeField] Color goodColour = new Color(0.1f, 1f, 0.3f, 0.85f);
        [SerializeField] Color warningColour = new Color(1f, 0.55f, 0.05f, 0.9f);
        [SerializeField] Color dangerColour = new Color(1f, 0.1f, 0.1f, 0.95f);

        Vector3[] seamPoints = Array.Empty<Vector3>();

        public void ConfigureWorkpieceLocal(Vector3[] points)
        {
            seamPoints = points == null ? Array.Empty<Vector3>() : (Vector3[])points.Clone();
            ConfigureLine(seamGuide, seamPoints);
            ConfigureLine(completedGuide, Array.Empty<Vector3>());
            if (seamPoints.Length > 0)
            {
                if (startMarker) startMarker.transform.localPosition = seamPoints[0];
                if (endMarker) endMarker.transform.localPosition = seamPoints[seamPoints.Length - 1];
            }
        }

        public void Apply(FeedbackState state)
        {
            float strength = Mathf.Clamp01((float)state.VisualStrength);
            bool visible = state.Mandatory || strength > 0f;
            SetActive(seamGuide, visible);
            SetActive(startMarker, visible);
            SetActive(endMarker, visible);
            SetActive(positionGuide, visible && Has(state, FeedbackCue.OffPath));
            SetActive(travelAngleGuide, visible && Has(state, FeedbackCue.TravelAngleError));
            SetActive(workAngleGuide, visible && Has(state, FeedbackCue.WorkAngleError));
            SetActive(speedIndicator, visible && HasAny(state, FeedbackCue.TooSlow | FeedbackCue.CorrectSpeed | FeedbackCue.TooFast));
            UpdateProgress(state.Progress);
            SetColour(speedIndicator, Has(state, FeedbackCue.CorrectSpeed) ? goodColour : warningColour);
            if (mandatoryStatus)
            {
                mandatoryStatus.gameObject.SetActive(state.Mandatory);
                mandatoryStatus.text = state.Mandatory ? Label(state.PrimaryCue) : string.Empty;
                mandatoryStatus.color = dangerColour;
            }
            if (completionStatus)
            {
                bool complete = Has(state, FeedbackCue.Complete);
                completionStatus.gameObject.SetActive(complete);
                completionStatus.text = complete ? "COMPLETE" : string.Empty;
                completionStatus.color = goodColour;
            }
            SetLineColour(seamGuide, Color.Lerp(new Color(normalColour.r, normalColour.g, normalColour.b, 0.15f), normalColour, strength));
            SetLineColour(completedGuide, Has(state, FeedbackCue.Complete) ? goodColour : normalColour);
        }

        public void Clear()
        {
            SetActive(seamGuide, false); SetActive(completedGuide, false); SetActive(positionGuide, false);
            SetActive(startMarker, false); SetActive(endMarker, false); SetActive(travelAngleGuide, false);
            SetActive(workAngleGuide, false); SetActive(speedIndicator, false);
            if (mandatoryStatus) mandatoryStatus.gameObject.SetActive(false);
            if (completionStatus) completionStatus.gameObject.SetActive(false);
        }

        void UpdateProgress(double progress)
        {
            if (!completedGuide || seamPoints.Length < 2) return;
            float p = Mathf.Clamp01((float)progress);
            if (p <= 0f) { ConfigureLine(completedGuide, Array.Empty<Vector3>()); return; }
            float scaled = p * (seamPoints.Length - 1);
            int full = Mathf.Min(Mathf.FloorToInt(scaled), seamPoints.Length - 1);
            int count = full >= seamPoints.Length - 1 ? seamPoints.Length : full + 2;
            var output = new Vector3[count];
            for (int i = 0; i <= full && i < seamPoints.Length; i++) output[i] = seamPoints[i];
            if (full < seamPoints.Length - 1) output[count - 1] = Vector3.Lerp(seamPoints[full], seamPoints[full + 1], scaled - full);
            ConfigureLine(completedGuide, output);
            SetActive(completedGuide, true);
        }

        static bool Has(FeedbackState state, FeedbackCue cue) => (state.ActiveCues & cue) != 0;
        static bool HasAny(FeedbackState state, FeedbackCue cues) => (state.ActiveCues & cues) != 0;
        static void ConfigureLine(LineRenderer line, Vector3[] points) { if (!line) return; line.useWorldSpace = false; line.positionCount = points.Length; if (points.Length > 0) line.SetPositions(points); line.gameObject.SetActive(points.Length > 0); }
        static void SetLineColour(LineRenderer line, Color colour) { if (!line) return; line.startColor = colour; line.endColor = colour; }
        static void SetColour(Renderer renderer, Color colour) { if (renderer && renderer.material) renderer.material.color = colour; }
        static void SetActive(Component component, bool value) { if (component) component.gameObject.SetActive(value); }
        static void SetActive(GameObject item, bool value) { if (item) item.SetActive(value); }
        static string Label(FeedbackCue cue)
        {
            switch (cue)
            {
                case FeedbackCue.EmergencyStop: return "EMERGENCY STOP";
                case FeedbackCue.ReflectionRisk: return "REFLECTION RISK";
                case FeedbackCue.TrackingLost: return "TRACKING LOST";
                case FeedbackCue.RegistrationLost: return "REGISTRATION LOST";
                case FeedbackCue.NozzleMismatch: return "WRONG NOZZLE";
                case FeedbackCue.ClampDisconnected: return "CLAMP DISCONNECTED";
                case FeedbackCue.ContactInvalid: return "CONTACT INVALID";
                default: return "ACTIVATION BLOCKED";
            }
        }
    }
}
