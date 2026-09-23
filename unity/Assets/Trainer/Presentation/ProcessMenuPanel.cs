using System.Text;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    /// <summary>Neutral world-space menu view. Input and commands remain adapter/application responsibilities.</summary>
    public sealed class ProcessMenuPanel : MonoBehaviour
    {
        [SerializeField] TextMesh heading;
        [SerializeField] TextMesh body;
        [SerializeField] TextMesh safetyStatus;
        [SerializeField] TextMesh response;
        [SerializeField] Renderer readableBackground;
        [SerializeField, Range(0f, 1f)] float backgroundOpacity = .72f;

        public void Render(ProcessMenuController controller)
        {
            if (controller == null || !controller.IsOpen) { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            ProcessMenuSnapshot view = controller.View;
            if (heading) heading.text = controller.Interactive ? "PROCESS SETUP" : "PAUSING PROCESS…";
            if (body) body.text = BuildBody(view, controller.FocusIndex);
            if (safetyStatus)
            {
                safetyStatus.text = BuildSafety(view);
                safetyStatus.color = view.Process.Reasons == BlockReason.None ? new Color(.15f, 1f, .3f) : new Color(1f, .25f, .12f);
            }
            if (response) response.text = controller.LastExplanation;
            if (readableBackground && readableBackground.material)
            {
                Color colour = readableBackground.material.color; colour.a = backgroundOpacity; readableBackground.material.color = colour;
            }
        }

        static string BuildBody(ProcessMenuSnapshot v, int focus)
        {
            if (v == null) return "Waiting for authoritative state";
            var text = new StringBuilder();
            text.AppendLine("Fixture: " + Empty(v.FixtureId));
            text.AppendLine("Seam: " + Empty(v.SeamId));
            text.AppendLine("Mode: " + v.Mode);
            text.AppendLine("Profile: " + Empty(v.ProfileId));
            foreach (var parameter in v.Parameters) text.AppendLine(parameter.Key + ": " + parameter.Value.ToString("0.###"));
            text.AppendLine("Clamp: " + v.Process.Clamp);
            text.AppendLine("Nozzle: " + v.Process.Nozzle + " / " + v.NozzleChange);
            text.AppendLine("Assistance: " + v.AssistancePercent.ToString("0") + "%");
            text.AppendLine(v.Saving ? "Saving…" : "Result: " + Empty(v.ResultStatus));
            for (int i = 0; i < v.Items.Count; i++)
            {
                ProcessMenuItem item = v.Items[i];
                text.Append(i == focus ? "> " : "  ").Append(item.Label);
                if (item.Step > 0) text.Append(": ").Append(item.Current.ToString("0.###")).Append(" ").Append(item.Unit);
                if (!item.Enabled) text.Append(" [Unavailable: ").Append(item.UnavailableReason).Append("]");
                text.AppendLine();
            }
            text.AppendLine("Focus " + (focus + 1) + " • A submit • stick navigate/close • B E-STOP");
            return text.ToString();
        }

        static string BuildSafety(ProcessMenuSnapshot v)
        {
            if (v == null) return "STATUS UNKNOWN";
            if (!v.RegistrationValid) return "REGISTRATION LOST — RE-REGISTER";
            if (v.Process.Reasons == BlockReason.None) return "DISARMED / READY FOR EXPLICIT ARM";
            var text = new StringBuilder("INHIBITED: ");
            if (v.InterlockReasons.Count == 0) text.Append(v.Process.Reasons);
            else for (int i = 0; i < v.InterlockReasons.Count; i++) { if (i > 0) text.Append("\n"); text.Append(v.InterlockReasons[i]); }
            return text.ToString();
        }

        static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
    }
}
