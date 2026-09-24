using System;
using Meta.XR.MRUtilityKit;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration.Meta
{
    /// <summary>
    /// Reusable owner of the real Quest registration runtime.
    ///
    /// This class deliberately contains no UI and no controller mapping. It owns the
    /// Meta QR tracker, RegistrationSession and unsaved session-anchor factory, so the
    /// standalone preview and the production integration can use exactly the same
    /// registration implementation.
    /// </summary>
    public sealed class MetaRegistrationRuntime : IDisposable
    {
        private readonly MetaQrTracker tracker;
        private readonly RegistrationSession session;
        private bool disposed;

        public RegistrationSnapshot Snapshot => session.LastSnapshot;
        public TrackerFrame LastFrame => tracker.LastFrame;
        public string RuntimeTuple => tracker.RuntimeTuple;

        public MetaRegistrationRuntime(
            SpatialCatalogSnapshot catalog,
            MRUK mruk,
            OVRCameraRig rig,
            QrAdapterQualification qualification,
            Action<string> diagnostic = null,
            IRegistrationEvidenceSink sink = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!mruk)
                throw new ArgumentNullException(nameof(mruk));
            if (!rig)
                throw new ArgumentNullException(nameof(rig));

            var clock = new UnityMonotonicClock();
            tracker = new MetaQrTracker(mruk, rig, qualification, clock, diagnostic);
            try
            {
                session = new RegistrationSession(
                    catalog,
                    clock,
                    tracker,
                    new MetaSessionAnchorFactory(clock),
                    sink);
            }
            catch
            {
                tracker.Dispose();
                throw;
            }
        }

        public RegistrationSnapshot Read()
        {
            ThrowIfDisposed();
            return session.Read();
        }

        public void BeginRegistration()
        {
            ThrowIfDisposed();
            session.Start();
        }

        public void ConfirmAssembly(
            bool correctPart,
            bool secured,
            bool plausibleOverlay)
        {
            ThrowIfDisposed();
            session.Confirm(correctPart, secured, plausibleOverlay);
        }

        public void CancelRegistration()
        {
            ThrowIfDisposed();
            session.Cancel();
        }

        public void InvalidateAssembly()
        {
            ThrowIfDisposed();
            session.Invalidate(RegistrationReason.AssemblyChanged);
        }

        public void InvalidateTracking()
        {
            ThrowIfDisposed();
            session.Invalidate(RegistrationReason.TrackingLost);
        }

        public void InvalidateOrigin()
        {
            ThrowIfDisposed();
            session.Invalidate(RegistrationReason.OriginChanged);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            session.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MetaRegistrationRuntime));
        }
    }
}
