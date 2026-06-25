using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Icone generate in codice (bianche su trasparente) per i pulsanti strumento e
    /// azione: glifi pieni e coerenti, nitidi a qualsiasi dimensione, senza problemi
    /// di import/sfondo dei PNG. Disegnate con SDF (segmenti, dischi, triangoli, archi)
    /// e anti-aliasing, in coordinate normalizzate -1..1 (y verso l'alto).
    /// </summary>
    public static class ToolIcon
    {
        const int S = 72;
        static readonly Dictionary<string, Texture2D> cache = new();
        static float[] cov;

        public static Texture2D Get(string name)
        {
            if (cache.TryGetValue(name, out var t))
                return t;

            cov = new float[S * S];
            switch (name)
            {
                case "pencil": Pencil(); break;
                case "droplet": Droplet(); break;
                case "eraser": Eraser(); break;
                case "undo": Arc(false); break;
                case "redo": Arc(true); break;
                case "save": SaveLoad(down: true); break;
                case "load": SaveLoad(down: false); break;
                case "clear": Trash(); break;
                case "options": Ellipsis(); break;
                default: Disc(Vector2.zero, 0.4f); break;
            }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(cov[i]));
            tex.SetPixels(px);
            tex.Apply();
            cache[name] = tex;
            return tex;
        }

        // ---------- glifi ----------

        static void Pencil()
        {
            Seg(new(-0.50f, -0.50f), new(0.34f, 0.34f), 0.18f); // corpo
            Tri(new(0.20f, 0.56f), new(0.56f, 0.20f), new(0.74f, 0.74f)); // punta
        }

        static void Droplet()
        {
            Disc(new(0f, -0.18f), 0.24f);
            Tri(new(-0.17f, -0.04f), new(0.17f, -0.04f), new(0f, 0.40f));
        }

        static void Eraser()
        {
            // blocco inclinato (parallelogramma) via due triangoli
            Vector2 a = new(-0.55f, -0.05f), b = new(0.20f, -0.50f);
            Vector2 c = new(0.55f, 0.12f), d = new(-0.20f, 0.57f);
            Tri(a, b, c);
            Tri(a, c, d);
        }

        static void Arc(bool redo)
        {
            const float R = 0.46f, th = 0.115f;
            const int N = 30;
            float a0 = 210f, a1 = -25f; // arco sopra la testa
            Vector2 prev = M(redo, Polar(R, a0));
            for (int i = 1; i <= N; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / N);
                var cur = M(redo, Polar(R, a));
                Seg(prev, cur, th);
                prev = cur;
            }
            // punta di freccia all'estremità mobile (a1), diretta lungo la tangente
            Vector2 tip = M(redo, Polar(R, a1));
            Vector2 tangent = M(redo, (Polar(R, a1) - Polar(R, a1 + 12f)).normalized);
            Vector2 n = new(-tangent.y, tangent.x);
            Vector2 baseC = tip - tangent * 0.04f;
            Tri(tip + tangent * 0.26f, baseC + n * 0.20f, baseC - n * 0.20f);
        }

        // Tre puntini orizzontali (menu "Options"): coerente con gli altri glifi SDF.
        static void Ellipsis()
        {
            Disc(new(-0.34f, 0f), 0.13f);
            Disc(new(0f, 0f), 0.13f);
            Disc(new(0.34f, 0f), 0.13f);
        }

        static void Trash()
        {
            Box(new(0f, 0.58f), new(0.16f, 0.06f));   // manico
            Box(new(0f, 0.42f), new(0.46f, 0.085f));  // coperchio
            // corpo a trapezio (due triangoli)
            Vector2 tl = new(-0.34f, 0.30f), tr = new(0.34f, 0.30f);
            Vector2 bl = new(-0.24f, -0.55f), br = new(0.24f, -0.55f);
            Tri(tl, bl, br);
            Tri(tl, br, tr);
        }

        static void SaveLoad(bool down)
        {
            float s = down ? 1f : -1f;
            Seg(new(0f, 0.48f * s), new(0f, -0.02f * s), 0.11f);              // asta
            Tri(new(-0.28f, -0.02f * s), new(0.28f, -0.02f * s), new(0f, -0.46f * s)); // freccia
            Box(new(0f, -0.58f), new(0.52f, 0.075f));                         // base/vassoio
        }

        // ---------- primitive SDF ----------

        static void Disc(Vector2 c, float r) => Stamp(p => (p - c).magnitude - r);

        static void Seg(Vector2 a, Vector2 b, float th) => Stamp(p => SdSeg(p, a, b) - th);

        static void Box(Vector2 c, Vector2 half) => Stamp(p => SdBox(p, c, half));

        static void Tri(Vector2 a, Vector2 b, Vector2 c) => Stamp(p => SdTriangle(p, a, b, c));

        static Vector2 Polar(float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        static Vector2 M(bool mirror, Vector2 v) => mirror ? new Vector2(-v.x, v.y) : v;

        static void Stamp(System.Func<Vector2, float> sdf)
        {
            const float aa = 2.5f / S;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2((x + 0.5f) / S * 2f - 1f, (y + 0.5f) / S * 2f - 1f);
                    float c = Mathf.Clamp01(0.5f - sdf(p) / aa);
                    int i = y * S + x;
                    if (c > cov[i]) cov[i] = c;
                }
            }
        }

        static float SdSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        static float SdBox(Vector2 p, Vector2 c, Vector2 half)
        {
            Vector2 q = new(Mathf.Abs(p.x - c.x) - half.x, Mathf.Abs(p.y - c.y) - half.y);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        // Distanza con segno a un triangolo pieno, INDIPENDENTE dal verso dei vertici
        // (orario/antiorario): negativa dentro, positiva fuori. Distanza non segnata
        // dai tre lati + test punto-nel-triangolo robusto per il segno.
        static float SdTriangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float dist = Mathf.Sqrt(Mathf.Min(Mathf.Min(
                Vector2.Dot(pq0, pq0), Vector2.Dot(pq1, pq1)), Vector2.Dot(pq2, pq2)));
            return PointInTriangle(p, p0, p1, p2) ? -dist : dist;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Edge(p, a, b), d2 = Edge(p, b, c), d3 = Edge(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        static float Edge(Vector2 p, Vector2 a, Vector2 b) =>
            (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
