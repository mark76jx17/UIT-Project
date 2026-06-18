using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Costruisce la mesh di un tubo in modo incrementale: ogni nuovo punto della
    /// polilinea aggiunge un anello di vertici e lo cuce al precedente, quindi il
    /// costo per campione è costante (a differenza di SplineExtrude, che rigenera
    /// l'intera mesh ad ogni modifica).
    ///
    /// - Raggio per-campione: la pressione del trigger modula lo spessore lungo il tratto.
    /// - Parallel transport del frame: evita che il tubo si attorcigli nelle curve.
    /// - UV: u = giro dell'anello, v = lunghezza percorsa in metri (per materiali con texture).
    /// - Upload economico: bounds mantenuti incrementalmente (niente RecalculateBounds O(n))
    ///   e SetTriangles senza validazione.
    /// </summary>
    public class TubeMesher
    {
        // Mesh a 16 bit: ~65k vertici. Oltre, il tratto smette di crescere.
        const int MaxVertices = 64000;

        readonly Mesh mesh;
        readonly int sides;

        readonly List<Vector3> vertices = new(1024);
        readonly List<Vector3> normals = new(1024);
        readonly List<Vector2> uvs = new(1024);
        readonly List<int> triangles = new(4096);
        readonly List<Vector3> path = new(256);

        Vector3 frameNormal;
        float firstRadius;
        float pathLength;
        Bounds bounds;
        bool boundsInitialized;

        public TubeMesher(Mesh mesh, int sides = 8)
        {
            this.mesh = mesh;
            this.sides = sides;
            mesh.MarkDynamic();
        }

        public int PointCount => path.Count;

        public void AddPoint(Vector3 point, float radius)
        {
            if (AddPointNoUpload(point, radius))
                Upload();
        }

        /// <summary>Aggiunta in blocco (ricostruzioni: smoothing, caricamento) con un solo upload.</summary>
        public void AddRange(IReadOnlyList<Vector3> points, IReadOnlyList<float> radii)
        {
            bool dirty = false;
            for (int i = 0; i < points.Count; i++)
                dirty |= AddPointNoUpload(points[i], radii[i]);
            if (dirty)
                Upload();
        }

        bool AddPointNoUpload(Vector3 point, float radius)
        {
            if (vertices.Count + 2 * sides > MaxVertices)
                return false;

            path.Add(point);
            EncapsulateBounds(point, radius);

            // Serve una direzione per orientare l'anello: il primo viene emesso
            // solo quando arriva il secondo punto.
            if (path.Count < 2)
            {
                firstRadius = radius;
                return false;
            }

            if (path.Count == 2)
            {
                var firstTangent = (path[1] - path[0]).normalized;
                frameNormal = ArbitraryPerpendicular(firstTangent);
                AddRing(path[0], firstTangent, firstRadius, 0f);
            }

            var tangent = (point - path[^2]).normalized;
            pathLength += Vector3.Distance(point, path[^2]);
            frameNormal = ParallelTransport(frameNormal, tangent);
            AddRing(point, tangent, radius, pathLength);
            StitchLastRings();
            return true;
        }

        void AddRing(Vector3 center, Vector3 tangent, float radius, float v)
        {
            var binormal = Vector3.Cross(tangent, frameNormal);
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                var dir = Mathf.Cos(angle) * frameNormal + Mathf.Sin(angle) * binormal;
                vertices.Add(center + dir * radius);
                normals.Add(dir);
                uvs.Add(new Vector2((float)i / sides, v));
            }
        }

        void StitchLastRings()
        {
            int a = vertices.Count - 2 * sides; // anello precedente
            int b = vertices.Count - sides;     // anello appena aggiunto
            for (int i = 0; i < sides; i++)
            {
                int i2 = (i + 1) % sides;
                triangles.Add(a + i); triangles.Add(a + i2); triangles.Add(b + i);
                triangles.Add(a + i2); triangles.Add(b + i2); triangles.Add(b + i);
            }
        }

        void Upload()
        {
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);
            mesh.bounds = bounds;
        }

        void EncapsulateBounds(Vector3 point, float radius)
        {
            var pointBounds = new Bounds(point, Vector3.one * (radius * 2f));
            if (!boundsInitialized)
            {
                bounds = pointBounds;
                boundsInitialized = true;
            }
            else
            {
                bounds.Encapsulate(pointBounds);
            }
        }

        static Vector3 ParallelTransport(Vector3 normal, Vector3 newTangent)
        {
            var projected = normal - newTangent * Vector3.Dot(normal, newTangent);
            return projected.sqrMagnitude < 1e-8f
                ? ArbitraryPerpendicular(newTangent)
                : projected.normalized;
        }

        static Vector3 ArbitraryPerpendicular(Vector3 tangent)
        {
            var reference = Mathf.Abs(tangent.y) < 0.99f ? Vector3.up : Vector3.right;
            return Vector3.Cross(tangent, reference).normalized;
        }
    }
}
