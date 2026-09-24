using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Registration.Meta;

namespace WeldingTrainer.Registration.Preview
{
    // Standalone diagnostic composition only. #58 supplies production input/interlocks and attempt lifecycle.
    [DefaultExecutionOrder(10000)]
    public sealed class RegistrationPreview : MonoBehaviour
    {
        public SpatialCatalogAsset catalog;
        public QrAdapterQualification adapterQualification;
        public OVRCameraRig rig;
        public MRUK mruk;
        public Material ghostMaterial;
        public TextMesh statusText;
        public bool rightControllerSmokeCommands = true;
        [Tooltip("Logs real MRUK events, tracking transitions and witnessed SDK updates. Does not alter registration.")]
        public bool diagnosticQrLogging;

        private MetaRegistrationRuntime runtime;
        private GameObject fixtureGhost, partGhost;
        private GameObject rawAxes;
        private readonly List<Material> axisMaterials = new List<Material>();
        private readonly List<Mesh> presentationMeshes = new List<Mesh>();
        private string shownBinding;
        private string startupError;
        private readonly Dictionary<string, Mesh> fixtures = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Mesh> parts = new Dictionary<string, Mesh>();

        public RegistrationSnapshot Snapshot => runtime?.Snapshot;

        private void Start()
        {
            try
            {
                var content = catalog.Freeze();
                var data = new UnitySpatialJson().Read<WeldingTrainer.Content.Spatial.CatalogData>(
                    content.CatalogJson);
                var meshes = new Dictionary<string, Mesh>();

                foreach (var mesh in catalog.CopyVisualMeshes())
                {
                    var converted = UnityRegistrationMesh.Create(mesh);
                    presentationMeshes.Add(converted);
                    meshes.Add(mesh.name, converted);
                }

                foreach (var item in data.fixtures)
                    fixtures.Add(item.id, meshes[item.sourceId]);

                foreach (var item in data.workpieces)
                    parts.Add(item.id, meshes[item.sourceId]);

                runtime = new MetaRegistrationRuntime(
                    content,
                    mruk,
                    rig,
                    adapterQualification,
                    diagnosticQrLogging
                        ? (Action<string>)(message => Debug.Log(message))
                        : null,
                    new PreviewLog());

                Debug.Log("Registration device tuple: " + runtime.RuntimeTuple);
                runtime.BeginRegistration();
            }
            catch (Exception error)
            {
                startupError = error.Message;
                Debug.LogException(error);
            }
        }

        public void BeginRegistration() => runtime?.BeginRegistration();

        public void ConfirmAssembly(
            bool correctPart,
            bool secured,
            bool plausibleOverlay) =>
            runtime?.ConfirmAssembly(correctPart, secured, plausibleOverlay);

        public void CancelRegistration() => runtime?.CancelRegistration();

        public void InvalidateAssembly() => runtime?.InvalidateAssembly();

        private void LateUpdate()
        {
            if (runtime == null)
            {
                if (statusText)
                    statusText.text = "Registration unavailable\n" + startupError;
                return;
            }

            var snapshot = runtime.Read();

            if (rightControllerSmokeCommands)
            {
                if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
                {
                    CancelRegistration();
                }
                else if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
                {
                    if (snapshot.State == RegistrationState.Preview)
                    {
                        ConfirmAssembly(true, true, true);
                    }
                    else if (snapshot.State == RegistrationState.Unregistered ||
                             snapshot.State == RegistrationState.Lost)
                    {
                        BeginRegistration();
                    }
                }
            }

            snapshot = runtime.Snapshot;
            DrawGhost(snapshot);
            DrawRawAxes();

            if (statusText)
            {
                string selected = snapshot.Binding == null
                    ? "No selected assembly"
                    : $"{snapshot.Binding.PartId} r{snapshot.Binding.PartRevision} / " +
                      $"{snapshot.Binding.FixtureId} r{snapshot.Binding.FixtureRevision}\n" +
                      $"Mount r{snapshot.Binding.MountRevision} / binding r{snapshot.Binding.Revision}";

                statusText.text =
                    $"QR REGISTRATION — STANDALONE A\n{snapshot.State}: {snapshot.Reason}\n{selected}\n" +
                    $"Generation {snapshot.Generation} / origin {snapshot.OriginGeneration}\n" +
                    $"Observations {snapshot.ObservationCount}, span {snapshot.ObservationSpanSeconds:F1}s\n" +
                    $"Scatter {Format(snapshot.TranslationScatterMetres, 1000)} mm / " +
                    $"{Format(snapshot.AngularScatterRadians, 180 / Math.PI)} deg\n" +
                    $"Physical qualification: {(snapshot.PhysicallyQualified ? "recorded" : "UNQUALIFIED")} / " +
                    $"scoring: {(snapshot.EligibleForScoring ? "content ready" : "blocked")}\n" +
                    "A: start/retry; B: cancel and release anchor\n" +
                    "At Preview, A declares ALL THREE:\ncorrect part mounted; bolts secured; overlay plausible.\n" +
                    "Do not move/rebolt the assembly. B cancels before changes.\n" +
                    "Unknown print/frame evidence blocks registration; no override.";

                if (snapshot.Reason == RegistrationReason.AwaitingTrackedQr)
                {
                    statusText.text +=
                        "\nQR temporarily unavailable: candidate retained; CONFIRM BLOCKED.";
                }

                var raw = runtime.LastFrame;
                if (raw != null)
                {
                    foreach (var tracked in raw.Trackables)
                    {
                        var observation = tracked.LastObservation;
                        statusText.text +=
                            $"\nMRUK object {tracked.TrackableId}: " +
                            $"{(tracked.IsTracked ? "Tracked" : "Untracked")}" +
                            (tracked.AwaitingTrackedUpdate
                                ? " / awaiting tracked update"
                                : string.Empty);

                        if (observation != null)
                        {
                            statusText.text +=
                                $"\nLast SDK plane (history): " +
                                $"{observation.WidthMetres * 1000:F1} x " +
                                $"{observation.HeightMetres * 1000:F1} mm " +
                                $"seq {observation.Sequence}; " +
                                $"age {Time.realtimeSinceStartupAsDouble - observation.ReceivedAt:F1}s";
                        }
                    }
                }
            }
        }

        private static string Format(double? value, double scale) =>
            value.HasValue ? (value.Value * scale).ToString("F2") : "unavailable";

        private void DrawGhost(RegistrationSnapshot snapshot)
        {
            bool visible =
                snapshot.WorldFromFixture.HasValue &&
                snapshot.WorldFromWorkpiece.HasValue;

            if (visible && snapshot.Binding.Id != shownBinding)
            {
                if (fixtureGhost)
                    Destroy(fixtureGhost);
                if (partGhost)
                    Destroy(partGhost);

                fixtureGhost = Ghost(
                    fixtures[snapshot.Binding.FixtureId],
                    "Fixture ghost");

                partGhost = Ghost(
                    parts[snapshot.Binding.PartId],
                    "Workpiece ghost");

                shownBinding = snapshot.Binding.Id;
            }

            if (fixtureGhost)
            {
                fixtureGhost.SetActive(visible);
                if (visible)
                    UnityRegistrationPose.Write(
                        fixtureGhost.transform,
                        snapshot.WorldFromFixture.Value);
            }

            if (partGhost)
            {
                partGhost.SetActive(visible);
                if (visible)
                    UnityRegistrationPose.Write(
                        partGhost.transform,
                        snapshot.WorldFromWorkpiece.Value);
            }
        }

        private GameObject Ghost(Mesh mesh, string label)
        {
            var go = new GameObject(label);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ghostMaterial;
            return go;
        }

        private void DrawRawAxes()
        {
            var frame = runtime?.LastFrame;
            var observation =
                frame != null && frame.Observations.Count == 1
                    ? frame.Observations[0]
                    : null;

            bool visible =
                observation != null &&
                observation.WorldFromMarker.HasValue &&
                Time.realtimeSinceStartupAsDouble - observation.ReceivedAt <= 1.5;

            if (visible && !rawAxes)
            {
                rawAxes = new GameObject(
                    "Diagnostic MRUK axes — not registration");

                var colors = new[]
                {
                    Color.red,
                    Color.green,
                    Color.blue
                };

                var axes = new[]
                {
                    Vector3.right,
                    Vector3.up,
                    Vector3.back
                };

                for (int i = 0; i < 3; i++)
                {
                    var line = new GameObject("XYZ"[i].ToString())
                        .AddComponent<LineRenderer>();

                    line.transform.SetParent(rawAxes.transform, false);

                    var material = new Material(ghostMaterial);
                    material.color = colors[i];
                    axisMaterials.Add(material);

                    line.sharedMaterial = material;
                    line.useWorldSpace = false;
                    line.startWidth = line.endWidth = .001f;
                    line.positionCount = 2;
                    line.SetPosition(0, Vector3.zero);
                    line.SetPosition(1, axes[i] * .035f);
                }
            }

            if (rawAxes)
            {
                rawAxes.SetActive(visible);
                if (visible)
                    UnityRegistrationPose.Write(
                        rawAxes.transform,
                        observation.WorldFromMarker.Value);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                runtime?.InvalidateTracking();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                runtime?.InvalidateTracking();
        }

        private void OnDisable()
        {
            runtime?.Dispose();
            runtime = null;

            if (fixtureGhost)
                Destroy(fixtureGhost);
            if (partGhost)
                Destroy(partGhost);
            if (rawAxes)
                Destroy(rawAxes);

            foreach (var material in axisMaterials)
                Destroy(material);
            axisMaterials.Clear();

            foreach (var mesh in presentationMeshes)
                Destroy(mesh);
            presentationMeshes.Clear();
        }

        private sealed class PreviewLog : IRegistrationEvidenceSink
        {
            private RegistrationState? state;
            private RegistrationReason? reason;

            public void Record(RegistrationSnapshot snapshot)
            {
                if (state == snapshot.State && reason == snapshot.Reason)
                    return;

                state = snapshot.State;
                reason = snapshot.Reason;

                Debug.Log(
                    $"Registration evidence: t={snapshot.CapturedAt:F3} " +
                    $"generation={snapshot.Generation} " +
                    $"origin={snapshot.OriginGeneration} " +
                    $"state={snapshot.State} " +
                    $"reason={snapshot.Reason} " +
                    $"binding={snapshot.Binding?.Id} " +
                    $"hash={snapshot.Binding?.ContentHash} " +
                    $"observations={snapshot.ObservationCount} " +
                    $"confirmed={snapshot.AssemblyConfirmed} " +
                    $"anchor={snapshot.Anchor} " +
                    $"physical={snapshot.PhysicallyQualified}");
            }
        }
    }
}
