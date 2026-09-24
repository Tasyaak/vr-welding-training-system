using System;
using UnityEngine;

namespace WeldingTrainer.Registration.Meta
{
    public static class UnityRegistrationMesh
    {
        // Presentation copy only. Upstream CAD vertices/normals/rigid poses remain right-handed and immutable.
        public static Mesh Create(Mesh source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                    throw new ArgumentException("Spatial visual mesh must contain triangles");
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name + " (Unity presentation)";
            var vertices = copy.vertices;
            var normals = copy.normals;
            var tangents = copy.tangents;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i].z = -vertices[i].z;
            for (int i = 0; i < normals.Length; i++)
                normals[i].z = -normals[i].z;
            for (int i = 0; i < tangents.Length; i++)
            {
                tangents[i].z = -tangents[i].z;
                tangents[i].w = -tangents[i].w;
            }

            copy.vertices = vertices;
            copy.normals = normals;
            copy.tangents = tangents;
            for (int submesh = 0; submesh < copy.subMeshCount; submesh++)
            {
                var indices = copy.GetTriangles(submesh);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int saved = indices[i + 1];
                    indices[i + 1] = indices[i + 2];
                    indices[i + 2] = saved;
                }

                copy.SetTriangles(indices, submesh);
            }

            copy.RecalculateBounds();
            return copy;
        }
    }
}
