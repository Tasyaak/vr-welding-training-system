using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WeldingTrainer.Content.Spatial.Tests
{
    // Same cases run in dependency-free .NET and Unity EditMode against their actual serializers.
    public sealed class SpatialTestCases
    {
        private readonly string root, directory;
        private readonly ISpatialJson codec;
        private readonly Func<object, string> write;
        private const string Identity = "647e8ce51ce0f02cda545677b969d484517b526841dc4e88a75a8829103f1117";
        public SpatialTestCases(string root, ISpatialJson codec, Func<object, string> write)
        {
            this.root = root;
            this.codec = codec;
            this.write = write;
            directory = Path.Combine(root, "unity/Assets/Trainer/Content/Spatial/Generated");
        }

        private CatalogData Catalog() => codec.Read<CatalogData>(File.ReadAllText(Path.Combine(directory, "supplied.spatial")));
        private SpatialCatalogSnapshot Load(string json = null, Func<string, byte[]> source = null, Func<string, byte[]> artifact = null) => SpatialCatalogSnapshot.Load(json ?? File.ReadAllText(Path.Combine(directory, "supplied.spatial")), codec, artifact ?? (name => File.ReadAllBytes(Path.Combine(directory, name))), source ?? (name => File.ReadAllBytes(Path.Combine(root, name))));
        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void Near(Vec3 actual, Vec3 expected, double tolerance = 1e-8) => Check((actual - expected).Length < tolerance, $"Expected ({expected.x},{expected.y},{expected.z}); got ({actual.x},{actual.y},{actual.z})");
        private static void Reject(Action action, string contains)
        {
            try
            {
                action();
            }
            catch (SpatialContentException e)
            {
                Check(e.Message.Contains(contains), "Unexpected diagnostic: " + e.Message);
                return;
            }

            throw new Exception("Invalid content was accepted: " + contains);
        }

        private static PoseData Pose(string destination, string source, Vec3 position = default, Quat? rotation = null) => new PoseData
        {
            destination = destination,
            source = source,
            position = position,
            rotation = rotation ?? new Quat(0, 0, 0, 1),
            scale = 1
        };
        private void BadCatalog(Action<CatalogData> mutate, string reason)
        {
            var c = Catalog();
            mutate(c);
            Reject(() => SpatialValidation.Catalog(c), reason);
        }

        private static SurfaceData Triangle(string id = "surface") => new SurfaceData
        {
            id = id,
            a = new Vec3(0, 0, 0),
            b = new Vec3(1, 0, 0),
            c = new Vec3(0, 1, 0),
            normal = new Vec3(0, 0, 1)
        };
        private static GeometryData Synthetic()
        {
            return new GeometryData
            {
                schemaVersion = 1,
                id = "synthetic",
                revision = 1,
                frame = "Workpiece",
                units = "m",
                sourceSha256 = Identity,
                semanticStatus = "Approved",
                surfaces = new[]
                {
                    Triangle()
                },
                seams = new[]
                {
                    new SeamData
                    {
                        id = "seam-1",
                        authoringEvidence = "Synthetic directed selection",
                        surfaceIds = new[]
                        {
                            "surface"
                        },
                        points = new[]
                        {
                            new Vec3(.1, .1, 0),
                            new Vec3(.7, .1, 0)
                        },
                        arcLengthsMetres = new[]
                        {
                            0,
                            .6
                        }
                    },
                    new SeamData
                    {
                        id = "seam-2",
                        authoringEvidence = "Synthetic reverse selection",
                        surfaceIds = new[]
                        {
                            "surface"
                        },
                        points = new[]
                        {
                            new Vec3(.1, .7, 0),
                            new Vec3(.1, .2, 0)
                        },
                        arcLengthsMetres = new[]
                        {
                            0,
                            .5
                        }
                    }
                },
                cleaningRegions = new[]
                {
                    new CleaningData
                    {
                        id = "pre",
                        phase = "PreWeld",
                        authoringEvidence = "Synthetic pre-clean mask",
                        surfaceIds = new[]
                        {
                            "surface"
                        },
                        triangles = new[]
                        {
                            Triangle("pre-triangle")
                        }
                    },
                    new CleaningData
                    {
                        id = "post",
                        phase = "PostWeld",
                        authoringEvidence = "Synthetic post-clean mask",
                        surfaceIds = new[]
                        {
                            "surface"
                        },
                        triangles = new[]
                        {
                            Triangle("post-triangle")
                        }
                    }
                }
            };
        }

        private static void BadGeometry(Action<GeometryData> mutate, string reason)
        {
            var g = Synthetic();
            mutate(g);
            Reject(() => SpatialValidation.Geometry(g), reason);
        }

        public Dictionary<string, Action> Cases() => new Dictionary<string, Action>
        {
            ["production print alternatives retain known specifications and no selection"] = () =>
            {
                var b = Load().ResolveForPreview("PART-001");
                var m = b.Marker;
                Check(m.PrintCandidates.Count == 2 && m.SelectedPrintCandidateId == null, "two unselected alternatives");
                Check(b.Qualification == "Unqualified" && !b.ReadyForScoredRegistration, "false qualification");
                foreach (var p in m.PrintCandidates)
                {
                    Check(p.Payload == "LW1:PART-001" && p.SchemaVersion == 1 && p.Revision == 1, "candidate identity");
                    Check(p.LabelWidthMetres == .09 && p.LabelHeightMetres == .09 && p.LabelThicknessMetres == .0001, "physical label dimensions");
                    Check(p.SymbolCentered && p.SymbolQuietZoneConvention == "ExcludesQuietZone" && p.ArtworkProvenance == "NotSupplied", "artwork/convention");
                    double size = p.Id == "QR-PRINT-A" ? .053 : p.Id == "QR-PRINT-B" ? .063 : 0;
                    Check(size > 0 && p.SymbolWidthMetres == size && p.SymbolHeightMetres == size, "symbol dimensions");
                }

                var mutable = Catalog();
                var frozen = Load(write(mutable));
                mutable.markers[0].printCandidates[0].labelWidthMetres = 99;
                Check(frozen.ResolveForPreview("PART-001").Marker.PrintCandidates[0].LabelWidthMetres == .09, "candidate snapshot mutable");
                Check(((IList<PrintCandidateSnapshot>)m.PrintCandidates).IsReadOnly, "mutable collection");
            },
            ["invalid print alternatives and false selection rejected"] = () =>
            {
                BadCatalog(c => c.markers[0].printCandidates[1].id = "QR-PRINT-A", "duplicate ID");
                BadCatalog(c => c.markers[0].printCandidates[0].payload = "LW1:PART-999", "bound part");
                BadCatalog(c => c.markers[0].printCandidates[0].symbolWidthMetres = .09, "margin");
                BadCatalog(c => c.markers[0].printCandidates[0].labelThicknessMetres = double.NaN, "positive");
                BadCatalog(c => c.markers[0].printCandidates[0].symbolCentered = false, "centered");
                BadCatalog(c => c.markers[0].printCandidates[0].symbolQuietZoneConvention = "IncludesQuietZone", "exclude");
                BadCatalog(c => c.markers[0].printCandidates[0].artworkProvenance = "Invented", "provenance");
                BadCatalog(c => c.markers[0].printCandidates[0].labelWidthMetres = .1, "beyond");
                BadCatalog(c => c.markers[0].selectedPrintCandidateId = "UNKNOWN", "not found");
                BadCatalog(c => c.markers[0].selectedPrintCandidateId = "QR-PRINT-A", "metadata must match");
            },
            ["selection alone does not qualify plane and installed white label must fit"] = () =>
            {
                var c = Catalog();
                var m = c.markers[0];
                m.selectedPrintCandidateId = "QR-PRINT-A";
                m.dimensionsKnown = true;
                m.widthMetres = m.heightMetres = .053;
                m.labelThicknessMetres = .0001;
                m.quietZoneConvention = "ExcludesQuietZone";
                var b = Load(write(c)).ResolveForPreview("PART-001");
                Check(!b.ReadyForScoredRegistration && b.ScoredRegistrationBlockers.Count == 2, "selection implied qualification");
                Near(b.FixtureFromMarker.Position, new Vec3(-.105, .0076, .105));
                m.physicalPlaneKnown = true;
                m.installationEvidence = "TEST ONLY";
                c.bindings[0].fixtureFromMarker.position.y += .0001;
                c.bindings[0].fixtureFromMarker.position.x += .001;
                Reject(() => SpatialValidation.Catalog(c), "beyond");
            },
            ["production directed seam and finite PreWeld PostWeld L bands"] = () =>
            {
                var g = Load().ResolveForPreview("PART-001").WorkpieceGeometry;
                Check(g.SemanticStatus == "Approved" && g.Seams.Count == 1 && g.CleaningRegions.Count == 2, "production semantics missing");
                var seam = g.Seams[0];
                var start = new Vec3(-.09446746641944131, .0155, -.044);
                var end = new Vec3(.09027822178886747, .0155, -.044);
                Check(seam.Points.Count == 2 && seam.AuthoringEvidence.Contains("project owner"), "selection evidence");
                Near(seam.Points[0], start, 1e-14);
                Near(seam.Points[1], end, 1e-14);
                Check(Math.Abs(seam.ArcLengthsMetres[1] - (end.x - start.x)) < 1e-14, "directed length");
                Check(g.Surfaces.Single(s => s.Id == seam.SurfaceIds[0]).SourceFace == 4, "horizontal support");
                Check(g.Surfaces.Single(s => s.Id == seam.AdjacentSurfaceIds[0]).SourceFace == 13, "upright support");
                Check(g.CleaningRegions.Select(r => r.Phase).OrderBy(x => x).SequenceEqual(new[] { "PostWeld", "PreWeld" }), "distinct pre/post");
                foreach (var region in g.CleaningRegions)
                {
                    foreach (int face in new[]
                    {
                        4,
                        13
                    }

                    )
                    {
                        var triangles = region.Triangles.Where(t => t.SourceFace == face).ToArray();
                        double area = triangles.Sum(t => Vec3.Cross(t.B - t.A, t.C - t.A).Length / 2);
                        Check(Math.Abs(area - (end.x - start.x) * .015) < 1e-10, "full finite band area");
                        foreach (var t in triangles)
                        {
                            Near(t.Normal, face == 4 ? new Vec3(0, 1, 0) : new Vec3(0, 0, 1));
                            foreach (var p in new[]
                            {
                                t.A,
                                t.B,
                                t.C
                            }

                            )
                            {
                                Check(p.x >= start.x - 1e-10 && p.x <= end.x + 1e-10, "band past seam");
                                Check(face == 4 ? Math.Abs(p.y - .0155) < 1e-10 && p.z >= -.044 - 1e-10 && p.z <= -.029 + 1e-10 : Math.Abs(p.z + .044) < 1e-10 && p.y >= .0155 - 1e-10 && p.y <= .0305 + 1e-10, "band leaves visible L region");
                            }
                        }
                    }

                    Check(region.Triangles.All(t => t.SourceFace == 4 || t.SourceFace == 13), "opposite face included");
                }
            },
            ["joint adjacent support must exist and be nonparallel"] = () =>
            {
                var g = codec.Read<GeometryData>(File.ReadAllText(Path.Combine(directory, "welded_part.geometry.json")));
                g.seams[0].adjacentSurfaceIds[0] = "unknown";
                Reject(() => SpatialValidation.Geometry(g), "adjacent");
                g.seams[0].adjacentSurfaceIds[0] = g.seams[0].surfaceIds[0];
                Reject(() => SpatialValidation.Geometry(g), "distinct adjacent");
            },
            ["marker region area rejects gaps between corner probes"] = () =>
            {
                var r = new MountRegionData
                {
                    id = "region",
                    widthMetres = 1,
                    heightMetres = 1,
                    sourceFace = 0,
                    outwardNormal = new Vec3(0, 0, 1),
                    fixtureFromRegion = Pose("Fixture", "MarkerMountRegion", new Vec3(.5, .5, 0))
                };
                var surfaces = new List<SurfaceData>();
                foreach (var interval in new[]
                {
                    new[]
                    {
                        0.0,
                        .6
                    },
                    new[]
                    {
                        .7,
                        1.0
                    }
                }

                )
                {
                    double a = interval[0], b = interval[1];
                    surfaces.Add(new SurfaceData { a = new Vec3(a, 0, 0), b = new Vec3(b, 0, 0), c = new Vec3(b, 1, 0), normal = new Vec3(0, 0, 1) });
                    surfaces.Add(new SurfaceData { a = new Vec3(a, 0, 0), b = new Vec3(b, 1, 0), c = new Vec3(a, 1, 0), normal = new Vec3(0, 0, 1) });
                }

                Reject(() => SpatialValidation.MountRegionCoverage(surfaces, r), "complete area");
            },
            ["default rigid pose and nonfinite transformed points reject"] = () =>
            {
                Reject(() => default(RigidPose).TransformPoint(new Vec3(1, 2, 3)), "uninitialized");
                Reject(() => new RigidPose(Pose("A", "B")).TransformPoint(new Vec3(double.NaN, 0, 0)), "finite");
            },
            ["qualified synthetic content can resolve without production bypass"] = () =>
            {
                var c = Catalog();
                var m = c.markers[0];
                m.printCandidates = Array.Empty<PrintCandidateData>();
                var b = c.bindings[0];
                m.dimensionsKnown = true;
                m.widthMetres = .05;
                m.heightMetres = .05;
                m.quietZoneConvention = "IncludesQuietZone";
                m.physicalPlaneKnown = true;
                m.labelThicknessMetres = .0002;
                m.installationEvidence = "TEST-ONLY installation";
                b.fixtureFromMarker.position = new Vec3(-.105, .0078, .105);
                b.qualification = "Qualified";
                b.qualificationEvidence = "TEST-ONLY approval";
                var g = Synthetic();
                g.id = c.workpieces[0].geometryId;
                var v = new VisualData
                {
                    schemaVersion = 1,
                    frame = "Workpiece",
                    units = "m",
                    vertices = new[]
                    {
                        g.surfaces[0].a,
                        g.surfaces[0].b,
                        g.surfaces[0].c
                    },
                    triangles = new[]
                    {
                        0,
                        1,
                        2
                    }
                };
                var imported = c.imports.Single(x => x.sourceId == c.workpieces[0].sourceId);
                var data = new Dictionary<string, byte[]>
                {
                    [imported.geometryFile] = Encoding.UTF8.GetBytes(write(g)),
                    [imported.visualFile] = Encoding.UTF8.GetBytes(write(v))
                };
                imported.geometrySha256 = SpatialCatalogSnapshot.Sha256(data[imported.geometryFile]);
                imported.visualSha256 = SpatialCatalogSnapshot.Sha256(data[imported.visualFile]);
                var s = Load(write(c), artifact: name => data.TryGetValue(name, out var bytes) ? bytes : File.ReadAllBytes(Path.Combine(directory, name)));
                Check(s.ResolveForScoredRegistration("PART-001").ReadyForScoredRegistration, "valid synthetic qualification rejected");
            },
            ["source mismatch remains rejected even with recomputed artifact hash"] = () =>
            {
                var c = Catalog();
                var imported = c.imports[1];
                var g = codec.Read<GeometryData>(File.ReadAllText(Path.Combine(directory, imported.geometryFile)));
                g.sourceSha256 = new string ('0', 64);
                var bytes = Encoding.UTF8.GetBytes(write(g));
                imported.geometrySha256 = SpatialCatalogSnapshot.Sha256(bytes);
                Reject(() => Load(write(c), artifact: name => name == imported.geometryFile ? bytes : File.ReadAllBytes(Path.Combine(directory, name))), "source identity/frame mismatch");
            },
            ["bake pipeline input tampering rejected"] = () => Reject(() => Load(source: name => name.EndsWith("bake.py") ? new byte[] { 0 } : File.ReadAllBytes(Path.Combine(root, name))), "bake input identity mismatch"),
            ["malformed visual index and mismatched proxy rejected"] = () =>
            {
                var g = Synthetic();
                var v = new VisualData
                {
                    schemaVersion = 1,
                    frame = "Workpiece",
                    units = "m",
                    vertices = new[]
                    {
                        g.surfaces[0].a,
                        g.surfaces[0].b,
                        g.surfaces[0].c
                    },
                    triangles = new[]
                    {
                        0,
                        1,
                        3
                    }
                };
                Reject(() => SpatialValidation.Visual(v, g), "index out of bounds");
                v.triangles[2] = 2;
                v.vertices[2] = new Vec3(0, 2, 0);
                Reject(() => SpatialValidation.Visual(v, g), "coordinate or winding mismatch");
            },
            ["missing arrays and null references fail clearly"] = () =>
            {
                BadCatalog(c => c.bindings = null, "array missing");
                BadCatalog(c => c.workpieces[0].sourceId = null, "identifier");
                BadGeometry(g => g.surfaces = null, "array missing");
                BadGeometry(g => g.seams[0].surfaceIds[0] = "UNKNOWN", "supporting surface");
            },
            ["actual sources, closed solids, visual/proxy hashes and catalog load"] = () =>
            {
                var s = Load();
                Check(s.Bindings.Count() == 1, "binding count");
                Check(s.ResolveForPreview("PART-001").WorkpieceGeometry.Surfaces.Count > 100, "actual geometry missing");
            },
            ["actual nominal identity and CAD QR region"] = () =>
            {
                var b = Load().ResolveForPreview("PART-001");
                Near(b.FixtureFromWorkpiece.TransformPoint(new Vec3(.1, .2, .3)), new Vec3(.1, .2, .3));
                var r = b.MarkerMountRegion;
                Check(r.WidthMetres == .09 && r.HeightMetres == .09, "90 mm recess");
                Near(r.FixtureFromRegion.Position, new Vec3(-.105, .0076, .105));
                Near(r.FixtureFromRegion.TransformDirection(new Vec3(1, 0, 0)), new Vec3(1, 0, 0));
                Near(r.FixtureFromRegion.TransformDirection(new Vec3(0, 1, 0)), new Vec3(0, 0, -1));
                Near(r.OutwardNormal, new Vec3(0, 1, 0));
                Near(r.FixtureFromRegion.TransformPoint(new Vec3(-.045, -.045, 0)), new Vec3(-.15, .0076, .15));
            },
            ["actual part bounds remain metres"] = () =>
            {
                var points = Load().ResolveForPreview("PART-001").WorkpieceGeometry.Surfaces.SelectMany(s => new[] { s.A, s.B, s.C }).ToArray();
                Near(new Vec3(points.Min(p => p.x), points.Min(p => p.y), points.Min(p => p.z)), new Vec3(-.0944674664, .008, -.075547106));
                Near(new Vec3(points.Max(p => p.x), points.Max(p => p.y), points.Max(p => p.z)), new Vec3(.0902782218, .0655, .014452894));
            },
            ["actual unqualified content rejects scored registration"] = () =>
            {
                var s = Load();
                var b = s.ResolveForPreview("PART-001");
                Check(b.Marker.WidthMetres == null && b.Marker.LabelThicknessMetres == null, "unknown must be nullable");
                Check(b.ScoredRegistrationBlockers.Count == 3, "missing blockers");
                Reject(() => s.ResolveForScoredRegistration("PART-001"), "StationUnqualified");
                Check(s.ToolSetups[0].ToolFromTip == null, "unknown tool offsets exposed");
            },
            ["unknown and malformed part identifiers"] = () =>
            {
                var s = Load();
                Reject(() => s.ResolveForPreview("PART-999"), "unknown");
                Reject(() => s.ResolveForPreview("LW1:PART-001"), "identifier");
                Reject(() => s.ResolveForPreview("../PART-001"), "identifier");
            },
            ["contract v1 translation example"] = () =>
            {
                var result = new RigidPose(Pose("World", "Marker", new Vec3(1, 2, 3))) * new RigidPose(Pose("Fixture", "Marker", new Vec3(.1, 0, 0))).Inverse() * new RigidPose(Pose("Fixture", "Workpiece", new Vec3(0, .008, 0)));
                Near(result.Position, new Vec3(.9, 2.008, 3));
            },
            ["nonidentity composition and inverse"] = () =>
            {
                double h = Math.Sqrt(.5);
                var worldMarker = new RigidPose(Pose("World", "Marker", new Vec3(1, 2, 3), new Quat(0, 0, h, h)));
                var fixtureMarker = new RigidPose(Pose("Fixture", "Marker", new Vec3(.1, .2, .3), new Quat(h, 0, 0, h)));
                var fixturePart = new RigidPose(Pose("Fixture", "Workpiece", new Vec3(.4, .5, .6), new Quat(0, h, 0, h)));
                var actual = worldMarker * fixtureMarker.Inverse() * fixturePart;
                Near(actual.Position, new Vec3(.7, 2.3, 2.7));
                var p = new Vec3(.02, .03, .04);
                Near(actual.TransformPoint(p), worldMarker.TransformPoint(fixtureMarker.Inverse().TransformPoint(fixturePart.TransformPoint(p))));
                Near(actual.Inverse().TransformPoint(actual.TransformPoint(p)), p);
                Near(new RigidPose(Pose("A", "B", default, new Quat(0, 0, h, h))).TransformPoint(new Vec3(1, 0, 0)), new Vec3(0, 1, 0));
            },
            ["mismatched composition frames"] = () =>
            {
                try
                {
                    var invalid = new RigidPose(Pose("World", "Marker")) * new RigidPose(Pose("Fixture", "Workpiece"));
                }
                catch (ArgumentException)
                {
                    return;
                }

                throw new Exception("frame mismatch accepted");
            },
            ["nonunit or mirrored scale rejected"] = () =>
            {
                foreach (double scale in new[]
                {
                    .001,
                    1000,
                    -1,
                    0,
                    2
                }

                )
                    BadCatalog(c => c.bindings[0].fixtureFromWorkpiece.scale = scale, "scale");
            },
            ["unnormalized quaternion rejected"] = () => BadCatalog(c => c.bindings[0].fixtureFromMarker.rotation = new Quat(0, 0, 0, 2), "normalized"),
            ["nonfinite pose rejected"] = () => BadCatalog(c => c.bindings[0].fixtureFromMarker.position = new Vec3(double.NaN, 0, 0), "finite"),
            ["wrong transform direction rejected"] = () => BadCatalog(c => c.bindings[0].fixtureFromWorkpiece.source = "Fixture", "direction"),
            ["duplicate active part binding rejected"] = () => BadCatalog(c =>
            {
                var b = codec.Read<BindingData>(write(c.bindings[0]));
                b.id = "ASSEMBLY-002";
                c.bindings = new[]
                {
                    c.bindings[0],
                    b
                };
            }, "duplicate active"),
            ["duplicate definition and bad ID rejected"] = () =>
            {
                BadCatalog(c => c.fixtures = new[] { c.fixtures[0], c.fixtures[0] }, "duplicate ID");
                BadCatalog(c => c.workpieces[0].id = " bad ", "identifier");
            },
            ["stale revisions and missing binding references rejected"] = () =>
            {
                BadCatalog(c => c.bindings[0].partRevision = 99, "stale");
                BadCatalog(c => c.bindings[0].mountRegionRevision = 2, "stale");
                BadCatalog(c => c.bindings[0].fixtureId = "unknown", "unknown");
            },
            ["source hash syntax rejected"] = () => BadCatalog(c => c.sources[0].sha256 = "not-a-hash", "SHA-256"),
            ["source identity mismatch rejected"] = () => Reject(() => Load(source: name => new byte[] { 1, 2, 3 }), "original STEP identity mismatch"),
            ["derived content tampering rejected"] = () => Reject(() => Load(artifact: name => Encoding.UTF8.GetBytes("{}")), "hash mismatch"),
            ["unsafe provenance paths rejected"] = () =>
            {
                BadCatalog(c => c.sources[0].path = "../outside.step", "relative");
                BadCatalog(c => c.imports[0].geometryFile = "../geometry.json", "filename");
            },
            ["double units conversion and mirrored axis rejected"] = () =>
            {
                BadCatalog(c => c.imports[0].millimetresToMetres = .000001, "exactly once");
                BadCatalog(c => c.imports[0].axisMap = "x,y,-z", "axis/pivot");
                BadCatalog(c => c.units = "mm", "metres");
            },
            ["marker dimension and unknown sentinel rejected"] = () =>
            {
                BadCatalog(c => c.markers[0].widthMetres = .09, "sentinel");
                BadCatalog(c =>
                {
                    c.markers[0].dimensionsKnown = true;
                    c.markers[0].widthMetres = -.09;
                }, "positive");
            },
            ["false physical qualification rejected"] = () => BadCatalog(c => c.bindings[0].qualification = "Qualified", "measured"),
            ["nominal marker rotation and plane rejected"] = () =>
            {
                BadCatalog(c => c.bindings[0].fixtureFromMarker.position = new Vec3(-.105, .008, .105), "nominal");
                BadCatalog(c => c.bindings[0].fixtureFromMarker.rotation = new Quat(0, 0, 0, 1), "normal");
            },
            ["oversized printed QR rejected"] = () => BadCatalog(c =>
            {
                c.markers[0].dimensionsKnown = true;
                c.markers[0].printCandidates = Array.Empty<PrintCandidateData>();
                c.markers[0].widthMetres = .1;
                c.markers[0].heightMetres = .1;
                c.markers[0].quietZoneConvention = "IncludesQuietZone";
            }, "beyond"),
            ["wrong CAD marker face selection rejected"] = () =>
            {
                var c = Catalog();
                c.fixtures[0].markerMountRegion.sourceFace = 7;
                Reject(() => Load(write(c)), "not supported");
            },
            ["synthetic multiple directed seams and pre/post masks"] = () => SpatialValidation.Geometry(Synthetic()),
            ["seam zero length, direction and arc length rejected"] = () =>
            {
                BadGeometry(g => g.seams[0].points[1] = g.seams[0].points[0], "zero length");
                BadGeometry(g => g.seams[0].arcLengthsMetres[1] = -.6, "arc length");
                BadGeometry(g => g.seams[0].arcLengthsMetres[1] = .7, "arc length");
            },
            ["seam leaves finite surface rejected"] = () => BadGeometry(g =>
            {
                g.seams[0].points[1] = new Vec3(1, .1, 0);
                g.seams[0].arcLengthsMetres[1] = .9;
            }, "leaves finite"),
            ["unapproved seam selection rejected"] = () => BadGeometry(g => g.seams[0].authoringEvidence = "", "evidence"),
            ["missing approved semantics rejected"] = () => BadGeometry(g => g.seams = Array.Empty<SeamData>(), "requires seams"),
            ["normal inversion, degeneracy, nonfinite and nonunit rejected"] = () =>
            {
                BadGeometry(g => g.surfaces[0].normal = new Vec3(0, 0, -1), "inverted");
                BadGeometry(g => g.surfaces[0].normal = new Vec3(0, 0, 2), "unit normal");
                BadGeometry(g => g.surfaces[0].c = g.surfaces[0].a, "degenerate");
                BadGeometry(g => g.surfaces[0].a = new Vec3(double.PositiveInfinity, 0, 0), "finite");
            },
            ["cleaning containment and phase rejected"] = () =>
            {
                BadGeometry(g => g.cleaningRegions[0].triangles[0].c = new Vec3(0, 2, 0), "leaves finite");
                BadGeometry(g => g.cleaningRegions[0].phase = "Weld", "pre/post");
            },
            ["open shell rejected when declared solid"] = () => BadGeometry(g => g.closedSolid = true, "open/nonmanifold"),
            ["actual inward closed solid rejected"] = () =>
            {
                var g = codec.Read<GeometryData>(File.ReadAllText(Path.Combine(directory, "welded_part.geometry.json")));
                foreach (var s in g.surfaces)
                {
                    var b = s.b;
                    s.b = s.c;
                    s.c = b;
                    s.normal = s.normal * -1;
                }

                Reject(() => SpatialValidation.Geometry(g), "inward");
            },
            ["snapshot defensive copies and lossless export"] = () =>
            {
                var g = Synthetic();
                var json = write(g);
                var frozen = GeometrySnapshot.FromJson(json, codec);
                g.seams[0].points[0] = new Vec3(99, 99, 99);
                Near(frozen.Seams[0].Points[0], new Vec3(.1, .1, 0));
                var list = (IList<Vec3>)frozen.Seams[0].Points;
                try
                {
                    list[0] = new Vec3(5, 5, 5);
                }
                catch (NotSupportedException)
                {
                    var a = Load();
                    var b = SpatialCatalogSnapshot.Load(a.CatalogJson, codec, name => Encoding.UTF8.GetBytes(a.ArtifactJson[name]));
                    Check(a.ContentHash == b.ContentHash, "export hash changed");
                    return;
                }

                throw new Exception("mutable snapshot");
            },
            ["serialization preserves doubles, frames and unknown flags"] = () =>
            {
                var c = Catalog();
                var copy = codec.Read<CatalogData>(write(c));
                SpatialValidation.Catalog(copy);
                Check(copy.markers[0].dimensionsKnown == false, "unknown became known");
                Near(copy.bindings[0].fixtureFromMarker.position, c.bindings[0].fixtureFromMarker.position);
                Load(write(copy));
            }
        };
    }
}
