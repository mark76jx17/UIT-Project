using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Riempimento di un contorno disegnato in aria: si stima il piano di
    /// best-fit del contorno (metodo di Newell), si proiettano i punti in 2D e
    /// si triangola il poligono con l'ear clipping. La mesh risultante usa i
    /// punti 3D originali (tollera contorni non perfettamente piani) ed è a
    /// due facce.
    /// I contorni che si auto-intersecano (un "8", un ∞, un tratto che si
    /// ripassa sopra) vengono prima spezzati nei loro anelli semplici nei punti
    /// di incrocio: così OGNI lobo viene riempito, non solo quello dominante.
    /// Contorni degeneri o collineari producono un riempimento parziale o
    /// nullo, mai un crash.
    /// </summary>
    public static class FillSurface
    {
        const int MaxContourPoints = 120; // l'ear clipping è O(n²): si decima
        const int MaxLoops = 64;          // guardia anti-blowup su input patologici

        // Un vertice del contorno: posizione 2D sul piano (per la triangolazione)
        // accoppiata alla posizione 3D originale (per la mesh). I punti di
        // incrocio creati dallo split ricavano la 3D interpolando lungo lo spigolo,
        // così si conserva la tolleranza ai contorni non perfettamente piani.
        readonly struct Vert
        {
            public readonly Vector2 uv;
            public readonly Vector3 pos;
            public Vert(Vector2 uv, Vector3 pos) { this.uv = uv; this.pos = pos; }
        }

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

            // Proietta in 2D mantenendo accanto la 3D originale.
            var loop = new List<Vert>(points.Count);
            foreach (var p in points)
            {
                var d = p - centroid;
                loop.Add(new Vert(new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)), p));
            }

            // Spezza il contorno auto-intersecante nei suoi anelli semplici: ogni
            // anello ha un suo verso coerente, quindi l'ear clipping lo riempie.
            var loops = new List<List<Vert>>();
            SplitIntoSimpleLoops(loop, loops);

            // Triangola ogni anello e accumula vertici + indici in un'unica mesh.
            var verts = new List<Vector3>();
            var triangles = new List<int>();
            var poly2d = new List<Vector2>();
            foreach (var l in loops)
            {
                if (l.Count < 3)
                    continue;
                poly2d.Clear();
                foreach (var vert in l)
                    poly2d.Add(vert.uv);

                var local = Triangulate(poly2d);
                if (local.Count == 0)
                    continue;

                int baseIndex = verts.Count;
                foreach (var vert in l)
                    verts.Add(vert.pos);
                foreach (var idx in local)
                    triangles.Add(baseIndex + idx);
            }
            if (triangles.Count == 0)
                return null;

            return BuildMesh(verts, triangles, normal, material);
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

        /// <summary>
        /// Decompone un anello (eventualmente auto-intersecante) negli anelli
        /// semplici che lo compongono. Trova il PRIMO incrocio proprio tra due
        /// spigoli non adiacenti, taglia il poligono in due sotto-anelli che
        /// condividono il punto d'incrocio e ricorre su entrambi. Senza incroci
        /// l'anello è semplice e viene aggiunto all'output.
        /// </summary>
        static void SplitIntoSimpleLoops(List<Vert> loop, List<List<Vert>> output)
        {
            int m = loop.Count;
            if (m < 3 || output.Count >= MaxLoops)
            {
                if (m >= 3)
                    output.Add(loop);
                return;
            }

            for (int i = 0; i < m; i++)
            {
                var a0 = loop[i].uv;
                var a1 = loop[(i + 1) % m].uv;
                for (int j = i + 2; j < m; j++)
                {
                    // Salta lo spigolo che si richiude su i (adiacente via chiusura).
                    if (i == 0 && j == m - 1)
                        continue;
                    var b0 = loop[j].uv;
                    var b1 = loop[(j + 1) % m].uv;
                    if (!SegmentsIntersect(a0, a1, b0, b1, out float t, out Vector2 x))
                        continue;

                    // Punto d'incrocio: 3D interpolata lungo lo spigolo i (preserva
                    // l'eventuale non-planarità del contorno).
                    var xv = new Vert(x, Vector3.Lerp(loop[i].pos, loop[(i + 1) % m].pos, t));

                    // Anello A: X, poi i vertici i+1..j.
                    var loopA = new List<Vert>(j - i + 1) { xv };
                    for (int k = i + 1; k <= j; k++)
                        loopA.Add(loop[k]);

                    // Anello B: X, poi j+1..m-1 e 0..i (avvolgendo).
                    var loopB = new List<Vert>(m - (j - i) + 1) { xv };
                    for (int k = j + 1; k < m; k++)
                        loopB.Add(loop[k]);
                    for (int k = 0; k <= i; k++)
                        loopB.Add(loop[k]);

                    SplitIntoSimpleLoops(loopA, output);
                    SplitIntoSimpleLoops(loopB, output);
                    return;
                }
            }

            output.Add(loop); // nessun incrocio: anello semplice
        }

        // Incrocio PROPRIO di due segmenti 2D (p→p2) e (q→q2): true solo se si
        // tagliano all'interno di entrambi (estremi esclusi, con margine), così
        // non si spezza in corrispondenza di vertici condivisi o tocchi degeneri.
        static bool SegmentsIntersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2,
            out float t, out Vector2 point)
        {
            t = 0f;
            point = default;
            var r = p2 - p;
            var d = q2 - q;
            float denom = r.x * d.y - r.y * d.x;
            if (Mathf.Abs(denom) < 1e-12f)
                return false; // paralleli o degeneri
            var qp = q - p;
            t = (qp.x * d.y - qp.y * d.x) / denom;
            float s = (qp.x * r.y - qp.y * r.x) / denom;
            const float e = 1e-4f;
            if (t <= e || t >= 1f - e || s <= e || s >= 1f - e)
                return false;
            point = p + t * r;
            return true;
        }

        static GameObject BuildMesh(List<Vector3> verts, List<int> triangles,
            Vector3 normal, Material material)
        {
            int n = verts.Count;
            var vertices = new Vector3[n * 2];
            var normals = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = verts[i];
                vertices[i + n] = verts[i];
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
            // Niente collider sul riempimento: un MeshCollider convesso su una forma
            // concava (es. "C", stella) coprirebbe l'intero inviluppo, rendendo
            // imprecisi presa e gomma. La forma si afferra/cancella dal contorno
            // (il tratto), che ha già i suoi collider.
            return go;
        }

        // Ear clipping per poligoni semplici, in senso antiorario.
        // Ritorna indici (0..polygon.Count-1) nel poligono passato.
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
