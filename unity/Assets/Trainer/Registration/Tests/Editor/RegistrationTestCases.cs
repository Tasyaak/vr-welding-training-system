using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration.Tests
{
    public sealed class RegistrationTestCases
    {
        private readonly string generated;
        private readonly ISpatialJson codec;
        private readonly Func<object, string> encode;
        private SpatialCatalogSnapshot synthetic;
        public RegistrationTestCases(string root, ISpatialJson codec, Func<object, string> encode)
        {
            generated = Path.Combine(root, "unity/Assets/Trainer/Content/Spatial/Generated");
            this.codec = codec;
            this.encode = encode;
        }

        private SpatialCatalogSnapshot Catalog(bool measured = true, Action<CatalogData> mutate = null, bool missingSemantics = false)
        {
            if (measured && mutate == null && !missingSemantics && synthetic != null)
                return synthetic;
            var data = codec.Read<CatalogData>(File.ReadAllText(Path.Combine(generated, "supplied.spatial")));
            if (measured)
            {
                data.markers[0].printCandidates = Array.Empty<PrintCandidateData>();
                data.markers[0].selectedPrintCandidateId = "";
                data.markers[0].dimensionsKnown = true;
                data.markers[0].widthMetres = .06;
                data.markers[0].heightMetres = .06;
                data.markers[0].quietZoneConvention = "ExcludesQuietZone";
                data.markers[0].physicalPlaneKnown = true;
                data.markers[0].installationEvidence = "SYNTHETIC TEST ONLY: zero-thickness marker, not station evidence";
                data.bindings[0].fixtureFromWorkpiece.position = new Vec3(.01, .02, .03);
                data.bindings[0].fixtureFromWorkpiece.rotation = new Quat(0, 0, Math.Sqrt(.5), Math.Sqrt(.5));
            }

            mutate?.Invoke(data);
            var replacements = new Dictionary<string, byte[]>();
            if (missingSemantics)
                foreach (var import in data.imports)
                    if (import.sourceId == data.workpieces[0].sourceId)
                    {
                        var geometry = codec.Read<GeometryData>(File.ReadAllText(Path.Combine(generated, import.geometryFile)));
                        geometry.semanticStatus = "MissingAuthoritativeSelection";
                        geometry.semanticReason = "SYNTHETIC missing semantics regression fixture";
                        geometry.seams = Array.Empty<SeamData>();
                        geometry.cleaningRegions = Array.Empty<CleaningData>();
                        var bytes = Encoding.UTF8.GetBytes(encode(geometry));
                        replacements.Add(import.geometryFile, bytes);
                        import.geometrySha256 = SpatialCatalogSnapshot.Sha256(bytes);
                    }

            var result = SpatialCatalogSnapshot.Load(encode(data), codec, name => replacements.TryGetValue(name, out var bytes) ? bytes : File.ReadAllBytes(Path.Combine(generated, name)));
            if (measured && mutate == null && !missingSemantics)
                synthetic = result;
            return result;
        }

        private static void SelectPrint(CatalogData data, int index, bool installed)
        {
            var marker = data.markers[0];
            var print = marker.printCandidates[index];
            marker.selectedPrintCandidateId = print.id;
            marker.dimensionsKnown = true;
            marker.widthMetres = print.symbolWidthMetres;
            marker.heightMetres = print.symbolHeightMetres;
            marker.labelThicknessMetres = print.labelThicknessMetres;
            marker.quietZoneConvention = print.symbolQuietZoneConvention;
            marker.physicalPlaneKnown = installed;
            marker.installationEvidence = installed ? "SYNTHETIC installed-plane evidence" : "";
            if (installed)
                data.bindings[0].fixtureFromMarker.position.y += print.labelThicknessMetres;
        }

        private sealed class FakeClock : IMonotonicClock
        {
            public double Now { get; set; } = 10;
        }

        private sealed class FakeTracker : IQrTracker
        {
            public readonly FakeClock Clock;
            public QrObservation[] Observations = Array.Empty<QrObservation>();
            public bool Supported = true, Config = true, Applied = true, Convention = true, Tracking = true;
            public PermissionState Permission = PermissionState.Granted;
            public long Origin = 1;
            public double Age;
            public double AdvanceOnRead;
            public bool Requested, Disposed, ThrowRead, ThrowStop;
            public FakeTracker(FakeClock clock)
            {
                Clock = clock;
            }

            public void RequestScanning()
            {
                Requested = true;
            }

            public void StopScanning()
            {
                Requested = false;
                if (ThrowStop)
                    throw new InvalidOperationException("synthetic stop failure");
            }

            public TrackerFrame Read()
            {
                Clock.Now += AdvanceOnRead;
                if (ThrowRead)
                    throw new InvalidOperationException("synthetic read failure");
                return new TrackerFrame(new PlatformStatus(Supported, Permission, Config, Requested, Applied, Convention, Tracking, Origin, Clock.Now - Age, "synthetic-runtime", "synthetic convention evidence"), Observations);
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class FakeAnchor : ISessionAnchor
        {
            public FakeClock Clock;
            public AnchorStatus Status = AnchorStatus.Creating;
            public RigidPose Pose;
            public long Generation, Origin;
            public double Age;
            public bool Disposed, ThrowDispose;
            public AnchorFrame Read() => new AnchorFrame(Status, Pose, Generation, Origin, Clock.Now - Age);
            public void Dispose()
            {
                Disposed = true;
                if (ThrowDispose)
                    throw new InvalidOperationException("synthetic disposal failure");
            }
        }

        private sealed class FakeAnchors : ISessionAnchorFactory
        {
            public FakeClock Clock;
            public readonly List<FakeAnchor> Created = new List<FakeAnchor>();
            public ISessionAnchor Create(RigidPose pose, long generation, long origin)
            {
                var a = new FakeAnchor
                {
                    Clock = Clock,
                    Pose = pose,
                    Generation = generation,
                    Origin = origin
                };
                Created.Add(a);
                return a;
            }
        }

        private sealed class Harness : IDisposable
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeTracker Tracker;
            public readonly FakeAnchors Anchors;
            public readonly RegistrationSession Session;
            public readonly RigidPose Marker = RegistrationMath.Pose("World", "Marker", new Vec3(1, 2, 3), new Quat(0, 0, Math.Sqrt(.5), Math.Sqrt(.5)));
            private long sequence;
            public Harness(SpatialCatalogSnapshot catalog)
            {
                Tracker = new FakeTracker(Clock);
                Anchors = new FakeAnchors
                {
                    Clock = Clock
                };
                Session = new RegistrationSession(catalog, Clock, Tracker, Anchors);
                Session.Start();
            }

            public QrObservation Observation(string payload = "LW1:PART-001", RigidPose? pose = null, double size = .06, double age = 0, Vec3? eye = null, long? seq = null, string convention = "ExcludesQuietZone")
            {
                var p = pose ?? Marker;
                return new QrObservation(Encoding.UTF8.GetBytes(payload), "qr1", seq ?? ++sequence, Tracker.Origin, Clock.Now - age, p, size, size, convention, eye ?? p.TransformPoint(new Vec3(0, 0, .5)));
            }

            public void Sample(QrObservation observation = null)
            {
                Tracker.Observations = new[]
                {
                    observation ?? Observation()
                };
                Session.Tick();
            }

            public void Preview(double size = .06)
            {
                for (int i = 0; i < 4; i++)
                {
                    Clock.Now += .75;
                    Sample(Observation(size: size));
                }

                Equal(RegistrationState.Preview, Session.LastSnapshot.State);
            }

            public void Register(double size = .06)
            {
                Preview(size);
                Session.Confirm(true, true, true);
                Equal(RegistrationState.Anchoring, Session.LastSnapshot.State);
                Anchors.Created[0].Status = AnchorStatus.Localized;
                Session.Tick();
                Equal(RegistrationState.Registered, Session.LastSnapshot.State);
            }

            public void Dispose()
            {
                Session.Dispose();
            }
        }

        private static void Check(bool value, string message = "assertion failed")
        {
            if (!value)
                throw new Exception(message);
        }

        private static void Equal<T>(T a, T b)
        {
            Check(EqualityComparer<T>.Default.Equals(a, b), $"Expected {a}, got {b}");
        }

        private static void Near(Vec3 expected, Vec3 actual)
        {
            Check((expected - actual).Length < 1e-8, $"Position mismatch: {expected.x},{expected.y},{expected.z} / {actual.x},{actual.y},{actual.z}");
        }

        private static void Throws(Action action)
        {
            try
            {
                action();
            }
            catch (SpatialContentException)
            {
                return;
            }

            throw new Exception("Expected rejected spatial content");
        }

        public IEnumerable<KeyValuePair<string, Action>> Cases()
        {
            var tests = new Dictionary<string, Action>();
            tests.Add("Port receive timestamps may advance during a read", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Tracker.AdvanceOnRead = .01;
                    h.Register();
                    Check(h.Session.LastSnapshot.Platform.ReceivedAt <= h.Session.LastSnapshot.CapturedAt);
                }
            });
            tests.Add("Two actual print alternatives are never auto-selected by size", () =>
            {
                foreach (var size in new[]
                {
                    .053,
                    .063
                }

                )
                    using (var h = new Harness(Catalog(false)))
                    {
                        h.Sample(h.Observation(size: size));
                        Equal(RegistrationReason.PrintCandidateNotSelected, h.Session.LastSnapshot.Reason);
                        Check(h.Session.LastSnapshot.Binding.Marker.SelectedPrintCandidateId == null);
                    }
            });
            tests.Add("Selected specification does not qualify physical plane", () =>
            {
                using (var h = new Harness(Catalog(false, d => SelectPrint(d, 0, false))))
                {
                    h.Sample(h.Observation(size: .053));
                    Equal(RegistrationReason.MarkerPlaneUnqualified, h.Session.LastSnapshot.Reason);
                    Check(h.Session.LastSnapshot.Binding.Marker.LabelThicknessMetres.HasValue);
                }
            });
            tests.Add("Either explicitly selected and installed print can register", () =>
            {
                for (int index = 0; index < 2; index++)
                    using (var h = new Harness(Catalog(false, d => SelectPrint(d, index, true))))
                    {
                        h.Register(index == 0 ? .053 : .063);
                        Equal(index == 0 ? "QR-PRINT-A" : "QR-PRINT-B", h.Session.LastSnapshot.Binding.Marker.SelectedPrintCandidateId);
                        Check(!h.Session.LastSnapshot.EligibleForScoring);
                    }
            });
            tests.Add("Other print size cannot impersonate selected print", () =>
            {
                using (var h = new Harness(Catalog(false, d => SelectPrint(d, 0, true))))
                {
                    h.Sample(h.Observation(size: .063));
                    Equal(RegistrationReason.DimensionsMismatch, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Missing semantic selection still does not block fixture registration", () =>
            {
                using (var h = new Harness(Catalog(missingSemantics: true)))
                {
                    h.Register();
                    Equal("MissingAuthoritativeSelection", h.Session.LastSnapshot.Binding.WorkpieceGeometry.SemanticStatus);
                    Check(!h.Session.LastSnapshot.EligibleForScoring);
                }
            });
            tests.Add("Port and cleanup exceptions cannot preserve valid data", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Tracker.ThrowRead = true;
                    h.Tracker.ThrowStop = true;
                    h.Anchors.Created[0].ThrowDispose = true;
                    h.Session.Tick();
                    Equal(RegistrationState.Lost, h.Session.LastSnapshot.State);
                    Check(!h.Session.LastSnapshot.WorldFromWorkpiece.HasValue);
                    Check(!string.IsNullOrWhiteSpace(h.Session.LastSnapshot.DiagnosticDetail));
                }
            });
            tests.Add("Cancel clears pose even if disposal fails", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Anchors.Created[0].ThrowDispose = true;
                    h.Tracker.ThrowStop = true;
                    h.Session.Cancel();
                    Equal(RegistrationState.Unregistered, h.Session.LastSnapshot.State);
                    Check(!h.Session.LastSnapshot.IsValid);
                }
            });
            tests.Add("Strict payloads", () =>
            {
                Check(QrPayload.TryParse(Encoding.UTF8.GetBytes("LW1:PART-001"), out var id));
                Equal("PART-001", id);
                foreach (var text in new[]
                {
                    "",
                    "LW2:PART-001",
                    "LW1:",
                    " LW1:A",
                    "LW1:A\n",
                    "LW1:A\0",
                    "LW1:../A",
                    "LW1:ДЕТАЛЬ",
                    "LW1:" + new string ('a', 65),
                    "LW1:A?pose=1"
                }

                )
                    Check(!QrPayload.TryParse(Encoding.UTF8.GetBytes(text), out _), text);
                Check(!QrPayload.TryParse(new byte[] { 0xc0, 0x80 }, out _));
            });
            tests.Add("Contract numeric composition", () =>
            {
                var wm = RegistrationMath.Pose("World", "Marker", new Vec3(1, 2, 3), new Quat(0, 0, 0, 1));
                var fm = RegistrationMath.Pose("Fixture", "Marker", new Vec3(.1, 0, 0), new Quat(0, 0, 0, 1));
                var fp = RegistrationMath.Pose("Fixture", "Workpiece", new Vec3(0, .008, 0), new Quat(0, 0, 0, 1));
                Near(new Vec3(.9, 2.008, 3), (wm * fm.Inverse() * fp).Position);
            });
            tests.Add("MRUK off-centre rotated plane", () =>
            {
                var mruk = RegistrationMath.Pose("World", "MrukPlane", new Vec3(1, 2, 3), new Quat(0, 0, Math.Sqrt(.5), Math.Sqrt(.5)));
                var marker = RegistrationMath.CenterMrukPlane(mruk, .1, .2);
                Near(new Vec3(.8, 2.1, 3), marker.Position);
                Near(new Vec3(0, -1, 0), marker.TransformDirection(new Vec3(1, 0, 0)));
                Near(new Vec3(0, 0, -1), marker.TransformDirection(new Vec3(0, 0, 1)));
            });
            tests.Add("Invalid rigid scale and NaN", () =>
            {
                Throws(() => new RigidPose(new PoseData { destination = "World", source = "Marker", scale = 2, rotation = new Quat(0, 0, 0, 1) }));
                Throws(() => RegistrationMath.Pose("World", "Marker", new Vec3(double.NaN, 0, 0), new Quat(0, 0, 0, 1)));
                Throws(() => RegistrationMath.Pose("World", "Marker", new Vec3(), new Quat(0, 0, 0, 2)));
            });
            tests.Add("Duplicate active bindings rejected by upstream", () => Throws(() => Catalog(true, d => d.bindings = new[] { d.bindings[0], d.bindings[0] })));
            tests.Add("Actual approved part remains blocked by unselected prints", () =>
            {
                using (var h = new Harness(Catalog(false)))
                {
                    h.Sample();
                    Equal(RegistrationReason.PrintCandidateNotSelected, h.Session.LastSnapshot.Reason);
                    Equal("Approved", h.Session.LastSnapshot.Binding.WorkpieceGeometry.SemanticStatus);
                    Equal(2, h.Session.LastSnapshot.Binding.Marker.PrintCandidates.Count);
                    h.Session.Confirm(true, true, true);
                    Check(!h.Session.LastSnapshot.IsValid);
                }
            });
            tests.Add("Measured synthetic marker registers with approved semantic content", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    var s = h.Session.LastSnapshot;
                    Check(s.IsValid);
                    Check(!s.EligibleForScoring);
                    Check(!s.PhysicallyQualified);
                    Equal("Approved", s.Binding.WorkpieceGeometry.SemanticStatus);
                    Check(s.AbsoluteAccuracyMetres == null && s.ReprojectionConfidence == null && s.QrSourceCapturedAt == null);
                }
            });
            tests.Add("Nonidentity workpiece and marker direction", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    var s = h.Session.LastSnapshot;
                    var expected = h.Marker * s.Binding.FixtureFromMarker.Inverse() * s.Binding.FixtureFromWorkpiece;
                    Near(expected.Position, s.WorldFromWorkpiece.Value.Position);
                    Check(RegistrationMath.Angle(expected.Rotation, s.WorldFromWorkpiece.Value.Rotation) < 1e-7);
                    Near(h.Marker.Position, (s.WorldFromFixture.Value * s.Binding.FixtureFromMarker).Position);
                }
            });
            foreach (var pair in new Dictionary<string, RegistrationReason>
            {
                {
                    "LW1:UNKNOWN",
                    RegistrationReason.UnknownPart
                },
                {
                    "LW1:A\n",
                    RegistrationReason.MalformedPayload
                }
            }

            )
            {
                var p = pair;
                tests.Add(p.Value.ToString(), () =>
                {
                    using (var h = new Harness(Catalog()))
                    {
                        h.Sample(h.Observation(p.Key));
                        Equal(p.Value, h.Session.LastSnapshot.Reason);
                    }
                });
            }

            tests.Add("Payload without pose", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(new QrObservation(Encoding.UTF8.GetBytes("LW1:PART-001"), "qr1", 1, 1, h.Clock.Now, null, .06, .06, "ExcludesQuietZone", new Vec3()));
                    Equal(RegistrationReason.InvalidPose, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Stale observation", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(age: 2));
                    Equal(RegistrationReason.StaleObservation, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Future observation", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(age: -1));
                    Equal(RegistrationReason.StaleObservation, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Dimension mismatch", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(size: .09));
                    Equal(RegistrationReason.DimensionsMismatch, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("NaN dimensions", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(size: double.NaN));
                    Equal(RegistrationReason.DimensionsMismatch, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Quiet zone mismatch", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(convention: "IncludesQuietZone"));
                    Equal(RegistrationReason.DimensionsMismatch, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Backside rejected", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(eye: h.Marker.TransformPoint(new Vec3(0, 0, -.5))));
                    Equal(RegistrationReason.InvalidView, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Bad view distance", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(eye: h.Marker.TransformPoint(new Vec3(0, 0, 4))));
                    Equal(RegistrationReason.InvalidView, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Duplicate visible identities", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Tracker.Observations = new[]
                    {
                        h.Observation(),
                        h.Observation()
                    };
                    h.Session.Tick();
                    Equal(RegistrationReason.AmbiguousQr, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Repeated polling is not fresh observations", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample();
                    for (int i = 0; i < 20; i++)
                        h.Session.Tick();
                    Equal(1, h.Session.LastSnapshot.ObservationCount);
                    Check(!h.Session.LastSnapshot.IsValid);
                }
            });
            tests.Add("Outlier resets window", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample();
                    h.Clock.Now += .75;
                    h.Sample(h.Observation(pose: RegistrationMath.Pose("World", "Marker", h.Marker.Position + new Vec3(.1, 0, 0), h.Marker.Rotation)));
                    Equal(RegistrationReason.UnstablePose, h.Session.LastSnapshot.Reason);
                    Equal(0, h.Session.LastSnapshot.ObservationCount);
                }
            });
            tests.Add("Quaternion signs have identical mean", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    var q = h.Marker.Rotation;
                    var a = h.Observation();
                    var b = h.Observation(pose: RegistrationMath.Pose("World", "Marker", h.Marker.Position, new Quat(-q.x, -q.y, -q.z, -q.w)));
                    var average = RegistrationMath.Mean(new[] { a, b });
                    Check(RegistrationMath.Angle(q, average.Rotation) < 1e-7);
                }
            });
            tests.Add("Requires full confirmation", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Preview();
                    h.Session.Confirm(true, false, true);
                    Equal(RegistrationReason.ConfirmationIncomplete, h.Session.LastSnapshot.Reason);
                    Equal(0, h.Anchors.Created.Count);
                }
            });
            tests.Add("Confirm cannot override fresh invalid dimensions", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Preview();
                    h.Tracker.Observations = new[]
                    {
                        h.Observation(size: .2)
                    };
                    h.Session.Confirm(true, true, true);
                    Check(h.Session.LastSnapshot.State != RegistrationState.Anchoring);
                }
            });
            tests.Add("Preview expires or becomes stale before confirm", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Preview();
                    h.Clock.Now += 2;
                    h.Session.Confirm(true, true, true);
                    Equal(RegistrationReason.StaleObservation, h.Session.LastSnapshot.Reason);
                    Equal(0, h.Anchors.Created.Count);
                }
            });
            tests.Add("Acquisition timeout", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Clock.Now += 46;
                    h.Session.Tick();
                    Equal(RegistrationReason.Timeout, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Anchor timeout disposes pending operation", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Preview();
                    h.Session.Confirm(true, true, true);
                    h.Clock.Now += 16;
                    h.Session.Tick();
                    Equal(RegistrationReason.Timeout, h.Session.LastSnapshot.Reason);
                    Check(h.Anchors.Created[0].Disposed);
                }
            });
            tests.Add("Anchor failure", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Preview();
                    h.Session.Confirm(true, true, true);
                    h.Anchors.Created[0].Status = AnchorStatus.Failed;
                    h.Session.Tick();
                    Equal(RegistrationReason.AnchorFailed, h.Session.LastSnapshot.Reason);
                    Check(!h.Session.LastSnapshot.WorldFromWorkpiece.HasValue);
                }
            });
            tests.Add("Cancellation and obsolete completion", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Preview();
                    h.Session.Confirm(true, true, true);
                    var old = h.Anchors.Created[0];
                    h.Session.Cancel();
                    Check(old.Disposed);
                    h.Session.Start();
                    old.Status = AnchorStatus.Localized;
                    h.Session.Tick();
                    Equal(RegistrationState.Acquiring, h.Session.LastSnapshot.State);
                    Equal(2L, h.Session.LastSnapshot.Generation);
                    Check(!h.Session.LastSnapshot.IsValid);
                }
            });
            tests.Add("QR occlusion preserves anchor", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Tracker.Observations = Array.Empty<QrObservation>();
                    h.Clock.Now += 10;
                    h.Session.Tick();
                    Check(h.Session.LastSnapshot.IsValid);
                }
            });
            tests.Add("Anchor movement drives frozen offsets", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    var before = h.Session.LastSnapshot.WorldFromWorkpiece.Value;
                    h.Tracker.Observations = Array.Empty<QrObservation>();
                    var a = h.Anchors.Created[0];
                    a.Pose = RegistrationMath.Pose("World", "Anchor", a.Pose.Position + new Vec3(.01, 0, 0), a.Pose.Rotation);
                    h.Session.Tick();
                    Near(before.Position + new Vec3(.01, 0, 0), h.Session.LastSnapshot.WorldFromWorkpiece.Value.Position);
                }
            });
            tests.Add("Later QR never snaps registration", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Sample(h.Observation(pose: RegistrationMath.Pose("World", "Marker", h.Marker.Position + new Vec3(.1, 0, 0), h.Marker.Rotation)));
                    Equal(RegistrationReason.QrConflict, h.Session.LastSnapshot.Reason);
                    Check(!h.Session.LastSnapshot.WorldFromWorkpiece.HasValue);
                }
            });
            tests.Add("Later other part requires deliberate restart", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Sample(h.Observation("LW1:OTHER"));
                    Equal(RegistrationReason.QrConflict, h.Session.LastSnapshot.Reason);
                    h.Sample();
                    Equal(RegistrationState.Lost, h.Session.LastSnapshot.State);
                }
            });
            tests.Add("Anchor loss invalidates immediately", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Anchors.Created[0].Status = AnchorStatus.Lost;
                    Check(!h.Session.Read().IsValid);
                    Equal(RegistrationReason.AnchorLost, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Saved snapshots expire and carry session identity", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    var s = h.Session.LastSnapshot;
                    Check(s.IsUsableAt(h.Clock.Now, 1));
                    Check(!s.IsUsableAt(h.Clock.Now + 1, 1));
                    Check(!s.IsUsableAt(h.Clock.Now, 2));
                    Check(!s.IsUsableAt(double.NaN, 1));
                    Check(s.ConfirmedAt.HasValue && s.AnchorReceivedAt.HasValue);
                    using (var next = new Harness(Catalog()))
                        Check(s.SessionId != next.Session.LastSnapshot.SessionId);
                }
            });
            tests.Add("Obsolete anchor generation fails closed", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Anchors.Created[0].Generation--;
                    h.Session.Tick();
                    Equal(RegistrationReason.OriginChanged, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Localization never completing times out", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Preview();
                    h.Session.Confirm(true, true, true);
                    h.Anchors.Created[0].Status = AnchorStatus.Localizing;
                    h.Session.Tick();
                    Equal(RegistrationState.Anchoring, h.Session.LastSnapshot.State);
                    h.Clock.Now += 16;
                    h.Session.Tick();
                    Check(h.Anchors.Created[0].Disposed);
                    Equal(RegistrationReason.Timeout, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Angular instability is not averaged away", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample();
                    h.Clock.Now += .75;
                    h.Sample(h.Observation(pose: RegistrationMath.Pose("World", "Marker", h.Marker.Position, new Quat(0, 0, 0, 1))));
                    Equal(RegistrationReason.UnstablePose, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Frozen anchor does not follow subthreshold QR noise", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    var frozen = h.Session.LastSnapshot.WorldFromWorkpiece.Value;
                    h.Sample(h.Observation(pose: RegistrationMath.Pose("World", "Marker", h.Marker.Position + new Vec3(.002, 0, 0), h.Marker.Rotation)));
                    Check(h.Session.LastSnapshot.IsValid);
                    Near(frozen.Position, h.Session.LastSnapshot.WorldFromWorkpiece.Value.Position);
                }
            });
            tests.Add("High frequency observations can fill the time window", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    for (int i = 0; i < 230; i++)
                    {
                        h.Clock.Now += .01;
                        h.Sample();
                    }

                    Equal(RegistrationState.Preview, h.Session.LastSnapshot.State);
                    if (h.Session.LastSnapshot.ObservationCount > 32)
                        throw new Exception("Unbounded window");
                }
            });
            tests.Add("Outlier between retained samples resets stability", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample();
                    h.Clock.Now += .01;
                    h.Sample(h.Observation(pose: RegistrationMath.Pose("World", "Marker", h.Marker.Position + new Vec3(.01, 0, 0), h.Marker.Rotation)));
                    Equal(RegistrationReason.UnstablePose, h.Session.LastSnapshot.Reason);
                    Equal(0, h.Session.LastSnapshot.ObservationCount);
                }
            });
            tests.Add("Stale source capture cannot invalidate a localized anchor", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    var o = h.Observation("LW1:OTHER");
                    h.Sample(new QrObservation(o.CopyPayload(), o.TrackableId, o.Sequence, o.OriginGeneration, o.ReceivedAt, o.WorldFromMarker, o.WidthMetres, o.HeightMetres, o.DimensionConvention, o.ObserverPosition, h.Clock.Now - 10));
                    Equal(RegistrationState.Registered, h.Session.LastSnapshot.State);
                }
            });
            tests.Add("Stale conflicting QR is ignored after registration", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Sample(h.Observation("LW1:OTHER", age: 2));
                    Check(h.Session.LastSnapshot.IsValid);
                }
            });
            tests.Add("Missing physical marker plane cannot register", () =>
            {
                using (var h = new Harness(Catalog(true, d =>
                {
                    d.markers[0].physicalPlaneKnown = false;
                    d.markers[0].installationEvidence = "";
                })))
                {
                    h.Sample();
                    Equal(RegistrationReason.MarkerPlaneUnqualified, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Observer at extreme angle fails view gate", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(eye: h.Marker.TransformPoint(new Vec3(.5, 0, .01))));
                    Equal(RegistrationReason.InvalidView, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Reversed observation sequence cannot fill stability window", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Sample(h.Observation(seq: 3));
                    h.Clock.Now += .5;
                    h.Sample(h.Observation(seq: 2));
                    Equal(RegistrationReason.StaleObservation, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Stale anchor invalidates", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Anchors.Created[0].Age = 1;
                    h.Session.Tick();
                    Equal(RegistrationReason.AnchorLost, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Origin generation mismatch", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Tracker.Origin++;
                    h.Session.Tick();
                    Equal(RegistrationReason.OriginChanged, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Tracking loss", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Tracker.Tracking = false;
                    h.Session.Tick();
                    Equal(RegistrationReason.TrackingLost, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Required tracking stale", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Tracker.Age = 1;
                    h.Session.Tick();
                    Equal(RegistrationReason.TrackingLost, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Known rebolting creates new generation only on Start", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Session.Invalidate(RegistrationReason.AssemblyChanged);
                    Equal(RegistrationState.Lost, h.Session.LastSnapshot.State);
                    Equal(1L, h.Session.LastSnapshot.Generation);
                    h.Session.Start();
                    h.Preview();
                    Equal(2L, h.Session.LastSnapshot.Generation);
                }
            });
            tests.Add("Clock regression fails closed", () =>
            {
                using (var h = new Harness(Catalog()))
                {
                    h.Register();
                    h.Clock.Now = 0;
                    h.Session.Tick();
                    Equal(RegistrationReason.ClockInvalid, h.Session.LastSnapshot.Reason);
                }
            });
            tests.Add("Session disposal never reuses anchor", () =>
            {
                var h = new Harness(Catalog());
                h.Register();
                h.Dispose();
                Check(h.Anchors.Created[0].Disposed && h.Tracker.Disposed);
                Equal(RegistrationState.Disposed, h.Session.LastSnapshot.State);
                using (var next = new Harness(Catalog()))
                    Check(!next.Session.LastSnapshot.IsValid);
            });
            foreach (var code in new[]
            {
                RegistrationReason.Unsupported,
                RegistrationReason.PermissionDenied,
                RegistrationReason.ConfigurationPending,
                RegistrationReason.ConfigurationInvalid,
                RegistrationReason.ConventionUnqualified
            }

            )
            {
                var c = code;
                tests.Add("Platform gate " + c, () =>
                {
                    using (var h = new Harness(Catalog()))
                    {
                        if (c == RegistrationReason.Unsupported)
                            h.Tracker.Supported = false;
                        if (c == RegistrationReason.PermissionDenied)
                            h.Tracker.Permission = PermissionState.Denied;
                        if (c == RegistrationReason.ConfigurationPending)
                            h.Tracker.Applied = false;
                        if (c == RegistrationReason.ConfigurationInvalid)
                            h.Tracker.Config = false;
                        if (c == RegistrationReason.ConventionUnqualified)
                            h.Tracker.Convention = false;
                        h.Sample();
                        Equal(c, h.Session.LastSnapshot.Reason);
                        Check(!h.Session.LastSnapshot.IsValid);
                    }
                });
            }

            return tests;
        }
    }
}
