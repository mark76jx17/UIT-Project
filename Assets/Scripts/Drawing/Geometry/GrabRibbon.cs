using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Striscia di grab-highlight che scorre lungo il contorno arrotondato di un pannello:
    /// la matematica pura (perimetro campionato + finestra di striscia) estratta da
    /// PaletteController per essere condivisa col foglio a quadretti (GridSheet).
    /// Passando da lato ad angolo la striscia segue la curva → transizione smooth.
    /// Un'istanza per pannello: tiene il perimetro precampionato e i buffer del mesh.
    /// </summary>
    public class GrabRibbon
    {
        Vector2[] periPos;          // contorno arrotondato campionato (loop): posizioni locali
        Vector2[] periNrm;          // normali uscenti per ogni campione
        float[] periArc;            // arc-length cumulativo per ogni campione
        float periLen;              // lunghezza totale del contorno

        readonly float thick;       // spessore della striscia
        readonly float window;      // lunghezza dell'arco visibile attorno al punto più vicino
        readonly int segs;          // risoluzione della finestra

        Vector3[] verts;
        Vector2[] uv;

        public GrabRibbon(Vector2 panelSize, float cornerRadius, float thickness, float windowLength, int windowSegs)
        {
            thick = thickness;
            window = windowLength;
            segs = windowSegs;
            BuildPerimeter(panelSize, cornerRadius);
        }

        /// <summary>
        /// Ricostruisce la striscia nel mesh dato, centrata sul punto del contorno più vicino
        /// a localPoint (spazio locale del pannello, pre-scala, piano XY).
        /// </summary>
        public void RebuildAt(Vector2 localPoint, Mesh mesh)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < periPos.Length; i++)
            {
                float d = (periPos[i] - localPoint).sqrMagnitude;
                if (d < best) { best = d; nearest = i; }
            }
            Rebuild(periArc[nearest], mesh);
        }

        // Ricostruisce la striscia per la finestra [s0-window/2, s0+window/2] lungo il contorno.
        void Rebuild(float s0, Mesh mesh)
        {
            int pts = segs + 1;
            if (verts == null || verts.Length != pts * 2)
            {
                verts = new Vector3[pts * 2];
                uv = new Vector2[pts * 2];
                var tris = new int[segs * 6];
                for (int i = 0; i < segs; i++)
                {
                    int b = i * 2;
                    tris[i * 6 + 0] = b; tris[i * 6 + 1] = b + 2; tris[i * 6 + 2] = b + 1;
                    tris[i * 6 + 3] = b + 1; tris[i * 6 + 4] = b + 2; tris[i * 6 + 5] = b + 3;
                }
                mesh.Clear();
                // imposto prima i vertici (riempiti sotto), poi i triangoli una volta
                mesh.vertices = verts;
                mesh.triangles = tris;
            }

            float half = thick * 0.5f;
            for (int i = 0; i < pts; i++)
            {
                float frac = i / (float)segs;               // 0..1 lungo la finestra
                float s = s0 + (frac - 0.5f) * window;
                SampleContour(s, out var p, out var n);
                verts[i * 2] = new Vector3(p.x - n.x * half, p.y - n.y * half, 0f); // interno
                verts[i * 2 + 1] = new Vector3(p.x + n.x * half, p.y + n.y * half, 0f); // esterno
                uv[i * 2] = new Vector2(frac, 0f);
                uv[i * 2 + 1] = new Vector2(frac, 1f);
            }
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.RecalculateBounds();
        }

        // Campiona il contorno a una data arc-length (wrap sul loop): posizione + normale uscente.
        void SampleContour(float s, out Vector2 pos, out Vector2 nrm)
        {
            s = Mathf.Repeat(s, periLen);
            int n = periPos.Length;
            for (int i = 0; i < n; i++)
            {
                float a0 = periArc[i];
                float a1 = (i + 1 < n) ? periArc[i + 1] : periLen;
                if (s >= a0 && s <= a1)
                {
                    float k = a1 > a0 ? (s - a0) / (a1 - a0) : 0f;
                    int j = (i + 1) % n;
                    pos = Vector2.Lerp(periPos[i], periPos[j], k);
                    nrm = Vector2.Lerp(periNrm[i], periNrm[j], k).normalized;
                    return;
                }
            }
            pos = periPos[0];
            nrm = periNrm[0];
        }

        // Campiona il contorno arrotondato (rettangolo con angoli di raggio r) in un loop ordinato
        // di (posizione, normale uscente, arc-length). Lati + 4 archi → transizione continua.
        void BuildPerimeter(Vector2 size, float r)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            var list = new System.Collections.Generic.List<(Vector2 p, Vector2 n)>();
            void Line(Vector2 from, Vector2 to, Vector2 nrm, int lineSegs)
            {
                for (int i = 0; i < lineSegs; i++)
                    list.Add((Vector2.Lerp(from, to, i / (float)lineSegs), nrm)); // estremo escluso
            }
            void Arc(Vector2 c, float a0, float a1, int arcSegs)
            {
                for (int i = 0; i < arcSegs; i++)
                {
                    float ang = Mathf.Lerp(a0, a1, i / (float)arcSegs);
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    list.Add((c + dir * r, dir));
                }
            }
            const float HALF_PI = Mathf.PI * 0.5f;
            Line(new Vector2(hx, -(hy - r)), new Vector2(hx, hy - r), new Vector2(1, 0), 6);
            Arc(new Vector2(hx - r, hy - r), 0f, HALF_PI, 5);
            Line(new Vector2(hx - r, hy), new Vector2(-(hx - r), hy), new Vector2(0, 1), 8);
            Arc(new Vector2(-(hx - r), hy - r), HALF_PI, Mathf.PI, 5);
            Line(new Vector2(-hx, hy - r), new Vector2(-hx, -(hy - r)), new Vector2(-1, 0), 6);
            Arc(new Vector2(-(hx - r), -(hy - r)), Mathf.PI, Mathf.PI * 1.5f, 5);
            Line(new Vector2(-(hx - r), -hy), new Vector2(hx - r, -hy), new Vector2(0, -1), 8);
            Arc(new Vector2(hx - r, -(hy - r)), Mathf.PI * 1.5f, Mathf.PI * 2f, 5);

            int count = list.Count;
            periPos = new Vector2[count];
            periNrm = new Vector2[count];
            periArc = new float[count];
            float acc = 0f;
            for (int i = 0; i < count; i++)
            {
                periPos[i] = list[i].p;
                periNrm[i] = list[i].n.normalized;
                periArc[i] = acc;
                int j = (i + 1) % count;
                acc += Vector2.Distance(list[i].p, list[j].p);
            }
            periLen = acc;
        }
    }
}
