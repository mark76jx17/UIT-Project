using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Riempimento di un contorno disegnato in aria: si stima il piano di
    /// best-fit del contorno (metodo di Newell), si proiettano i punti in 2D e
    /// si triangola il poligono con l'ear clipping. La mesh risultante usa i
    /// punti 3D originali (tollera contorni non perfettamente piani) ed è a
    /// due facce. Contorni degeneri o fortemente auto-intersecanti producono
    /// un riempimento parziale o nullo, mai un crash.
    /// </summary>
    public static class FillSurface
    {
        const int MaxContourPoints = 120; // l'ear clipping è O(n²): si decima

        public static GameObject Build(IReadOnlyList<Vector3> contour, Material material)
        {
            var points = Prepare(contour);
            if (points.Count < 3)
                return null;

            // Piano di best-fit: normale col metodo di Newell, robusto per
            // poligoni qualsiasi; centro = baricentro.
            var centroid = Vector3.zero;
            foreach (var p in points)
                centroid += p;
            centroid /= points.Count;

            var normal = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
            }
            if (normal.sqrMagnitude < 1e-12f)
                return null; // contorno collineare
            normal.Normalize();

            // Base 2D sul piano (u × v = normal).
            var u = Mathf.Abs(normal.y) < 0.99f
                ? Vector3.Cross(normal, Vector3.up).normalized
                : Vector3.Cross(normal, Vector3.right).normalized;
            var v = Vector3.Cross(normal, u);

            var projected = new List<Vector2>(points.Count);
            foreach (var p in points)
            {
                var d = p - centroid;
                projected.Add(new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)));
            }

            var triangles = Triangulate(projected);
            if (triangles.Count == 0)
                return null;

            return BuildMesh(points, triangles, normal, material);
        }

        static List<Vector3> Prepare(IReadOnlyList<Vector3> contour)
        {
            var points = new List<Vector3>(contour);
            // Se l'utente ha chiuso il contorno a mano, l'ultimo punto duplica il primo.
            while (points.Count > 1 && Vector3.Distance(points[0], points[^1]) < 0.005f)
                points.RemoveAt(points.Count - 1);
            // Decimazione uniforme per tenere l'ear clipping veloce.
            if (points.Count > MaxContourPoints)
            {
                var decimated = new List<Vector3>(MaxContourPoints);
                for (int i = 0; i < MaxContourPoints; i++)
                    decimated.Add(points[i * points.Count / MaxContourPoints]);
                points = decimated;
            }
            return points;
        }

        static GameObject BuildMesh(List<Vector3> points, List<int> triangles,
            Vector3 normal, Material material)
        {
            int n = points.Count;
            var vertices = new Vector3[n * 2];
            var normals = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = points[i];
                vertices[i + n] = points[i];
                normals[i] = normal;
                normals[i + n] = -normal;
            }
            // Fronte (CCW nel piano u,v = normale +n) + retro invertito.
            var indices = new int[triangles.Count * 2];
            for (int i = 0; i < triangles.Count; i += 3)
            {
                indices[i] = triangles[i];
                indices[i + 1] = triangles[i + 1];
                indices[i + 2] = triangles[i + 2];
                indices[triangles.Count + i] = triangles[i + 2] + n;
                indices[triangles.Count + i + 1] = triangles[i + 1] + n;
                indices[triangles.Count + i + 2] = triangles[i] + n;
            }

            var mesh = new Mesh { name = "FillSurface" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = indices;
            mesh.RecalculateBounds();

            var go = new GameObject("Fill");
            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = material;
            // Collider convesso: la forma riempita si può afferrare anche al centro.
            var collider = go.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.isTrigger = true;
            collider.sharedMesh = mesh;
            return go;
        }

        // Ear clipping per poligoni semplici, in senso antiorario.
        static List<int> Triangulate(List<Vector2> polygon)
        {
            var indices = new List<int>();
            for (int i = 0; i < polygon.Count; i++)
                indices.Add(i);
            if (SignedArea(polygon) < 0f)
                indices.Reverse();

            var triangles = new List<int>();
            int guard = polygon.Count * polygon.Count + 16;
            while (indices.Count > 3 && guard-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int i0 = indices[(i - 1 + indices.Count) % indices.Count];
                    int i1 = indices[i];
                    int i2 = indices[(i + 1) % indices.Count];
                    var a = polygon[i0];
                    var b = polygon[i1];
                    var c = polygon[i2];

                    if (Cross(b - a, c - b) <= 0f)
                        continue; // vertice riflesso: non è un orecchio

                    bool containsPoint = false;
                    foreach (var j in indices)
                    {
                        if (j == i0 || j == i1 || j == i2)
                            continue;
                        if (PointInTriangle(polygon[j], a, b, c))
                        {
                            containsPoint = true;
                            break;
                        }
                    }
                    if (containsPoint)
                        continue;

                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
                if (!earFound)
                    break; // contorno degenere/auto-intersecante: riempimento parziale
            }
            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }
            return triangles;
        }

        static float SignedArea(List<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area += Cross(a, b);
            }
            return area * 0.5f;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            return d1 >= 0f && d2 >= 0f && d3 >= 0f;
        }
    }
}
