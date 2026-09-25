using System;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    public sealed class FusionTrainingView : MonoBehaviour, IVisualFeedbackSink, IAudioFeedbackSink
    {
        public Material metal, sparkMaterial, overlay;
        public Transform Head { get; set; }
        public Transform Workpiece { get; private set; }
        public string Status { get; set; }
        public string ActionHint { get; set; }
        public bool Rehearsal { get; set; }

        TextMesh title, status, cue, values, footer;
        Transform panel, progress;
        LineRenderer target, errorLine, toolLine;
        FusionBeadView bead;
        FusionSparks sparks;
        AudioSource audioSource;
        AudioClip good, warning;
        FeedbackState feedback;
        FusionAttemptSummary summary;
        DirectedSpline seam;
        Vec3 normal;
        public void Initialize()
        {
            Workpiece = new GameObject("Registered weld overlays").transform;
            Workpiece.SetParent(transform, false);
            panel = new GameObject("Fusion training panel").transform;
            panel.SetParent(transform, false);
            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Readable HUD backing";
            background.transform.SetParent(panel, false);
            background.transform.localScale = new Vector3(.64f, .42f, 1);
            background.transform.localPosition = new Vector3(0, 0, .008f);
            Destroy(background.GetComponent<Collider>());
            var bg = new Material(overlay);
            bg.color = new Color(.014f, .025f, .041f, 1);
            background.GetComponent<Renderer>().material = bg;
            title = Text("FUSION  /  TRAINING", new Vector3(-.29f, .178f, 0), .0045f, new Color(.35f, .85f, 1));
            status = Text("Register the workpiece", new Vector3(-.29f, .136f, 0), .0038f, Color.white);
            cue = Text("", new Vector3(-.29f, .090f, 0), .0042f, Color.white);
            values = Text("", new Vector3(-.29f, .040f, 0), .0035f, new Color(.8f, .88f, .94f));
            footer = Text("", new Vector3(-.29f, -.157f, 0), .0026f, new Color(.55f, .68f, .78f));
            progress = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            progress.SetParent(panel, false);
            progress.localPosition = new Vector3(-.29f, -.137f, -.001f);
            Destroy(progress.GetComponent<Collider>());
            progress.GetComponent<Renderer>().material = new Material(overlay)
            {
                color = new Color(.15f, .8f, .63f)
            };
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            good = Tone(720);
            warning = Tone(320);
            toolLine = Line("Calibrated nozzle", transform, .003f, new Color(.65f, .83f, .9f));
            toolLine.useWorldSpace = true;
        }

        TextMesh Text(string text, Vector3 position, float size, Color color)
        {
            var t = new GameObject("HUD text").AddComponent<TextMesh>();
            t.transform.SetParent(panel, false);
            t.transform.localPosition = position;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.GetComponent<MeshRenderer>().sharedMaterial = t.font.material;
            t.text = text;
            t.fontSize = 64;
            t.characterSize = size;
            t.anchor = TextAnchor.UpperLeft;
            t.color = color;
            t.richText = true;
            return t;
        }

        LineRenderer Line(string label, Transform parent, float width, Color color)
        {
            var line = new GameObject(label).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.sharedMaterial = overlay;
            line.startColor = line.endColor = color;
            line.numCapVertices = 4;
            return line;
        }

        public void Configure(DirectedSpline value, Vec3 outward)
        {
            foreach (Transform child in Workpiece)
                Destroy(child.gameObject);
            seam = value;
            normal = outward;
            summary = null;
            target = Line("Authored seam", Workpiece, .0013f, new Color(.2f, .7f, 1));
            target.positionCount = seam.Points.Count;
            for (int i = 0; i < seam.Points.Count; i++)
                target.SetPosition(i, FusionBeadView.ToUnity(seam.Points[i] + normal * .0008));
            errorLine = Line("Tip to seam", Workpiece, .0008f, new Color(1, .5f, .1f));
            for (int i = 0; i < 2; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = i == 0 ? "START" : "END";
                marker.transform.SetParent(Workpiece, false);
                marker.transform.localPosition = FusionBeadView.ToUnity(i == 0 ? seam.Points[0] : seam.Points[seam.Points.Count - 1]);
                marker.transform.localScale = Vector3.one * .005f;
                Destroy(marker.GetComponent<Collider>());
                marker.GetComponent<Renderer>().material = new Material(overlay)
                {
                    color = i == 0 ? new Color(.2f, 1, .65f) : new Color(.3f, .65f, 1)
                };
            }

            var b = new GameObject("Developing weld");
            b.transform.SetParent(Workpiece, false);
            bead = b.AddComponent<FusionBeadView>();
            bead.metal = metal;
            bead.Configure(seam, normal);
            var s = new GameObject("Active Fusion pool");
            s.SetActive(false);
            s.transform.SetParent(Workpiece, false);
            sparks = s.AddComponent<FusionSparks>();
            sparks.sparkMaterial = sparkMaterial;
            sparks.sparksPerSecond = 70;
            s.SetActive(true);
        }

        public void SetTool(Vector3 worldTip, Vector3 worldBody, bool valid)
        {
            toolLine.enabled = valid;
            if (!valid)
                return;
            toolLine.positionCount = 2;
            toolLine.SetPosition(0, worldTip);
            toolLine.SetPosition(1, worldBody);
        }

        public void Render(PathMetrics path, FusionResult result, bool spatialValid)
        {
            if (bead && result != null)
                bead.Apply(result.Coverage);
            if (target)
                target.enabled = spatialValid && feedback.VisualStrength > 0;
            if (errorLine)
            {
                errorLine.enabled = spatialValid && path != null && path.Valid && feedback.VisualStrength > 0;
                if (errorLine.enabled)
                {
                    errorLine.positionCount = 2;
                    errorLine.SetPosition(0, FusionBeadView.ToUnity(path.Projection.PositionWorkpieceMetres));
                    errorLine.SetPosition(1, FusionBeadView.ToUnity(path.Projection.PositionWorkpieceMetres + path.ErrorVectorWorkpieceMetres));
                }
            }

            if (sparks)
            {
                bool on = spatialValid && result != null && result.OutputOn;
                if (on)
                    sparks.SetWelding(true, Workpiece.TransformPoint(FusionBeadView.ToUnity(path.Projection.PositionWorkpieceMetres + normal * .002)), Workpiece.TransformDirection(FusionBeadView.ToUnity(normal)));
                else
                    sparks.Clear();
            }

            if (summary != null)
                return;
            float fraction = (float)(result?.Coverage.AttemptedFraction ?? 0);
            Bar(fraction);
            values.text = path != null && path.Valid ? $"COVERED  {fraction * 100:F0}%     GOOD  {(result?.Coverage.AcceptableFraction ?? 0) * 100:F0}%\n\nPATH  {path.TotalErrorMetres * 1000:F1} mm     SPEED  {(path.SpeedValid ? (path.FilteredSpeedMps * 1000).ToString("F0") : "--")} mm/s\nTRAVEL  {(path.TravelAngleValid ? (Math.Abs(path.TravelAngleRadians) * 180 / Math.PI).ToString("F0") : "--")} deg     WORK  {(path.WorkAngleValid ? (path.WorkAngleRadians * 180 / Math.PI).ToString("F0") : "--")} deg\nTARGET  15-35 mm/s  /  axes <= 4 mm" : "Awaiting valid registered tool measurements\n\nFollow the blue seam from green START\nB = emergency stop at any time";
        }

        void Bar(float fraction)
        {
            progress.localScale = new Vector3(Mathf.Max(.001f, .58f * fraction), .004f, 1);
            progress.localPosition = new Vector3(-.29f + .29f * fraction, -.137f, -.001f);
        }

        public void ShowResults(FusionAttemptSummary value, bool saved)
        {
            summary = value;
            Stop();
            sparks?.Clear();
            Bar((float)(value.attemptedMetres / Math.Max(value.seamMetres, 1e-9)));
            title.text = "FUSION  /  ATTEMPT RESULTS";
            status.text = value.interrupted ? "Attempt interrupted" : value.completed ? "Pass completed" : "Finished - review gaps and quality";
            cue.text = saved ? "Saved on this device" : "RECORDING INCOMPLETE";
            cue.color = saved ? new Color(.3f, 1, .7f) : Color.red;
            values.text = $"COVERED  {value.attemptedMetres * 1000:F0} / {value.seamMetres * 1000:F0} mm     GOOD  {value.acceptableMetres * 1000:F0} mm\nMISSED  {value.missedMetres * 1000:F0} mm     ACTIVE  {value.activeSeconds:F1} s\nPATH mean / max  {Number(value.meanPositionMm, value.positionSampleSeconds)} / {Number(value.maximumPositionMm, value.positionSampleSeconds)} mm\nSPEED in range  {Number(value.speedInRangePercent, value.speedSampleSeconds)}%\nTRAVEL / WORK  {Number(value.meanTravelDegrees, value.travelSampleSeconds)} / {Number(value.meanWorkDegrees, value.workSampleSeconds)} deg\nBLOCKED  {value.blockedTriggerSeconds:F1} s    INVALID  {value.invalidSeconds:F1} s";
        }

        static string Number(double number, double seconds) => seconds > 0 ? number.ToString("F1") : "--";
        public void Apply(FeedbackState state)
        {
            feedback = state;
        }

        public void Clear()
        {
            Stop();
            sparks?.Clear();
        }

        public void Play(FeedbackCue c, double strength)
        {
            audioSource.clip = c == FeedbackCue.CorrectSpeed ? good : warning;
            audioSource.volume = (float)strength * .16f;
            audioSource.Play();
        }

        public void Stop()
        {
            if (audioSource)
                audioSource.Stop();
        }

        void LateUpdate()
        {
            if (!panel)
                return;
            if (Head)
            {
                Vector3 position = Head.TransformPoint(new Vector3(.48f, .08f, .95f));
                panel.position = Vector3.Lerp(panel.position, position, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 8));
                panel.rotation = Quaternion.LookRotation(panel.position - Head.position, Vector3.up);
            }

            footer.text = (Rehearsal ? "EDITOR REHEARSAL  |  " : "") + (ActionHint ?? "") + "\nTraining visualization - not predicted metallurgy";
            if (summary != null)
                return;
            title.text = "FUSION  /  TRAINING";
            status.text = Status;
            cue.text = feedback.VisualStrength > 0 ? CueText(feedback.PrimaryCue) :
                summary == null && Status != null && Status.StartsWith("QR ", StringComparison.Ordinal)
                    ? "Register the secured workpiece to begin"
                    : "Coaching off";
            cue.color = feedback.Mandatory ? new Color(1, .55f, .2f) : new Color(.3f, 1, .75f);
        }

        public static string CueText(FeedbackCue cue) => cue switch
        {
            FeedbackCue.EmergencyStop => "STOP - release trigger, then acknowledge",
            FeedbackCue.ReflectionRisk => "Reflected direction unsafe / unavailable",
            FeedbackCue.TrackingLost => "Tool or head tracking unavailable",
            FeedbackCue.RegistrationLost => "Register the secured workpiece",
            FeedbackCue.NozzleMismatch => "Select the welding nozzle",
            FeedbackCue.ClampDisconnected => "Connect the virtual clamp",
            FeedbackCue.ContactInvalid => "Move tip onto the finite weld surface",
            FeedbackCue.ActivationBlocked => "Output inhibited - check status above",
            FeedbackCue.OffPath => "Move closer to the blue seam",
            FeedbackCue.TooFast => "Slow down",
            FeedbackCue.TooSlow => "Keep moving along the seam",
            FeedbackCue.TravelAngleError => "Move in the seam direction",
            FeedbackCue.WorkAngleError => "Correct the tool angle",
            FeedbackCue.ReverseMotion => "Follow START to END",
            FeedbackCue.CorrectSpeed => "Good pace - keep it steady",
            FeedbackCue.Complete => "Full pass achieved - release trigger",
            _ => "Follow the blue seam"
        };
        static AudioClip Tone(float frequency)
        {
            var clip = AudioClip.Create("Fusion cue", 4800, 1, 48000, false);
            var samples = new float[4800];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / 48000) * Mathf.Sin(Mathf.PI * i / samples.Length) * .4f;
            clip.SetData(samples, 0);
            return clip;
        }

        void OnDisable() => Clear();
        void OnDestroy()
        {
            if (good)
                Destroy(good);
            if (warning)
                Destroy(warning);
        }
    }
}
