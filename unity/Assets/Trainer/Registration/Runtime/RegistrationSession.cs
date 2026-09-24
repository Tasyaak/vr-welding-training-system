using System;
using System.Collections.Generic;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration
{
    // Single-threaded owner. Ports are polled on the owner thread: no callback may publish registration.
    public sealed class RegistrationSession : IDisposable
    {
        private readonly SpatialCatalogSnapshot catalog;
        private readonly IMonotonicClock clock;
        private readonly IQrTracker tracker;
        private readonly ISessionAnchorFactory anchors;
        private readonly IRegistrationEvidenceSink sink;
        private readonly RegistrationQuality quality = new RegistrationQuality();
        private readonly List<QrObservation> window = new List<QrObservation>();
        private ISessionAnchor anchor;
        private RegistrationState state = RegistrationState.Unregistered;
        private RegistrationReason reason = RegistrationReason.None;
        private AssemblyBindingSnapshot binding;
        private PlatformStatus platform;
        private RigidPose? fixture, anchorFromFixture;
        private AnchorStatus? anchorStatus;
        private long generation, origin;
        private bool originSet, confirmed, dimensions;
        private string trackableId;
        private long lastSequence = -1;
        private double now, lastNow, deadline, span;
        private double? lastQr, sourceTime, translation, angular;
        private double? confirmedAt, anchorReceivedAt;
        private QrObservation lastObservation;
        private string diagnosticDetail;
        private readonly string sessionId = Guid.NewGuid().ToString("N");
        public RegistrationSnapshot LastSnapshot { get; private set; }

        public RegistrationSession(SpatialCatalogSnapshot catalog, IMonotonicClock clock, IQrTracker tracker, ISessionAnchorFactory anchors, IRegistrationEvidenceSink sink = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            this.anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
            this.sink = sink;
            now = lastNow = clock.Now;
            if (!RegistrationMath.Finite(now) || now < 0)
                throw new ArgumentException("Clock must supply finite monotonic seconds");
            Publish();
        }

        // Consumers call Read, not cached history, before using a frame. Tick is also driven by the host.
        public RegistrationSnapshot Read()
        {
            Tick();
            return LastSnapshot;
        }

        public void Start()
        {
            if (state == RegistrationState.Disposed)
                throw new ObjectDisposedException(nameof(RegistrationSession));
            if (state != RegistrationState.Unregistered && state != RegistrationState.Lost)
                return;
            if (!UpdateTime())
                return;
            ReleaseAnchor();
            generation++;
            originSet = false;
            binding = null;
            trackableId = null;
            lastSequence = -1;
            lastQr = sourceTime = null;
            fixture = null;
            confirmed = false;
            anchorStatus = null;
            ResetWindow();
            confirmedAt = anchorReceivedAt = null;
            lastObservation = null;
            diagnosticDetail = null;
            state = RegistrationState.Acquiring;
            reason = RegistrationReason.AwaitingQr;
            deadline = now + quality.AcquireTimeout;
            try
            {
                tracker.RequestScanning();
            }
            catch (Exception error)
            {
                diagnosticDetail = error.Message;
                Lose(RegistrationReason.PlatformFailure);
            }

            Publish();
        }

        public void Confirm(bool correctPart, bool secured, bool plausibleOverlay)
        {
            Tick();
            if (state != RegistrationState.Preview || reason == RegistrationReason.AwaitingTrackedQr)
                return;
            if (!correctPart || !secured || !plausibleOverlay)
            {
                reason = RegistrationReason.ConfirmationIncomplete;
                Publish();
                return;
            }

            confirmed = true;
            confirmedAt = now;
            state = RegistrationState.Anchoring;
            reason = RegistrationReason.None;
            deadline = now + quality.AnchorTimeout;
            try
            {
                var f = fixture.Value;
                anchor = anchors.Create(RegistrationMath.Pose("World", "Anchor", f.Position, f.Rotation), generation, origin);
                if (anchor == null)
                    Lose(RegistrationReason.AnchorFailed);
                else
                    anchorStatus = AnchorStatus.Creating;
            }
            catch (Exception error)
            {
                diagnosticDetail = error.Message;
                Lose(RegistrationReason.AnchorFailed);
            }

            Publish();
        }

        public void Cancel()
        {
            if (state == RegistrationState.Disposed)
                return;
            UpdateTime();
            fixture = null;
            confirmed = false;
            ResetWindow();
            state = RegistrationState.Unregistered;
            reason = RegistrationReason.Cancelled;
            Cleanup();
            Publish();
        }

        public void Invalidate(RegistrationReason cause)
        {
            if (state == RegistrationState.Disposed)
                return;
            if (cause != RegistrationReason.AssemblyChanged && cause != RegistrationReason.WrongPart && cause != RegistrationReason.MarkerMoved && cause != RegistrationReason.OriginChanged && cause != RegistrationReason.TrackingLost)
                throw new ArgumentException("Use an explicit assembly/origin/tracking invalidation reason");
            UpdateTime();
            Lose(cause);
            Publish();
        }

        public void Tick()
        {
            if (state == RegistrationState.Disposed || !UpdateTime())
                return;
            if (state == RegistrationState.Unregistered || state == RegistrationState.Lost)
            {
                Publish();
                return;
            }

            try
            {
                TickActive();
            }
            catch (Exception error)
            {
                diagnosticDetail = error.Message;
                Lose(RegistrationReason.PlatformFailure);
            }

            Publish();
        }

        private void TickActive()
        {
            var frame = tracker.Read();
            // Ports timestamp during acquisition. Capture the clock AFTER the read, not before it.
            if (!UpdateTime())
                return;
            platform = frame.Platform;
            if (!Fresh(platform.ReceivedAt, quality.RequiredTrackingMaxAge))
            {
                Lose(RegistrationReason.TrackingLost);
                return;
            }

            if (state == RegistrationState.Acquiring && binding == null)
            {
                if (now > deadline)
                {
                    Lose(RegistrationReason.Timeout);
                    return;
                }

                var preflight = PlatformGate(platform);
                if (preflight != RegistrationReason.None)
                {
                    Reject(preflight);
                    return;
                }

                if (!platform.TrackingValid)
                {
                    Reject(RegistrationReason.TrackingLost);
                    return;
                }
            }

            if (!originSet)
            {
                origin = platform.OriginGeneration;
                originSet = true;
            }

            if (origin != platform.OriginGeneration)
            {
                Lose(RegistrationReason.OriginChanged);
                return;
            }

            if (!platform.TrackingValid)
            {
                Lose(RegistrationReason.TrackingLost);
                return;
            }

            if (state != RegistrationState.Registered && now > deadline)
            {
                Lose(RegistrationReason.Timeout);
                return;
            }

            if (state == RegistrationState.Registered || state == RegistrationState.Anchoring)
            {
                if (!platform.ConfigurationValid || platform.Permission != PermissionState.Granted)
                {
                    Lose(RegistrationReason.ConfigurationInvalid);
                    return;
                }

                ReadAnchor();
                if (state == RegistrationState.Lost)
                    return;
                // Zero observations is normal after acceptance. Only positive, fresh conflicting evidence invalidates.
                QrObservation freshDiagnostic = null;
                foreach (var diagnostic in frame.Observations)
                {
                    if (!FreshObservation(diagnostic))
                        continue;
                    if (freshDiagnostic != null)
                    {
                        Lose(RegistrationReason.AmbiguousQr);
                        return;
                    }

                    freshDiagnostic = diagnostic;
                }

                if (freshDiagnostic != null)
                    Monitor(freshDiagnostic);
                return;
            }

            var gate = PlatformGate(platform);
            if (gate != RegistrationReason.None)
            {
                Reject(gate);
                return;
            }

            if (frame.Observations.Count != 1)
            {
                if (frame.Observations.Count == 0 && PauseForSameTrackable(frame))
                    return;
                Reject(frame.Observations.Count == 0 ? RegistrationReason.AwaitingQr : RegistrationReason.AmbiguousQr);
                return;
            }

            var observation = frame.Observations[0];
            gate = Validate(observation, out var selected);
            if (binding == null && selected != null)
            {
                binding = selected;
                trackableId = observation.TrackableId;
            }

            if (gate != RegistrationReason.None)
            {
                Reject(gate);
                return;
            }

            if (binding != null && (binding.Id != selected.Id || trackableId != observation.TrackableId))
            {
                Reject(RegistrationReason.AmbiguousQr);
                binding = null;
                trackableId = null;
                lastSequence = -1;
                lastQr = null;
                return;
            }

            binding = selected;
            trackableId = observation.TrackableId;
            if (observation.Sequence == lastSequence)
                return;
            if (observation.Sequence < lastSequence || (lastQr.HasValue && observation.ReceivedAt <= lastQr.Value))
            {
                Reject(RegistrationReason.StaleObservation);
                return;
            }

            lastSequence = observation.Sequence;
            lastQr = observation.ReceivedAt;
            sourceTime = observation.SourceCapturedAt;
            lastObservation = observation;
            if (state == RegistrationState.Preview)
            {
                var expected = fixture.Value * binding.FixtureFromMarker;
                if (Conflict(expected, observation.WorldFromMarker.Value, quality.TranslationScatter, quality.AngularScatter))
                    Reject(RegistrationReason.UnstablePose);
                else
                    reason = RegistrationReason.AwaitingConfirmation;
                return;
            }

            window.RemoveAll(x => now - x.ReceivedAt > quality.Window);
            // Bound storage by time, not render rate. Still reject outliers on EVERY fresh update.
            if (window.Count > 0)
            {
                var baseline = RegistrationMath.Mean(window);
                if (Conflict(baseline, observation.WorldFromMarker.Value, quality.TranslationScatter, quality.AngularScatter))
                {
                    Reject(RegistrationReason.UnstablePose);
                    return;
                }

                if (observation.ReceivedAt - window[window.Count - 1].ReceivedAt < quality.MinimumSampleInterval)
                    return;
            }

            if (window.Count >= quality.MaximumObservations)
                window.RemoveAt(0);
            window.Add(observation);
            var average = RegistrationMath.Mean(window);
            translation = angular = 0;
            foreach (var sample in window)
            {
                translation = Math.Max(translation.Value, (sample.WorldFromMarker.Value.Position - average.Position).Length);
                angular = Math.Max(angular.Value, RegistrationMath.Angle(sample.WorldFromMarker.Value.Rotation, average.Rotation));
            }

            span = window[window.Count - 1].ReceivedAt - window[0].ReceivedAt;
            if (translation > quality.TranslationScatter || angular > quality.AngularScatter)
            {
                Reject(RegistrationReason.UnstablePose);
                return;
            }

            dimensions = true;
            reason = RegistrationReason.AwaitingQr;
            if (window.Count < quality.MinimumObservations || span < quality.MinimumSpan)
                return;
            fixture = average * binding.FixtureFromMarker.Inverse();
            state = RegistrationState.Preview;
            reason = RegistrationReason.AwaitingConfirmation;
            deadline = now + quality.PreviewTimeout;
        }

        private bool PauseForSameTrackable(TrackerFrame frame)
        {
            if (binding == null || !lastQr.HasValue)
                return false;
            foreach (var trackable in frame.Trackables)
            {
                if (trackable.TrackableId != trackableId || (trackable.IsTracked && !trackable.AwaitingTrackedUpdate))
                    continue;
                // No new grace period: the original observation's deadline keeps running.
                if (!Fresh(lastQr.Value, quality.QrMaxAge))
                    Reject(RegistrationReason.StaleObservation);
                else
                    reason = RegistrationReason.AwaitingTrackedQr;
                // Retain candidate/history only; Confirm is blocked and IsValid remains false.
                return true;
            }

            return false;
        }

        private bool FreshObservation(QrObservation observation) => observation != null && Fresh(observation.ReceivedAt, quality.QrMaxAge) && (!observation.SourceCapturedAt.HasValue || (Fresh(observation.SourceCapturedAt.Value, quality.QrMaxAge) && observation.SourceCapturedAt.Value <= observation.ReceivedAt));
        private RegistrationReason PlatformGate(PlatformStatus p)
        {
            if (!p.Supported)
                return RegistrationReason.Unsupported;
            if (p.Permission == PermissionState.Denied)
                return RegistrationReason.PermissionDenied;
            if (p.Permission != PermissionState.Granted)
                return RegistrationReason.PermissionRequired;
            if (!p.ConfigurationValid)
                return RegistrationReason.ConfigurationInvalid;
            if (!p.Requested || !p.Applied)
                return RegistrationReason.ConfigurationPending;
            if (!p.ConventionQualified)
                return RegistrationReason.ConventionUnqualified;
            return RegistrationReason.None;
        }

        private RegistrationReason Validate(QrObservation o, out AssemblyBindingSnapshot selected)
        {
            selected = null;
            if (o == null || !QrPayload.TryParse(o.CopyPayload(), out var partId))
                return RegistrationReason.MalformedPayload;
            try
            {
                selected = catalog.ResolveForPreview(partId);
            }
            catch (SpatialContentException)
            {
                return RegistrationReason.UnknownPart;
            }

            if (!RegistrationMath.IsPose(o.WorldFromMarker, "World", "Marker") || string.IsNullOrEmpty(o.TrackableId) || o.Sequence < 0)
                return RegistrationReason.InvalidPose;
            if (o.OriginGeneration != origin)
                return RegistrationReason.OriginChanged;
            if (!Fresh(o.ReceivedAt, quality.QrMaxAge) || (o.SourceCapturedAt.HasValue && (!Fresh(o.SourceCapturedAt.Value, quality.QrMaxAge) || o.SourceCapturedAt.Value > o.ReceivedAt)))
                return RegistrationReason.StaleObservation;
            if (!o.ObserverPosition.HasValue)
                return RegistrationReason.InvalidView;
            var delta = o.ObserverPosition.Value - o.WorldFromMarker.Value.Position;
            double distance = delta.Length;
            if (!RegistrationMath.Finite(distance) || distance < quality.MinimumViewingDistance || distance > quality.MaximumViewingDistance || Vec3.Dot(delta * (1 / distance), o.WorldFromMarker.Value.TransformDirection(new Vec3(0, 0, 1))) < quality.MinimumFacingCosine)
                return RegistrationReason.InvalidView;
            var marker = selected.Marker;
            if (marker.PrintCandidates.Count > 0 && marker.SelectedPrintCandidateId == null)
                return RegistrationReason.PrintCandidateNotSelected;
            if (!marker.WidthMetres.HasValue || !marker.HeightMetres.HasValue || marker.QuietZoneConvention == "Unqualified")
                return RegistrationReason.DimensionsUnknown;
            if (o.DimensionConvention != marker.QuietZoneConvention || !RegistrationMath.Finite(o.WidthMetres) || !RegistrationMath.Finite(o.HeightMetres) || Math.Abs(o.WidthMetres / marker.WidthMetres.Value - 1) > quality.DimensionRelativeTolerance || Math.Abs(o.HeightMetres / marker.HeightMetres.Value - 1) > quality.DimensionRelativeTolerance)
                return RegistrationReason.DimensionsMismatch;
            if (!marker.LabelThicknessMetres.HasValue || string.IsNullOrWhiteSpace(marker.InstallationEvidence))
                return RegistrationReason.MarkerPlaneUnqualified;
            return RegistrationReason.None;
        }

        private void ReadAnchor()
        {
            if (anchor == null)
            {
                Lose(RegistrationReason.AnchorFailed);
                return;
            }

            var a = anchor.Read();
            if (!UpdateTime())
                return;
            anchorStatus = a.Status;
            anchorReceivedAt = a.ReceivedAt;
            if (a.RegistrationGeneration != generation || a.OriginGeneration != origin)
            {
                Lose(RegistrationReason.OriginChanged);
                return;
            }

            if (a.Status == AnchorStatus.Failed || a.Status == AnchorStatus.Disposed)
            {
                Lose(RegistrationReason.AnchorFailed);
                return;
            }

            if (a.Status == AnchorStatus.Lost || !Fresh(a.ReceivedAt, quality.RequiredTrackingMaxAge))
            {
                Lose(RegistrationReason.AnchorLost);
                return;
            }

            if (a.Status != AnchorStatus.Localized)
            {
                if (state == RegistrationState.Registered)
                    Lose(RegistrationReason.AnchorLost);
                return;
            }

            if (!RegistrationMath.IsPose(a.WorldFromAnchor, "World", "Anchor"))
            {
                Lose(RegistrationReason.AnchorFailed);
                return;
            }

            if (state == RegistrationState.Anchoring)
            {
                anchorFromFixture = a.WorldFromAnchor.Value.Inverse() * fixture.Value;
                state = RegistrationState.Registered;
                reason = RegistrationReason.None;
            }

            fixture = a.WorldFromAnchor.Value * anchorFromFixture.Value;
        }

        private void Monitor(QrObservation observation)
        {
            // Invalid/stale diagnostic QR cannot move the frozen registration or masquerade as new evidence.
            if (!FreshObservation(observation))
                return;
            if (!QrPayload.TryParse(observation.CopyPayload(), out var id) || id != binding.PartId)
            {
                Lose(RegistrationReason.QrConflict);
                return;
            }

            if (observation.OriginGeneration != origin)
            {
                Lose(RegistrationReason.OriginChanged);
                return;
            }

            if (!RegistrationMath.IsPose(observation.WorldFromMarker, "World", "Marker"))
                return;
            var check = Validate(observation, out _);
            if (check == RegistrationReason.DimensionsMismatch || check == RegistrationReason.MarkerPlaneUnqualified)
            {
                Lose(RegistrationReason.QrConflict);
                return;
            }

            if (check != RegistrationReason.None)
                return;
            var expected = fixture.Value * binding.FixtureFromMarker;
            if (Conflict(expected, observation.WorldFromMarker.Value, quality.ConflictTranslation, quality.ConflictAngle))
                Lose(RegistrationReason.QrConflict);
        }

        private static bool Conflict(RigidPose a, RigidPose b, double metres, double radians) => (a.Position - b.Position).Length > metres || RegistrationMath.Angle(a.Rotation, b.Rotation) > radians;
        private bool Fresh(double timestamp, double age) => RegistrationMath.Finite(timestamp) && timestamp >= 0 && timestamp <= now && now - timestamp <= age;
        private bool UpdateTime()
        {
            double value = clock.Now;
            if (!RegistrationMath.Finite(value) || value < lastNow)
            {
                Lose(RegistrationReason.ClockInvalid);
                Publish();
                return false;
            }

            now = lastNow = value;
            return true;
        }

        private void ResetWindow()
        {
            window.Clear();
            span = 0;
            translation = angular = null;
            dimensions = false;
        }

        private void Reject(RegistrationReason cause)
        {
            if (state == RegistrationState.Preview)
            {
                state = RegistrationState.Acquiring;
                deadline = now + quality.AcquireTimeout;
            }

            fixture = null;
            confirmed = false;
            ResetWindow();
            reason = cause;
        }

        private void Lose(RegistrationReason cause)
        {
            fixture = null;
            confirmed = false;
            state = RegistrationState.Lost;
            reason = cause;
            Cleanup();
        }

        private void Cleanup()
        {
            ReleaseAnchor();
            try
            {
                tracker.StopScanning();
            }
            catch (Exception error)
            {
                diagnosticDetail = error.Message;
            }
        }

        private void ReleaseAnchor()
        {
            var previous = anchor;
            anchor = null;
            anchorFromFixture = null;
            if (previous != null)
            {
                anchorStatus = AnchorStatus.Disposed;
                try
                {
                    previous.Dispose();
                }
                catch (Exception error)
                {
                    diagnosticDetail = error.Message;
                }
            }
        }

        private void Publish()
        {
            LastSnapshot = Capture();
            try
            {
                sink?.Record(LastSnapshot);
            }
            catch (Exception error)
            {
                diagnosticDetail = error.Message;
                if (state != RegistrationState.Disposed)
                    Lose(RegistrationReason.PlatformFailure);
                LastSnapshot = Capture();
            }
        }

        private RegistrationSnapshot Capture() => new RegistrationSnapshot(state, reason, generation, origin, now, binding, fixture, fixture.HasValue && binding != null ? (RigidPose? )(fixture.Value * binding.FixtureFromWorkpiece) : null, window.Count, span, translation, angular, dimensions, confirmed, anchorStatus, platform, lastQr, sourceTime, sessionId, lastObservation, confirmedAt, anchorReceivedAt, diagnosticDetail);
        public void Dispose()
        {
            if (state == RegistrationState.Disposed)
                return;
            fixture = null;
            confirmed = false;
            state = RegistrationState.Disposed;
            reason = RegistrationReason.SessionEnded;
            Cleanup();
            try
            {
                tracker.Dispose();
            }
            catch (Exception error)
            {
                diagnosticDetail = error.Message;
            }

            Publish();
        }
    }
}
