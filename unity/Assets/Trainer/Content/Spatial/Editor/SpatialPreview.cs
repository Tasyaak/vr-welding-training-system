using System.Linq;
using UnityEditor;
using UnityEngine;
using WeldingTrainer.Content.Spatial.Unity;

namespace WeldingTrainer.Content.Spatial.Editor
{
    // Editor-only standalone inspection. No scene mutation, tracking or training state.
    public sealed class SpatialPreview : EditorWindow
    {
        private SpatialCatalogAsset asset;
        private SpatialCatalogSnapshot snapshot;
        private PreviewRenderUtility preview;
        private Material material;
        private Vector2 orbit = Vector2.zero;
        private bool topView = true;
        private string diagnostic;
        [MenuItem("Trainer/Spatial/Inspect supplied CAD assembly")]
        public static void Open()
        {
            var window = GetWindow<SpatialPreview>("Spatial CAD preview");
            window.asset = AssetDatabase.LoadAssetAtPath<SpatialCatalogAsset>("Assets/Trainer/Content/Spatial/Generated/supplied.spatial");
            window.Reload();
        }

        private void Reload()
        {
            try
            {
                snapshot = asset == null ? null : asset.Freeze();
                diagnostic = snapshot == null ? "Select an imported spatial catalog." : string.Join("\n", snapshot.Bindings.SelectMany(x => x.ScoredRegistrationBlockers));
            }
            catch (System.Exception error)
            {
                snapshot = null;
                diagnostic = error.Message;
            }

            Repaint();
        }

        private void OnEnable()
        {
            preview = new PreviewRenderUtility();
            preview.camera.nearClipPlane = .001f;
            preview.camera.farClipPlane = 10;
            material = new Material(Shader.Find("Hidden/Internal-Colored"));
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            material.SetInt("_ZWrite", 1);
        }

        private void OnDisable()
        {
            preview?.Cleanup();
            if (material != null)
                DestroyImmediate(material);
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            asset = (SpatialCatalogAsset)EditorGUILayout.ObjectField("Catalog", asset, typeof(SpatialCatalogAsset), false);
            if (EditorGUI.EndChangeCheck())
                Reload();
            EditorGUILayout.HelpBox(diagnostic ?? "Open a catalog", MessageType.Info);
            EditorGUILayout.LabelField("CAD metres • drag to orbit • cyan = QR recess floor, not printed QR");
            if (GUILayout.Button("Reset: QR side, holes north"))
            {
                orbit = Vector2.zero;
                topView = false;
                Repaint();
            }

            if (GUILayout.Button("Exact top: +Y view, -Z north"))
            {
                orbit = Vector2.zero;
                topView = true;
                Repaint();
            }

            var rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (snapshot == null)
                return;
            if (Event.current.type == EventType.MouseDrag && rect.Contains(Event.current.mousePosition))
            {
                orbit += Event.current.delta;
                topView = false;
                Event.current.Use();
                Repaint();
            }

            if (Event.current.type != EventType.Repaint)
                return;
            preview.BeginPreview(rect, GUIStyle.none);
            DrawAssembly();
            CadPreviewView.Render(preview.camera);
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
            ClearTemporaryMeshes();
        }

        private void DrawAssembly()
        {
            var camera = preview.camera;
            camera.fieldOfView = 45;
            var eye = topView ? new Vector3(0, .6f, 0) : Quaternion.Euler(orbit.y, orbit.x, 0) * new Vector3(0, .6f, .13f);
            CadPreviewView.Configure(camera, eye, new Vector3(0, .02f, 0), topView ? Vector3.back : Vector3.up);
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = new Color(.08f, .09f, .12f);
            var binding = snapshot.Bindings.First();
            var definitions = new UnitySpatialJson().Read<CatalogData>(snapshot.CatalogJson);
            var partSource = definitions.workpieces.Single(x => x.id == binding.PartId).sourceId;
            var fixtureSource = definitions.fixtures.Single(x => x.id == binding.FixtureId).sourceId;
            foreach (var mesh in asset.CopyVisualMeshes())
            {
                if (mesh.name != partSource && mesh.name != fixtureSource)
                    continue;
                var color = mesh.name == fixtureSource ? new Color(.45f, .5f, .58f) : new Color(.88f, .65f, .28f);
                var light = new Vector3(-.3f, .8f, -.5f).normalized;
                var colors = mesh.normals.Select(n => color * (.35f + .65f * Mathf.Max(0, Vector3.Dot(n, light)))).ToArray();
                for (int i = 0; i < colors.Length; i++)
                    colors[i].a = 1;
                var copy = Instantiate(mesh);
                copy.colors = colors;
                var pose = binding.FixtureFromWorkpiece;
                var p = pose.Position;
                var q = pose.Rotation;
                var matrix = mesh.name == partSource ? Matrix4x4.TRS(new Vector3((float)p.x, (float)p.y, (float)p.z), new Quaternion((float)q.x, (float)q.y, (float)q.z, (float)q.w), Vector3.one) : Matrix4x4.identity;
                preview.DrawMesh(copy, matrix, material, 0);
                temporary.Add(copy);
            }

            // Draw exact authored mount-region perimeter above the floor by a display-only 0.05 mm offset.
            var r = binding.MarkerMountRegion;
            var corners = new[]
            {
                new Vec3(-r.WidthMetres / 2, -r.HeightMetres / 2, .00005),
                new Vec3(r.WidthMetres / 2, -r.HeightMetres / 2, .00005),
                new Vec3(r.WidthMetres / 2, r.HeightMetres / 2, .00005),
                new Vec3(-r.WidthMetres / 2, r.HeightMetres / 2, .00005)
            };
            var overlay = new Mesh();
            overlay.vertices = corners.Select(p => r.FixtureFromRegion.TransformPoint(p)).Select(p => new Vector3((float)p.x, (float)p.y, (float)p.z)).ToArray();
            overlay.colors = Enumerable.Repeat(Color.cyan, 4).ToArray();
            overlay.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, MeshTopology.Lines, 0);
            preview.DrawMesh(overlay, Matrix4x4.identity, material, 0);
            temporary.Add(overlay);
            // Semantic overlays are display-only offsets; authored triangles remain on CAD.
            var posePart = binding.FixtureFromWorkpiece;
            foreach (var region in binding.WorkpieceGeometry.CleaningRegions.Where(x => x.Phase == "PreWeld"))
            {
                var vertices = region.Triangles.SelectMany(t => new[] { t.A + t.Normal * .00008, t.B + t.Normal * .00008, t.C + t.Normal * .00008 }).Select(posePart.TransformPoint).Select(ToUnity).ToArray();
                var mask = new Mesh
                {
                    vertices = vertices,
                    colors = Enumerable.Repeat(new Color(.2f, .65f, .35f, 1), vertices.Length).ToArray()
                };
                mask.SetIndices(Enumerable.Range(0, vertices.Length).ToArray(), MeshTopology.Triangles, 0);
                preview.DrawMesh(mask, Matrix4x4.identity, material, 0);
                temporary.Add(mask);
            }

            foreach (var seam in binding.WorkpieceGeometry.Seams)
            {
                var vertices = seam.Points.Select(p => posePart.TransformPoint(p + new Vec3(0, .0002, .0002))).Select(ToUnity).ToArray();
                var line = new Mesh
                {
                    vertices = vertices,
                    colors = Enumerable.Repeat(Color.yellow, vertices.Length).ToArray()
                };
                line.SetIndices(Enumerable.Range(0, vertices.Length - 1).SelectMany(i => new[] { i, i + 1 }).ToArray(), MeshTopology.Lines, 0);
                preview.DrawMesh(line, Matrix4x4.identity, material, 0);
                temporary.Add(line);
            }
        }

        private static Vector3 ToUnity(Vec3 p) => new Vector3((float)p.x, (float)p.y, (float)p.z);
        private void ClearTemporaryMeshes()
        {
            foreach (var mesh in temporary)
                DestroyImmediate(mesh);
            temporary.Clear();
        }

        [MenuItem("Trainer/Spatial/Export nominal CAD preview PNG")]
        public static void ExportEvidence()
        {
            SpatialBuildValidation.ValidateAll();
            var window = CreateInstance<SpatialPreview>();
            Texture2D texture = null;
            try
            {
                window.asset = AssetDatabase.LoadAssetAtPath<SpatialCatalogAsset>("Assets/Trainer/Content/Spatial/Generated/supplied.spatial");
                window.Reload();
                if (window.snapshot == null)
                    throw new SpatialContentException(window.diagnostic);
                window.preview.BeginStaticPreview(new Rect(0, 0, 1400, 1000));
                window.DrawAssembly();
                CadPreviewView.Render(window.preview.camera);
                texture = window.preview.EndStaticPreview();
                var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../.."));
                var path = System.IO.Path.Combine(root, "artifacts/spatial-assembly.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log("Exported nominal CAD preview to artifacts/spatial-assembly.png");
            }
            finally
            {
                window.ClearTemporaryMeshes();
                if (texture != null)
                    DestroyImmediate(texture);
                DestroyImmediate(window);
            }
        }

        private readonly System.Collections.Generic.List<Mesh> temporary = new System.Collections.Generic.List<Mesh>();
    }
}
