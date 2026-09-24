using UnityEngine;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration.Meta
{
    // OVRSpatialAnchor updates its tracking flags/pose in Update and LateUpdate (default order).
    // Capture AFTER that update, even when the stationary pose is numerically identical.
    [DefaultExecutionOrder(9000)]
    public sealed class SessionAnchorPoll : MonoBehaviour
    {
        private OVRSpatialAnchor anchor;
        private IMonotonicClock clock;
        private long generation, origin;
        private bool localized;
        public AnchorFrame Latest { get; private set; }

        public void Initialize(OVRSpatialAnchor value, IMonotonicClock time, long registrationGeneration, long originGeneration)
        {
            anchor = value;
            clock = time;
            generation = registrationGeneration;
            origin = originGeneration;
            Latest = new AnchorFrame(AnchorStatus.Creating, null, generation, origin, clock.Now);
        }

        private void LateUpdate()
        {
            if (clock == null)
                return;
            AnchorStatus status;
            RigidPose? pose = null;
            if (!anchor)
                status = AnchorStatus.Failed;
            else if (!anchor.isActiveAndEnabled)
                status = AnchorStatus.Lost;
            else if (!anchor.Created)
                status = AnchorStatus.Creating;
            else if (!anchor.Localized || !anchor.IsTracked)
                status = localized ? AnchorStatus.Lost : AnchorStatus.Localizing;
            else
            {
                status = AnchorStatus.Localized;
                try
                {
                    pose = UnityRegistrationPose.Read(transform, "Anchor");
                    localized = true;
                }
                catch
                {
                    status = AnchorStatus.Failed;
                }
            }

            Latest = new AnchorFrame(status, pose, generation, origin, clock.Now);
        }
    }
}
