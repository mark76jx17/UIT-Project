using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Mesh procedurali per la palette: rettangoli con angoli arrotondati
    /// (raggio = metà lato => cerchio/pillola) e quad con UV per le texture.
    /// Tutte rivolte verso -Z, come i Quad di Unity usati finora.
    /// </summary>
    public static class RoundedMesh
    {
        public static Mesh Rect(float width, float height, float corner, int cornerSegments = 5)
        {
            corner = Mathf.Min(corner, Mathf.Min(width, height) * 0.5f);
            var contour = new List<Vector3>();
            float cx = width * 0.5f - corner;
            float cy = height * 0.5f - corner;

            // Quattro archi in senso antiorario, partendo dall'angolo in alto a destra.
            AddArc(contour, new Vector2(cx, cy), corner, 0f, cornerSegments);
            AddArc(contour, new Vector2(-cx, cy), corner, 90f, cornerSegments);
            AddArc(contour, new Vector2(-cx, -cy), corner, 180f, cornerSegments);
            AddArc(contour, new Vector2(cx, -cy), corner, 270f, cornerSegments);

            // Ventaglio dal centro; (centro, p[i+1], p[i]) per la faccia verso -Z.
            var vertices = new List<Vector3> { Vector3.zero };
            vertices.AddRange(contour);
            var triangles = new List<int>();
            for (int i = 1; i <= contour.Count; i++)
            {
                int next = i % contour.Count + 1;
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(i);
            }

            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Vector3.back;

            var mesh = new Mesh { name = "RoundedRect" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(new List<Vector3>(normals));
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        /// <summary>Quad con UV (0..1) rivolto verso -Z, per texture (ruota colori, gradiente).</summary>
        public static Mesh TexturedQuad(float width, float height)
        {
            float w = width * 0.5f;
            float h = height * 0.5f;
            var mesh = new Mesh { name = "TexturedQuad" };
            mesh.SetVertices(new List<Vector3>
            {
                new(-w, -h, 0), new(w, -h, 0), new(w, h, 0), new(-w, h, 0),
            });
            mesh.SetUVs(0, new List<Vector2>
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1),
            });
            mesh.SetNormals(new List<Vector3>
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            });
            mesh.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
            return mesh;
        }

        static void AddArc(List<Vector3> contour, Vector2 center, float radius,
            float startDegrees, int segments)
        {
            for (int i = 0; i <= segments; i++)
            {
                float angle = (startDegrees + 90f * i / segments) * Mathf.Deg2Rad;
                contour.Add(new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    0f));
            }
        }
    }
}
