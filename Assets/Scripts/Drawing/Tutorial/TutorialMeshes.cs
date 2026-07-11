using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>Mesh procedurali del tutorial: un anello (annulus) per evidenziare un tasto
    /// senza coprirlo. Rivolto verso -Z come le altre mesh della UI.</summary>
    public static class TutorialMeshes
    {
        public static Mesh Ring(float outer, float inner, int segments = 48)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts.Add(new Vector3(c * outer, s * outer, 0f));
                verts.Add(new Vector3(c * inner, s * inner, 0f));
            }
            for (int i = 0; i < segments; i++)
            {
                int o = i * 2;
                tris.Add(o); tris.Add(o + 1); tris.Add(o + 2);
                tris.Add(o + 2); tris.Add(o + 1); tris.Add(o + 3);
            }
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Vector3.back;

            var mesh = new Mesh { name = "TutorialRing" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetNormals(new List<Vector3>(normals));
            return mesh;
        }
    }
}
