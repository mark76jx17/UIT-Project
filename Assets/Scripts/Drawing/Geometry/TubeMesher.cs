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
    /// - [NUOVO] Indici a 32 bit (IndexFormat.UInt32): supporta fino a ~4M vertici invece
    ///   dei ~64K del formato a 16 bit, eliminando il troncamento dei tratti lunghi.
    /// </summary>
    public class TubeMesher
    {
        // Mesh a 32 bit: limite pratico ~1M vertici (RAM device). Prima era 64 000 (16-bit).
        const int MaxVertices = 1_000_000;

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

        // Orientamento del primo anello: se impostato (es. dal Nastro), allinea la
        // sezione all'"alto" del controller invece di una perpendicolare arbitraria,
        // così ruotando il polso si controlla l'inclinazione del nastro.
        public Vector3? UpHint;

        // Scala della coordinata V (lunghezza). Per il tratteggio: V più piccola =
        // trattini più lunghi, così si possono rendere proporzionali allo spessore.
        public float UvScale = 1f;

        // Upload a OGNI punto: con UploadEvery>1 la linea cresceva a blocchetti (scatti)
        // mentre si disegna. La smoothness vince sul risparmio (che si nota solo su tratti
        // lunghissimi). Il vero rimedio O(n) sarebbe l'upload incrementale del solo range
        // nuovo (Mesh.SetVertexBufferData) — da fare con calma e test.
        int sinceUpload;
        const int UploadEvery = 1;

        public TubeMesher(Mesh mesh, int sides = 8)
        {
            this.mesh = mesh;
            this.sides = sides;
            // 32-bit index format: rimuove il limite a ~64K vertici del formato 16-bit.
            // Su Meta Quest 3/3S la GPU supporta pienamente UInt32.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.MarkDynamic();
        }

        public int PointCount => path.Count;

        public void AddPoint(Vector3 point, float radius)
        {
            if (!AddPointNoUpload(point, radius))
                return;
            if (++sinceUpload >= UploadEvery)
            {
                sinceUpload = 0;
                Upload();
            }
        }

        /// <summary>Forza l'upload dei punti accumulati ma non ancora caricati (fine tratto).</summary>
        public void Flush() => Upload();

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

            // Guardia: punti coincidenti (da JSON o ricampionamento) darebbero tangente
            // nulla → anello degenere/NaN. Si saltano.
            if (path.Count > 0 && (point - path[^1]).sqrMagnitude < 1e-10f)
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
                frameNormal = InitialFrame(firstTangent);
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
                uvs.Add(new Vector2((float)i / sides, v * UvScale));
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

        // Primo frame: allineato a UpHint (proiettato perpendicolare alla tangente) se
        // disponibile, altrimenti una perpendicolare arbitraria.
        Vector3 InitialFrame(Vector3 tangent)
        {
            if (UpHint.HasValue)
            {
                var projected = UpHint.Value - tangent * Vector3.Dot(UpHint.Value, tangent);
                if (projected.sqrMagnitude > 1e-6f)
                    return projected.normalized;
            }
            return ArbitraryPerpendicular(tangent);
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
