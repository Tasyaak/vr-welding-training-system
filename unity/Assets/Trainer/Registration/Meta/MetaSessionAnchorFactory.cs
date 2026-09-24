using UnityEngine;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration.Meta
{
    public sealed class MetaSessionAnchorFactory : ISessionAnchorFactory
    {
        private readonly IMonotonicClock clock;
        public MetaSessionAnchorFactory(IMonotonicClock clock)
        {
            this.clock = clock;
        }

        public ISessionAnchor Create(RigidPose worldFromAnchor, long generation, long origin) => new Operation(worldFromAnchor, generation, origin, clock);
        private sealed class Operation : ISessionAnchor
        {
            private GameObject root;
            private readonly OVRSpatialAnchor anchor;
            private readonly SessionAnchorPoll poll;
            private readonly IMonotonicClock clock;
            private readonly long generation, origin;
            public Operation(RigidPose pose, long generation, long origin, IMonotonicClock clock)
            {
                this.generation = generation;
                this.origin = origin;
                this.clock = clock;
                root = new GameObject("Unsaved registration anchor");
                UnityRegistrationPose.Write(root.transform, pose);
                anchor = root.AddComponent<OVRSpatialAnchor>();
                poll = root.AddComponent<SessionAnchorPoll>();
                poll.Initialize(anchor, clock, generation, origin);
            }

            public AnchorFrame Read()
            {
                if (!root)
                    return new AnchorFrame(AnchorStatus.Disposed, null, generation, origin, clock.Now);
                if (!anchor)
                    return new AnchorFrame(AnchorStatus.Failed, null, generation, origin, clock.Now);
                if (!anchor.isActiveAndEnabled)
                    return new AnchorFrame(AnchorStatus.Lost, null, generation, origin, clock.Now);
                return poll.Latest;
            }

            public void Dispose()
            {
                if (!root)
                    return;
                // SDK 205 OnSpatialAnchorCreateComplete destroys late native spaces when component is gone.
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                root = null;
            }
        }
    }
}
