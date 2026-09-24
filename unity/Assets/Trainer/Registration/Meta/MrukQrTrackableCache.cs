using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeldingTrainer.Registration.Meta
{
    // Membership follows MRUK lifecycle, not IsTracked. No clock or pose polling in this cache.
    public sealed class MrukQrTrackableCache
    {
        private sealed class Entry
        {
            public string Identity;
            public MrukPlaneUpdateWitness Witness;
            public bool PendingAdd, TrackingKnown, IsTracked, AwaitingTrackedUpdate = true;
            public QrObservation LastObservation;
        }

        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private readonly Action<string> diagnostic;
        private long lifetime;
        public MrukQrTrackableCache(Action<string> diagnostic = null) => this.diagnostic = diagnostic;
        public void Add(int instance, List<Vector2> boundary, bool nativeAdded)
        {
            if (entries.ContainsKey(instance))
                return;
            var entry = new Entry
            {
                Identity = instance + ":" + (++lifetime),
                Witness = new MrukPlaneUpdateWitness(boundary),
                PendingAdd = nativeAdded
            };
            entries.Add(instance, entry);
            Log($"registry-{(nativeAdded ? "add" : "seed")} instance={instance} identity={entry.Identity}");
        }

        public QrObservation Poll(int instance, bool isTracked, List<Vector2> boundary, Func<string, QrObservation> capture)
        {
            // An object found by enumeration may predate scanning. Its initial pose is not new evidence.
            Add(instance, boundary, false);
            var entry = entries[instance];
            if (!entry.TrackingKnown || entry.IsTracked != isTracked)
                Log($"tracking instance={instance} identity={entry.Identity} from={(entry.TrackingKnown ? entry.IsTracked.ToString() : "unknown")} to={isTracked}");
            entry.TrackingKnown = true;
            entry.IsTracked = isTracked;
            // Consume even while untracked: an untracked update must not be promoted upon reacquisition.
            bool planeUpdated = entry.Witness.Consume(boundary);
            bool updated = planeUpdated || entry.PendingAdd;
            entry.PendingAdd = false;
            if (updated)
                Log($"sdk-plane-update instance={instance} identity={entry.Identity} tracked={isTracked} witness={(planeUpdated ? "boundary-mutation" : "TrackableAdded")}");
            if (!isTracked)
            {
                entry.AwaitingTrackedUpdate = true;
                return null;
            }

            if (updated)
            {
                entry.LastObservation = capture(entry.Identity);
                entry.AwaitingTrackedUpdate = false;
                var observation = entry.LastObservation;
                Log($"observation instance={instance} identity={entry.Identity} seq={observation.Sequence} received={observation.ReceivedAt:F6} pose={observation.WorldFromMarker.HasValue} size={observation.WidthMetres * 1000:F3}x{observation.HeightMetres * 1000:F3}mm");
            }

            return entry.AwaitingTrackedUpdate ? null : entry.LastObservation;
        }

        public IReadOnlyList<QrTrackableStatus> Snapshot()
        {
            var result = new List<QrTrackableStatus>();
            foreach (var entry in entries.Values)
                result.Add(new QrTrackableStatus(entry.Identity, entry.IsTracked, entry.AwaitingTrackedUpdate, entry.LastObservation));
            return result.AsReadOnly();
        }

        public void Remove(int instance)
        {
            if (entries.TryGetValue(instance, out var entry))
                Log($"registry-remove instance={instance} identity={entry.Identity}");
            entries.Remove(instance);
        }

        public void Reconcile(HashSet<int> present)
        {
            var removed = new List<int>();
            foreach (var instance in entries.Keys)
                if (!present.Contains(instance))
                    removed.Add(instance);
            foreach (var instance in removed)
            {
                Log($"registry-missing instance={instance} (enumeration, not a TrackableRemoved event)");
                Remove(instance);
            }
        }

        public void Clear() => entries.Clear(); // Never reuse a lifetime identity after reset/removal.
        private void Log(string message)
        {
            try
            {
                diagnostic?.Invoke(message);
            }
            catch
            { /* Diagnostic listeners must not change registration semantics. */
            }
        }
    }
}
