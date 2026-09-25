using System;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using WeldingTrainer.Application;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Registration;
using WeldingTrainer.Registration.Meta;

namespace WeldingTrainer.Integration
{
    /// <summary>
    /// #58 join-layer adapter.
    ///
    /// It owns one real Meta registration runtime and exposes two views of the same
    /// authoritative RegistrationSnapshot:
    /// 1) IRegistrationPort for TrainingCoordinator validity/generation/fixture checks;
    /// 2) the registered WorldFromFixture / WorldFromWorkpiece poses for spatial
    ///    presentation and Workpiece-local evaluation mapping.
    ///
    /// No QR pose is re-estimated here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestRegistrationBridge : MonoBehaviour, IRegistrationPort
    {
        [Header("Registration dependencies")]
        public SpatialCatalogAsset catalog;
        public QrAdapterQualification adapterQualification;
        public OVRCameraRig rig;
        public MRUK mruk;

        [Header("Lifecycle")]
        [Tooltip("Useful for a demonstration scene. In final production, normally begin when the TrainingCoordinator enters Registering.")]
        public bool beginOnStart;

        [Header("Diagnostics")]
        public bool diagnosticQrLogging;

        private MetaRegistrationRuntime runtime;
        private string startupError;

        public RegistrationSnapshot Snapshot => runtime?.Snapshot;
        public TrackerFrame LastFrame => runtime?.LastFrame;
        public string RuntimeTuple => runtime?.RuntimeTuple;
        public string StartupError => startupError;
        public bool Initialized => runtime != null;

        private void Start()
        {
            EnsureRuntime();

            if (beginOnStart && runtime != null)
                runtime.BeginRegistration();
        }

        public RegistrationSnapshot Refresh()
        {
            if (!EnsureRuntime())
                return null;

            return runtime.Read();
        }

        public void BeginRegistration()
        {
            if (EnsureRuntime())
                runtime.BeginRegistration();
        }

        public void ConfirmAssembly(
            bool correctPart,
            bool secured,
            bool plausibleOverlay)
        {
            if (EnsureRuntime())
            {
                runtime.ConfirmAssembly(
                    correctPart,
                    secured,
                    plausibleOverlay);
            }
        }

        public void CancelRegistration()
        {
            if (runtime != null)
                runtime.CancelRegistration();
        }

        public void InvalidateAssembly()
        {
            if (runtime != null)
                runtime.InvalidateAssembly();
        }

        public void InvalidateTracking()
        {
            if (runtime != null)
                runtime.InvalidateTracking();
        }

        /// <summary>
        /// Adapter consumed by TrainingCoordinator.
        ///
        /// 'Available' means the real port exists. 'Valid' means the freshly-read
        /// registration is currently Registered and fresh. Origin equality with the
        /// tool/head input is intentionally checked by TrainingCoordinator itself.
        /// </summary>
        public RegistrationInput Capture(double now)
        {
            var snapshot = Refresh();
            if (snapshot == null)
                return new RegistrationInput(false, false, 0, 0, null);

            bool valid = snapshot.IsUsableAt(
                now,
                snapshot.OriginGeneration);

            return new RegistrationInput(
                available: true,
                valid: valid,
                generation: snapshot.Generation,
                origin: snapshot.OriginGeneration,
                fixture: snapshot.Binding?.FixtureId);
        }

        /// <summary>
        /// Returns the same fresh registered spatial frame that backs IRegistrationPort.
        /// Consumers should transform tool/head samples into Workpiece coordinates from
        /// this value; they should not read QR transforms directly.
        /// </summary>
        public bool TryGetRegisteredSpatialFrame(
            double now,
            out RigidPose worldFromFixture,
            out RigidPose worldFromWorkpiece,
            out AssemblyBindingSnapshot binding,
            out long registrationGeneration,
            out long originGeneration)
        {
            worldFromFixture = default;
            worldFromWorkpiece = default;
            binding = null;
            registrationGeneration = 0;
            originGeneration = 0;

            var snapshot = Refresh();
            if (snapshot == null ||
                !snapshot.IsUsableAt(now, snapshot.OriginGeneration) ||
                !snapshot.WorldFromFixture.HasValue ||
                !snapshot.WorldFromWorkpiece.HasValue ||
                snapshot.Binding == null)
            {
                return false;
            }

            worldFromFixture = snapshot.WorldFromFixture.Value;
            worldFromWorkpiece = snapshot.WorldFromWorkpiece.Value;
            binding = snapshot.Binding;
            registrationGeneration = snapshot.Generation;
            originGeneration = snapshot.OriginGeneration;
            return true;
        }

        /// <summary>
        /// TrainingCoordinator calls this at teardown. Releasing the session registration
        /// also releases the unsaved anchor; the runtime object itself remains reusable
        /// until this component is disabled.
        /// </summary>
        public void Teardown()
        {
            if (runtime != null)
                runtime.CancelRegistration();
        }

        private bool EnsureRuntime()
        {
            if (runtime != null)
                return true;

            if (!string.IsNullOrEmpty(startupError))
                return false;

            try
            {
                if (!catalog)
                    throw new InvalidOperationException(
                        "QuestRegistrationBridge requires a SpatialCatalogAsset.");

                if (!adapterQualification)
                    throw new InvalidOperationException(
                        "QuestRegistrationBridge requires a QrAdapterQualification asset.");

                if (!rig)
                    throw new InvalidOperationException(
                        "QuestRegistrationBridge requires OVRCameraRig.");

                if (!mruk)
                    throw new InvalidOperationException(
                        "QuestRegistrationBridge requires MRUK.");

                var content = catalog.Freeze();

                runtime = new MetaRegistrationRuntime(
                    content,
                    mruk,
                    rig,
                    adapterQualification,
                    diagnosticQrLogging
                        ? (Action<string>)(message => Debug.Log(message, this))
                        : null);

                Debug.Log(
                    "Production registration device tuple: " +
                    runtime.RuntimeTuple,
                    this);

                return true;
            }
            catch (Exception error)
            {
                startupError = error.Message;
                Debug.LogException(error, this);
                return false;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && runtime != null)
                runtime.InvalidateTracking();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && runtime != null)
                runtime.InvalidateTracking();
        }

        private void OnDisable()
        {
            runtime?.Dispose();
            runtime = null;
        }
    }
}
