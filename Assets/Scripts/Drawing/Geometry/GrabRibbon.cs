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

        float smoothedArc;          // arc-length del centro finestra, smorzato tra i frame
        bool hasSmoothed;           // false finché non c'è un valore da cui smorzare

        // Frazione della finestra su cui arrotondare ciascun capo della striscia. Profilo
        // circolare (semicerchio): resta a spessore pieno quasi fino al capo e arrotonda solo
        // in punta → capi tondi e "pieni", non appuntiti.
        const float EndRound = 0.12f;

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
        /// <paramref name="smoothing"/> &gt; 0 = costante di tempo (s) dello smorzamento
        /// temporale del centro finestra: elimina lo "scatto" quando il controller scorre lungo
        /// il bordo (0 = aggancio immediato, comportamento storico).
        /// </summary>
        public void RebuildAt(Vector2 localPoint, Mesh mesh, float smoothing = 0f)
        {
            // Bersaglio CONTINUO: proiezione sul segmento di contorno più vicino (non sul
            // campione discreto più vicino, che faceva saltare il centro finestra a gradini).
            float target = NearestArc(localPoint);

            float s0;
            if (smoothing <= 0f || !hasSmoothed)
            {
                s0 = target;
            }
            else
            {
                // Avvicina il centro smorzato al bersaglio lungo la via più breve sul loop.
                float delta = Mathf.Repeat(target - smoothedArc, periLen);
                if (delta > periLen * 0.5f)
                    delta -= periLen;
                float k = 1f - Mathf.Exp(-Time.deltaTime / smoothing); // smorzamento esponenziale
                s0 = Mathf.Repeat(smoothedArc + delta * k, periLen);
            }
            smoothedArc = s0;
            hasSmoothed = true;
            Rebuild(s0, mesh);
        }

        /// <summary>Azzera lo stato di smorzamento: alla prossima comparsa la striscia si
        /// posiziona subito sul punto giusto invece di scivolarci da dov'era.</summary>
        public void ResetSmoothing() => hasSmoothed = false;

        // Arc-length del punto del contorno più vicino a pt, con proiezione continua su ogni
        // segmento del perimetro (interpola tra i campioni → nessuna quantizzazione).
        float NearestArc(Vector2 pt)
        {
            int n = periPos.Length;
            float bestD = float.MaxValue, bestArc = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 a = periPos[i], ab = periPos[j] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-9f ? Mathf.Clamp01(Vector2.Dot(pt - a, ab) / len2) : 0f;
                float d = (pt - (a + ab * t)).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    float a0 = periArc[i];
                    float a1 = (i + 1 < n) ? periArc[i + 1] : periLen;
                    bestArc = Mathf.Lerp(a0, a1, t);
                }
            }
            return bestArc;
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
                // Arrotonda i due capi con un profilo CIRCOLARE (semicerchio): pieno al centro,
                // resta pieno quasi fino al capo e curva solo in punta → capi tondi, non a punta.
                float d = Mathf.Clamp01(Mathf.Min(frac, 1f - frac) / EndRound);
                float h = half * Mathf.Sqrt(Mathf.Max(0f, 1f - (1f - d) * (1f - d)));
                verts[i * 2] = new Vector3(p.x - n.x * h, p.y - n.y * h, 0f); // interno
                verts[i * 2 + 1] = new Vector3(p.x + n.x * h, p.y + n.y * h, 0f); // esterno
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
