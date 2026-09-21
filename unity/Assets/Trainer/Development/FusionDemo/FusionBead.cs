using System.Collections.Generic;
using UnityEngine;

namespace WeldingTrainer.FusionDemo
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FusionBead : MonoBehaviour
    {
        public const float Length = 1f;
        [Range(100, 1000)] public int cells = 500;
        [Range(0.003f, 0.02f)] public float leg = 0.008f;
        [Range(0f, 0.004f)] public float reinforcement = 0.0012f;
        [Min(0.1f)] public float coolingSeconds = 6f;
        [Min(0.01f)] public float formationSeconds = 0.12f;
        private const int Profile = 7;
        private const int VerticesPerCell = Profile * 2;
        private Mesh mesh;
        private Vector2[] times;
        private List<int> triangles;
        private MaterialPropertyBlock properties;
        private MeshRenderer beadRenderer;
        private bool dirty;
        private bool topologyDirty;
        private bool[] burned;
        private float depositTime;
        private System.Action<int, bool> touchCell;
        public SeamCoverage Coverage { get; private set; }

        private void Awake()
        {
            Coverage = new SeamCoverage(Length, Mathf.Clamp(cells, 100, 1000));
            burned = new bool[Coverage.Count];
            var vertices = new Vector3[Coverage.Count * VerticesPerCell];
            times = new Vector2[vertices.Length];
            triangles = new List<int>(Coverage.Count * (Profile - 1) * 6);
            for (int c = 0; c < Coverage.Count; c++)
            for (int ring = 0; ring < 2; ring++)
            for (int p = 0; p < Profile; p++)
            {
                float t = p / (float)(Profile - 1);
                float z = (c + ring) * Coverage.CellLength - Length / 2;
                // Continuous scallops across cell boundaries; no per-cell random cracks.
                float ripple = 0.85f + 0.15f * Mathf.Cos(z * Mathf.PI * 2 / 0.006f);
                float crown = reinforcement * Mathf.Sin(t * Mathf.PI) * ripple;
                vertices[c * VerticesPerCell + ring * Profile + p] =
                    new Vector3(leg * (1 - t) + crown, leg * t + crown, z);
            }
            mesh = new Mesh { name = "Fusion bead (runtime)" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            // Build normals once from the complete surface, then remove all visible faces.
            for (int c = 0; c < Coverage.Count; c++) AddFaces(c);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            triangles.Clear();
            mesh.SetTriangles(triangles, 0, false);
            mesh.uv2 = times;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            properties = new MaterialPropertyBlock();
            properties.SetFloat("_CoolingSeconds", coolingSeconds);
            properties.SetFloat("_FormationSeconds", formationSeconds);
            beadRenderer = GetComponent<MeshRenderer>();
            beadRenderer.SetPropertyBlock(properties);
            touchCell = TouchCell;
        }

        public void Deposit(float fromMetres, float toMetres)
        {
            depositTime = Time.timeSinceLevelLoad;
            Coverage.Deposit(fromMetres, toMetres, touchCell);
            dirty = true;
        }

        private void TouchCell(int cell, bool fresh)
        {
            if (burned[cell]) return;
            if (fresh) AddFaces(cell);
            int start = cell * VerticesPerCell;
            for (int i = start; i < start + VerticesPerCell; i++)
                times[i] = new Vector2(depositTime, fresh ? depositTime : times[i].y);
        }

        public void DepositCell(int cell)
        {
            float centre = (cell + 0.5f) * Coverage.CellLength;
            Deposit(centre, centre);
        }

        public void BurnCell(int cell)
        {
            if (burned[cell]) return;
            burned[cell] = true;
            topologyDirty = dirty = true;
        }

        private void AddFaces(int cell)
        {
            int start = cell * VerticesPerCell;
            for (int p = 0; p < Profile - 1; p++)
            {
                int a = start + p, b = a + Profile;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
            }
        }

        private void LateUpdate()
        {
            // Explicit clock also makes cooling deterministic after entering Play again.
            properties.SetFloat("_DemoTime", Time.timeSinceLevelLoad);
            beadRenderer.SetPropertyBlock(properties);
            if (!dirty) return;
            if (topologyDirty)
            {
                triangles.Clear();
                for (int c = 0; c < Coverage.Count; c++)
                    if (Coverage[c] && !burned[c]) AddFaces(c);
                topologyDirty = false;
            }
            mesh.uv2 = times;
            mesh.SetTriangles(triangles, 0, false);
            dirty = false;
        }

        public void ResetBead()
        {
            Coverage.Clear();
            System.Array.Clear(burned, 0, burned.Length);
            topologyDirty = false;
            triangles.Clear();
            dirty = true;
        }

        private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
