using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeldingTrainer.FusionDemo
{
    /// <summary>Clips the actual flange surfaces, not a black decal over intact metal.</summary>
    public sealed class FusionBurnThroughVisual : IDisposable
    {
        private readonly Transform workpiece;
        private readonly Texture2D stateTexture;
        private readonly Color32[] pixels;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<Material[]> originals = new List<Material[]>();
        private readonly List<Material> materials = new List<Material>();

        public FusionBurnThroughVisual(Transform piece, FusionBead bead, Renderer[] surfaces)
        {
            workpiece = piece;
            Shader shader = Resources.Load<Shader>("FusionWorkpiece");
            if (shader == null) throw new InvalidOperationException("Missing Resources/FusionWorkpiece.shader");
            stateTexture = new Texture2D(bead.Coverage.Count, 1, TextureFormat.RGBA32, false, true)
            { name = "Fusion thermal cells", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[bead.Coverage.Count];
            stateTexture.SetPixels32(pixels);
            stateTexture.Apply(false, false);
            if (surfaces == null || surfaces.Length == 0)
                surfaces = piece.GetComponentsInChildren<MeshRenderer>(true);
            foreach (Renderer surface in surfaces)
            {
                if (surface == null || surface == bead.GetComponent<Renderer>() || renderers.Contains(surface)) continue;
                Material[] original = surface.sharedMaterials;
                Material[] replacements = new Material[original.Length];
                for (int i = 0; i < replacements.Length; i++)
                {
                    Material mat = new Material(shader) { name = "Fusion perforated metal", enableInstancing = true };
                    if (original[i] != null)
                    {
                        foreach (string property in new[] { "_BaseColor", "_Metallic", "_Smoothness" })
                            if (original[i].HasProperty(property))
                            {
                                if (property == "_BaseColor") mat.SetColor(property, original[i].GetColor(property));
                                else mat.SetFloat(property, original[i].GetFloat(property));
                            }
                        if (original[i].HasProperty("_BaseMap"))
                        {
                            mat.SetTexture("_BaseMap", original[i].GetTexture("_BaseMap"));
                            mat.SetTextureScale("_BaseMap", original[i].GetTextureScale("_BaseMap"));
                            mat.SetTextureOffset("_BaseMap", original[i].GetTextureOffset("_BaseMap"));
                        }
                    }
                    mat.SetTexture("_CellState", stateTexture);
                    mat.SetFloat("_HoleRadius", bead.leg * 1.25f);
                    mat.SetMatrix("_WorldToWorkpiece", piece.worldToLocalMatrix);
                    replacements[i] = mat;
                    materials.Add(mat);
                }
                renderers.Add(surface); originals.Add(original);
                surface.sharedMaterials = replacements;
            }
        }

        public void Refresh(SeamThermalModel model)
        {
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(model.Burned[i] ? (byte)255 : (byte)0,
                    (byte)(Mathf.Clamp01(model.HeatSeconds[i] / model.BurnDose) * 255), 0, 255);
            stateTexture.SetPixels32(pixels);
            stateTexture.Apply(false, false);
            foreach (Material material in materials)
                material.SetMatrix("_WorldToWorkpiece", workpiece.worldToLocalMatrix);
        }

        public void Dispose()
        {
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null) renderers[i].sharedMaterials = originals[i];
            foreach (Material mat in materials) UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(stateTexture);
        }
    }
}
