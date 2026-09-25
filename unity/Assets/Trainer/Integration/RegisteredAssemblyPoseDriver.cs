using UnityEngine;
using WeldingTrainer.Registration;
using WeldingTrainer.Registration.Meta;

namespace WeldingTrainer.Integration
{
    /// <summary>
    /// Presentation-only pose driver for the registered fixture/workpiece roots.
    ///
    /// Do not use these Unity Transforms as scoring truth. Evaluation should consume
    /// RegistrationSnapshot / WorldFromWorkpiece and transform the tool mathematically
    /// into Workpiece coordinates.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(12000)]
    public sealed class RegisteredAssemblyPoseDriver : MonoBehaviour
    {
        public QuestRegistrationBridge registration;
        public Transform fixtureRoot;
        public Transform workpieceRoot;

        [Tooltip("If true, show the candidate pose during Preview/Anchoring as well as Registered.")]
        public bool showPreviewPose = true;

        [Tooltip("Disable configured roots when no usable/candidate pose exists.")]
        public bool hideWhenUnavailable = true;

        private void LateUpdate()
        {
            if (!registration)
                return;

            // Do not poll registration here. The current demo driver (and later
            // TrainingCoordinator through IRegistrationPort.Capture) owns polling.
            RegistrationSnapshot snapshot = registration.Snapshot;

            bool hasPose =
                snapshot != null &&
                snapshot.WorldFromFixture.HasValue &&
                snapshot.WorldFromWorkpiece.HasValue;

            bool visible =
                hasPose &&
                (showPreviewPose || snapshot.IsValid);

            if (fixtureRoot)
            {
                if (hideWhenUnavailable)
                    fixtureRoot.gameObject.SetActive(visible);

                if (visible)
                {
                    UnityRegistrationPose.Write(
                        fixtureRoot,
                        snapshot.WorldFromFixture.Value);
                }
            }

            if (workpieceRoot)
            {
                if (hideWhenUnavailable)
                    workpieceRoot.gameObject.SetActive(visible);

                if (visible)
                {
                    UnityRegistrationPose.Write(
                        workpieceRoot,
                        snapshot.WorldFromWorkpiece.Value);
                }
            }
        }
    }
}
