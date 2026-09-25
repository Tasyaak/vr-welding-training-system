using System.Collections.Generic;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    // FusionDemo's scalloped crown and cooling shader, rebuilt from authoritative
    // coverage intervals. No demo coverage, thermal model, or process input owner.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FusionBeadView : MonoBehaviour
    {
        public Material metal;
        Mesh mesh;
        DirectedSpline seam;
        Vector3 normal, lateral;
        long revision = -1;
        readonly List<Vector3> vertices = new();
        readonly List<Vector2> times = new();
        readonly List<int> triangles = new();
        readonly Dictionary<int, float> deposited = new();
        MaterialPropertyBlock properties;
        MeshRenderer view;
        public void Configure(DirectedSpline value, Vec3 outward)
        {
            seam = value;
            normal = ToUnity(outward).normalized;
            lateral = Vector3.Cross(ToUnity(value.Points[1] - value.Points[0]).normalized, normal).normalized;
            mesh = new Mesh
            {
                name = "Authoritative Fusion coverage"
            };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            view = GetComponent<MeshRenderer>();
            view.sharedMaterial = metal;
            properties = new MaterialPropertyBlock();
        }

        public void Apply(CoverageSnapshot coverage)
        {
            if (coverage.Revision == revision)
                return;
            revision = coverage.Revision;
            vertices.Clear();
            triangles.Clear();
            times.Clear();
            foreach (var interval in coverage.Intervals)
            {
                // Bound visual work to 2 mm strips; exact interval ends preserve gaps.
                int count = Mathf.Max(1, Mathf.CeilToInt((float)interval.LengthMetres / .002f));
                for (int i = 0; i < count; i++)
                {
                    float start = (float)(interval.StartArcMetres + interval.LengthMetres * i / count);
                    float end = (float)(interval.StartArcMetres + interval.LengthMetres * (i + 1) / count);
                    int cell = Mathf.FloorToInt((start + end) * .5f / .002f);
                    if (!deposited.TryGetValue(cell, out float time))
                        deposited[cell] = time = Time.timeSinceLevelLoad;
                    int offset = vertices.Count;
                    for (int ring = 0; ring < 2; ring++)
                    {
                        float arc = ring == 0 ? start : end;
                        Vector3 centre = ToUnity(seam.PositionAtArc(arc));
                        for (int p = 0; p < 7; p++)
                        {
                            float t = p / 6f;
                            float ripple = .85f + .15f * Mathf.Cos(arc * Mathf.PI * 2 / .006f);
                            float crown = .0014f * Mathf.Sin(t * Mathf.PI) * ripple;
                            vertices.Add(centre + lateral * ((t - .5f) * .006f) + normal * (.0022f + crown));
                            times.Add(new Vector2(time, interval.Quality == CoverageQuality.Poor ? 1 : 0));
                        }
                    }

                    for (int p = 0; p < 6; p++)
                    {
                        int a = offset + p, b = a + 7;
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(a + 1);
                        triangles.Add(a + 1);
                        triangles.Add(b);
                        triangles.Add(b + 1);
                    }
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(1, times);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        void LateUpdate()
        {
            if (!view)
                return;
            properties.SetFloat("_DemoTime", Time.timeSinceLevelLoad);
            view.SetPropertyBlock(properties);
        }

        public static Vector3 ToUnity(Vec3 v) => new((float)v.X, (float)v.Y, (float)-v.Z);
        void OnDestroy()
        {
            if (mesh)
                Destroy(mesh);
        }
    }
}
