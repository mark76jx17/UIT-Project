using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Riempimento "a secchiello" dell'area chiusa dove punti, indipendentemente da come
    /// sono raggruppati i tratti: clicca dentro un buco chiuso da delle linee → si riempie.
    ///
    /// Metodo FLOOD FILL raster (come il secchiello dei programmi di disegno, e il
    /// trapped-ball dell'industria dei cartoni): i tratti vicini vengono proiettati su un
    /// piano e "disegnati" (con spessore) su una griglia; da punto puntato si allaga l'area
    /// vuota finché non tocca le linee. Se l'allagamento resta racchiuso → è un'area chiusa
    /// → si estrae il contorno (esterno + eventuali buchi) e si costruisce la mesh. Se
    /// fuoriesce ai bordi → area aperta → non si riempie (niente fill spuri tra oggetti
    /// vicini che non racchiudono nulla). È un'operazione ONE-SHOT (al trigger).
    /// </summary>
    public static class FillRegion
    {
        const int GridTarget = 200;      // celle sul lato più lungo del riquadro
        const int MaxDim = 320;          // tetto sulla dimensione della griglia
        const float WallHalf = 0.01f;    // semi-spessore delle linee sulla griglia = gap massimo richiudibile (1 cm)
        const float SimplifyEps = 1.3f;  // Douglas-Peucker (in celle): leviga i gradini del contorno
        const int MaxRecords = 64;
        const int MaxRings = 60;         // tetto sugli anelli concentrici (fill "col pennello")

        // Chiusura fessure: si ALLUNGANO un po' le estremità libere delle linee aperte, così
        // una divisoria che non arriva fino al bordo lo raggiunge e separa le due celle
        // adiacenti (senza ingrossare i muri, che invece "mangerebbero" le celle sottili).
        const float GapExtend = 0.012f;   // quanto allungare gli estremi liberi (1.2 cm)
        const float OpenSpanMin = 0.03f;  // si allunga solo se la linea è lunga almeno così (no puntini)

        static readonly Collider[] overlapBuffer = new Collider[256];

        /// <summary>Regione da riempire: contorno esterno + eventuali BUCHI (in spazio mondo).</summary>
        public class Region
        {
            public List<Vector3> outer;
            public readonly List<List<Vector3>> holes = new();
            public Transform root;      // oggetto a cui agganciare il fill (se ne delimita uno solo)
            public float outerArea;
            public BrushType brushType = BrushType.Round; // tipo di pennello copiato dall'oggetto
            public float radius = 0.006f;
        }

        public static GameObject FillCellAt(Vector3 seed, Color color, float searchRadius, float closeThreshold)
        {
            var region = FindRegion(seed, searchRadius, closeThreshold);
            if (region == null)
                return null;

            // Se l'area è delimitata da UN solo oggetto, il fill entra nella sua gerarchia
            // (così si muove con lui e si salva come figlio); altrimenti resta indipendente.
            if (region.root != null)
            {
                var localOuter = ToLocal(region.root, region.outer);
                var localHoles = new List<List<Vector3>>();
                foreach (var h in region.holes)
                    localHoles.Add(ToLocal(region.root, h));
                var fill = Stroke.MakeGroupFill(localOuter, localHoles, color, region.brushType);
                if (fill.GetComponent<MeshFilter>() != null)
                    fill.transform.SetParent(region.root, false);
                return fill;
            }
            return Stroke.MakeCellFill(region, color);
        }

        static List<Vector3> ToLocal(Transform root, List<Vector3> world)
        {
            var local = new List<Vector3>(world.Count);
            foreach (var p in world)
                local.Add(root.InverseTransformPoint(p));
            return local;
        }

        // Contesto raster condiviso da FindRegion (fill piatto) e FindRings (anelli):
        // la maschera dell'area allagata + i parametri per riportarla nel mondo.
        class RasterCtx
        {
            public bool[] region;
            public int W, H;
            public float cell, minx, miny;
            public Vector3 seed, u, v;
            public Transform root;              // oggetto unico che delimita l'area, o null
            public List<StrokeRecord> records;  // tratti vicini (per il pennello degli anelli)
        }

        // Costruisce la maschera dell'area chiusa che contiene il seed (piano, rasterizza i
        // tratti con spessore, flood, dilatazione). Null se lì non c'è area chiusa (l'allagamento
        // tocca il bordo). Base comune di FindRegion (piatto) e FindRings (anelli).
        static RasterCtx BuildMask(Vector3 seed, float searchRadius)
        {
            var records = GatherRecords(seed, searchRadius);
            if (records.Count == 0)
                return null;
            if (!FitPlane(records, seed, out var u, out var v, out _))
                return null;

            var segs = new List<(Vector2 a, Vector2 b)>();
            float minx = float.MaxValue, miny = float.MaxValue, maxx = float.MinValue, maxy = float.MinValue;
            foreach (var rec in records)
            {
                var t = rec.transform;
                int m = rec.points.Count;
                Vector2 first = Project(t.TransformPoint(rec.points[0]), seed, u, v);
                Accum(first, ref minx, ref miny, ref maxx, ref maxy);
                Vector2 prev = first, second = first, secondLast = first, last = first;
                for (int i = 1; i < m; i++)
                {
                    Vector2 cur = Project(t.TransformPoint(rec.points[i]), seed, u, v);
                    segs.Add((prev, cur));
                    Accum(cur, ref minx, ref miny, ref maxx, ref maxy);
                    if (i == 1) second = cur;
                    secondLast = prev;
                    last = cur;
                    prev = cur;
                }

                // Linea APERTA e non troppo corta: allunga i due estremi liberi per chiudere
                // le fessure con il contorno (separa celle che condividono la divisoria).
                if (m >= 2 && (last - first).sqrMagnitude > OpenSpanMin * OpenSpanMin)
                {
                    Vector2 d0 = first - second;
                    if (d0.sqrMagnitude > 1e-8f)
                    {
                        Vector2 e = first + d0.normalized * GapExtend;
                        segs.Add((first, e)); Accum(e, ref minx, ref miny, ref maxx, ref maxy);
                    }
                    Vector2 d1 = last - secondLast;
                    if (d1.sqrMagnitude > 1e-8f)
                    {
                        Vector2 e = last + d1.normalized * GapExtend;
                        segs.Add((last, e)); Accum(e, ref minx, ref miny, ref maxx, ref maxy);
                    }
                }
            }
            if (segs.Count == 0)
                return null;

            minx = Mathf.Max(minx, -searchRadius); miny = Mathf.Max(miny, -searchRadius);
            maxx = Mathf.Min(maxx, searchRadius); maxy = Mathf.Min(maxy, searchRadius);
            float margin = WallHalf * 2f + 0.02f;
            minx -= margin; miny -= margin; maxx += margin; maxy += margin;
            float bw = maxx - minx, bh = maxy - miny;
            if (bw < 1e-4f || bh < 1e-4f)
                return null;

            float cell = Mathf.Max(bw, bh) / GridTarget;
            int W = Mathf.CeilToInt(bw / cell) + 2;
            int H = Mathf.CeilToInt(bh / cell) + 2;
            if (W > MaxDim || H > MaxDim)
            {
                cell = Mathf.Max(bw, bh) / (MaxDim - 2);
                W = Mathf.CeilToInt(bw / cell) + 2;
                H = Mathf.CeilToInt(bh / cell) + 2;
            }

            var wall = new bool[W * H];
            int tpx = Mathf.Max(1, Mathf.RoundToInt(WallHalf / cell));
            foreach (var s in segs)
                StampSegment(wall, W, H, s.a, s.b, minx, miny, cell, tpx);

            int sx = Mathf.FloorToInt((0f - minx) / cell);
            int sy = Mathf.FloorToInt((0f - miny) / cell);
            if (sx < 0 || sy < 0 || sx >= W || sy >= H)
                return null;
            if (wall[sy * W + sx] && !NearestFree(wall, W, H, ref sx, ref sy, tpx + 3))
                return null;

            var region = new bool[W * H];
            if (!Flood(wall, region, W, H, sx, sy))
                return null;
            region = Dilate(region, W, H, tpx);

            Transform root = null;
            bool single = true;
            foreach (var rec in records)
            {
                var item = rec.GetComponentInParent<DrawnItem>();
                var r = item != null ? item.transform : null;
                if (root == null) root = r;
                else if (r != root) { single = false; break; }
            }

            return new RasterCtx
            {
                region = region, W = W, H = H, cell = cell, minx = minx, miny = miny,
                seed = seed, u = u, v = v, root = single ? root : null, records = records,
            };
        }

        /// <summary>
        /// Regione (esterno + buchi, in spazio mondo) dell'area chiusa che contiene
        /// <paramref name="seed"/>, o null se lì non c'è un'area chiusa. Usata dal fill piatto
        /// e dall'anteprima.
        /// </summary>
        public static Region FindRegion(Vector3 seed, float searchRadius, float closeThreshold)
        {
            var ctx = BuildMask(seed, searchRadius);
            if (ctx == null)
                return null;

            var loops = ContourLoops(ctx.region, ctx.W, ctx.H);
            if (loops.Count == 0)
                return null;
            for (int i = 0; i < loops.Count; i++)
                loops[i] = Simplify(loops[i], SimplifyEps);

            int outerIdx = -1;
            float outerArea = 0f;
            for (int i = 0; i < loops.Count; i++)
            {
                float a = Mathf.Abs(SignedArea(loops[i]));
                if (a > outerArea) { outerArea = a; outerIdx = i; }
            }
            if (outerIdx < 0 || loops[outerIdx].Count < 3)
                return null;

            var result = new Region { outerArea = outerArea * ctx.cell * ctx.cell, root = ctx.root };
            result.outer = ToWorld(loops[outerIdx], ctx);
            var outer2d = loops[outerIdx];
            for (int i = 0; i < loops.Count; i++)
            {
                if (i == outerIdx || loops[i].Count < 3)
                    continue;
                if (PointInPoly(outer2d, Centroid(loops[i])))
                    result.holes.Add(ToWorld(loops[i], ctx));
            }
            NearestBrush(ctx.records, seed, out var bt, out var rad);
            result.brushType = bt;
            result.radius = rad;
            return result;
        }

        // Tipo di pennello + raggio del tratto più vicino al seed (per far "copiare" al
        // riempimento lo stile dell'oggetto).
        static void NearestBrush(List<StrokeRecord> records, Vector3 seed,
            out BrushType brushType, out float radius)
        {
            brushType = BrushType.Round;
            radius = 0.006f;
            StrokeRecord nearest = null;
            float best = float.MaxValue;
            foreach (var rec in records)
            {
                var t = rec.transform;
                foreach (var p in rec.points)
                {
                    float d = (t.TransformPoint(p) - seed).sqrMagnitude;
                    if (d < best) { best = d; nearest = rec; }
                }
            }
            if (nearest != null)
            {
                brushType = nearest.brushType;
                float rmax = 0f;
                foreach (var r in nearest.radii) if (r > rmax) rmax = r;
                if (rmax > 1e-4f) radius = Mathf.Clamp(rmax, 0.003f, 0.02f);
            }
        }

        /// <summary>
        /// Anelli concentrici (spazio mondo) che riempiono l'area chiusa seguendone il contorno
        /// verso l'interno — da disegnare col pennello dell'oggetto (vedi Stroke.MakeRingFill).
        /// Null se non c'è area chiusa. Restituisce anche l'oggetto a cui agganciare il fill e
        /// il tipo/raggio di pennello copiati dal tratto più vicino al punto.
        /// </summary>
        public static List<List<Vector3>> FindRings(Vector3 seed, float searchRadius,
            out Transform root, out BrushType brushType, out float radius)
        {
            root = null; brushType = BrushType.Round; radius = 0.006f;
            var ctx = BuildMask(seed, searchRadius);
            if (ctx == null)
                return null;
            root = ctx.root;
            NearestBrush(ctx.records, seed, out brushType, out radius);

            // Distanza dal bordo; anelli = iso-contorni a passo = diametro del pennello.
            var dist = DistanceTransform(ctx.region, ctx.W, ctx.H);
            float maxD = 0f;
            foreach (var d in dist) if (d > maxD) maxD = d;
            float spacingPx = Mathf.Max(1f, radius * 2f / ctx.cell);
            int count = Mathf.Max(1, Mathf.CeilToInt(maxD / spacingPx));
            if (count > MaxRings) { count = MaxRings; spacingPx = maxD / MaxRings; }

            var rings = new List<List<Vector3>>();
            var mask = new bool[ctx.region.Length];
            for (int k = 0; k < count; k++)
            {
                float thr = k * spacingPx;
                bool any = false;
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = ctx.region[i] && dist[i] >= thr;
                    any |= mask[i];
                }
                if (!any)
                    break;
                foreach (var loop in ContourLoops(mask, ctx.W, ctx.H))
                {
                    var s = Simplify(loop, SimplifyEps);
                    if (s.Count >= 3)
                        rings.Add(ToWorld(s, ctx));
                }
            }
            return rings.Count > 0 ? rings : null;
        }

        // Distanza (in pixel, Euclidea approssimata) di ogni pixel dell'area dal bordo, con
        // due passate chamfer. I pixel fuori dall'area valgono 0.
        static float[] DistanceTransform(bool[] region, int W, int H)
        {
            const float BIG = 1e9f, D1 = 1f, D2 = 1.41421356f;
            var d = new float[W * H];
            for (int i = 0; i < d.Length; i++) d[i] = region[i] ? BIG : 0f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (!region[i]) continue;
                    float m = d[i];
                    if (x > 0) m = Mathf.Min(m, d[i - 1] + D1);
                    if (y > 0) m = Mathf.Min(m, d[i - W] + D1);
                    if (x > 0 && y > 0) m = Mathf.Min(m, d[i - W - 1] + D2);
                    if (x < W - 1 && y > 0) m = Mathf.Min(m, d[i - W + 1] + D2);
                    d[i] = m;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    int i = y * W + x;
                    if (!region[i]) continue;
                    float m = d[i];
                    if (x < W - 1) m = Mathf.Min(m, d[i + 1] + D1);
                    if (y < H - 1) m = Mathf.Min(m, d[i + W] + D1);
                    if (x < W - 1 && y < H - 1) m = Mathf.Min(m, d[i + W + 1] + D2);
                    if (x > 0 && y < H - 1) m = Mathf.Min(m, d[i + W - 1] + D2);
                    d[i] = m;
                }
            return d;
        }

        static void Accum(Vector2 p, ref float minx, ref float miny, ref float maxx, ref float maxy)
        {
            if (p.x < minx) minx = p.x; if (p.x > maxx) maxx = p.x;
            if (p.y < miny) miny = p.y; if (p.y > maxy) maxy = p.y;
        }

        // Disegna un segmento come linea spessa (dischetti lungo il percorso).
        static void StampSegment(bool[] wall, int W, int H, Vector2 a, Vector2 b,
            float minx, float miny, float cell, int tpx)
        {
            float ax = (a.x - minx) / cell, ay = (a.y - miny) / cell;
            float bx = (b.x - minx) / cell, by = (b.y - miny) / cell;
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(by - ay))) + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Stamp(wall, W, H, Mathf.RoundToInt(Mathf.Lerp(ax, bx, t)),
                                   Mathf.RoundToInt(Mathf.Lerp(ay, by, t)), tpx);
            }
        }

        static void Stamp(bool[] wall, int W, int H, int cx, int cy, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < W && y < H)
                        wall[y * W + x] = true;
                }
        }

        static bool NearestFree(bool[] wall, int W, int H, ref int sx, ref int sy, int maxR)
        {
            for (int r = 1; r <= maxR; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = sx + dx, y = sy + dy;
                        if (x >= 0 && y >= 0 && x < W && y < H && !wall[y * W + x])
                        { sx = x; sy = y; return true; }
                    }
            return false;
        }

        // Allaga l'area vuota da (sx,sy). Ritorna false se raggiunge il bordo (area aperta).
        static bool Flood(bool[] wall, bool[] region, int W, int H, int sx, int sy)
        {
            var q = new Queue<int>();
            int start = sy * W + sx;
            region[start] = true;
            q.Enqueue(start);
            bool bounded = true;
            while (q.Count > 0)
            {
                int idx = q.Dequeue();
                int x = idx % W, y = idx / W;
                if (x == 0 || y == 0 || x == W - 1 || y == H - 1)
                    bounded = false; // toccato il bordo: area non chiusa
                Visit(wall, region, q, W, H, x + 1, y);
                Visit(wall, region, q, W, H, x - 1, y);
                Visit(wall, region, q, W, H, x, y + 1);
                Visit(wall, region, q, W, H, x, y - 1);
            }
            return bounded;
        }

        static void Visit(bool[] wall, bool[] region, Queue<int> q, int W, int H, int x, int y)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = y * W + x;
            if (wall[i] || region[i]) return;
            region[i] = true;
            q.Enqueue(i);
        }

        // Dilatazione binaria: espande la regione di un disco di raggio r (per far arrivare
        // il riempimento fin sotto le linee, togliendo l'alone tra colore e contorno).
        static bool[] Dilate(bool[] src, int W, int H, int r)
        {
            if (r <= 0) return src;
            var dst = (bool[])src.Clone();
            int r2 = r * r;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!src[y * W + x]) continue;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (dx * dx + dy * dy > r2) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < W && ny < H)
                                dst[ny * W + nx] = true;
                        }
                }
            return dst;
        }

        // Contorni dell'area allagata come anelli ordinati (esterno + buchi), in coordinate
        // di griglia (angoli dei pixel). Segue i "bordi" tra pixel dentro/fuori l'area,
        // orientati con l'area a sinistra, e li concatena in cicli chiusi.
        static List<List<Vector2>> ContourLoops(bool[] region, int W, int H)
        {
            long stride = H + 2;
            long Key(int gx, int gy) => gx * stride + gy;
            var adj = new Dictionary<long, List<long>>();
            void Add(long s, long e)
            {
                if (!adj.TryGetValue(s, out var l)) adj[s] = l = new List<long>();
                l.Add(e);
            }
            bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && region[y * W + x];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!region[y * W + x]) continue;
                    if (!In(x - 1, y)) Add(Key(x, y + 1), Key(x, y));         // lato sinistro
                    if (!In(x + 1, y)) Add(Key(x + 1, y), Key(x + 1, y + 1)); // lato destro
                    if (!In(x, y - 1)) Add(Key(x, y), Key(x + 1, y));         // lato inferiore
                    if (!In(x, y + 1)) Add(Key(x + 1, y + 1), Key(x, y + 1)); // lato superiore
                }

            var loops = new List<List<Vector2>>();
            var starts = new List<long>(adj.Keys);
            foreach (var s0 in starts)
            {
                while (adj.TryGetValue(s0, out var l0) && l0.Count > 0)
                {
                    var loop = new List<Vector2>();
                    long cur = s0;
                    long next = Pop(adj, cur);
                    loop.Add(Corner(cur, stride));
                    int guard = W * H * 4 + 16;
                    while (next != s0 && guard-- > 0)
                    {
                        loop.Add(Corner(next, stride));
                        long n = Pop(adj, next);
                        if (n == long.MinValue) break;
                        next = n;
                    }
                    if (loop.Count >= 3) loops.Add(loop);
                }
            }
            return loops;
        }

        static long Pop(Dictionary<long, List<long>> adj, long key)
        {
            if (!adj.TryGetValue(key, out var l) || l.Count == 0) return long.MinValue;
            long e = l[l.Count - 1];
            l.RemoveAt(l.Count - 1);
            return e;
        }

        static Vector2 Corner(long key, long stride) => new(key / stride, key % stride);

        // Douglas-Peucker su un anello chiuso (tiene fissi il primo e l'ultimo vertice).
        static List<Vector2> Simplify(List<Vector2> pts, float eps)
        {
            int n = pts.Count;
            if (n < 4) return pts;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            DP(pts, 0, n - 1, eps * eps, keep);
            var outp = new List<Vector2>();
            for (int i = 0; i < n; i++) if (keep[i]) outp.Add(pts[i]);
            return outp;
        }

        static void DP(List<Vector2> pts, int first, int last, float eps2, bool[] keep)
        {
            if (last <= first + 1) return;
            Vector2 a = pts[first], b = pts[last];
            Vector2 ab = b - a; float len2 = ab.sqrMagnitude;
            float maxd = -1f; int idx = -1;
            for (int i = first + 1; i < last; i++)
            {
                Vector2 ap = pts[i] - a;
                float t = len2 > 1e-9f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / len2) : 0f;
                float d2 = (pts[i] - (a + t * ab)).sqrMagnitude;
                if (d2 > maxd) { maxd = d2; idx = i; }
            }
            if (maxd > eps2 && idx > 0)
            {
                keep[idx] = true;
                DP(pts, first, idx, eps2, keep);
                DP(pts, idx, last, eps2, keep);
            }
        }

        static float SignedArea(List<Vector2> loop)
        {
            float area = 0f; int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % n];
                area += a.x * b.y - a.y * b.x;
            }
            return area * 0.5f;
        }

        static Vector2 Centroid(List<Vector2> loop)
        {
            Vector2 s = Vector2.zero;
            foreach (var p in loop) s += p;
            return s / loop.Count;
        }

        // Angoli di griglia → mondo, sul piano di fit.
        static List<Vector3> ToWorld(List<Vector2> loop, RasterCtx c)
        {
            var w = new List<Vector3>(loop.Count);
            foreach (var g in loop)
                w.Add(c.seed + (c.minx + g.x * c.cell) * c.u + (c.miny + g.y * c.cell) * c.v);
            return w;
        }

        // Tratti (StrokeRecord) con un collider entro searchRadius dal seed, esclusi
        // punti-sfera e riempimenti.
        static List<StrokeRecord> GatherRecords(Vector3 seed, float searchRadius)
        {
            var result = new List<StrokeRecord>();
            int count = Physics.OverlapSphereNonAlloc(seed, searchRadius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count && result.Count < MaxRecords; i++)
            {
                var rec = overlapBuffer[i].GetComponentInParent<StrokeRecord>();
                if (rec == null || rec.isPoint || rec.isFill || rec.points.Count < 2)
                    continue;
                if (!rec.gameObject.activeInHierarchy)
                    continue;
                if (!result.Contains(rec))
                    result.Add(rec);
            }
            return result;
        }

        // Piano di best-fit attorno al seed (somma delle normali dei triangoli seed-a-b).
        static bool FitPlane(List<StrokeRecord> records, Vector3 seed,
            out Vector3 u, out Vector3 v, out Vector3 normal)
        {
            normal = Vector3.zero;
            foreach (var rec in records)
            {
                var t = rec.transform;
                for (int i = 1; i < rec.points.Count; i++)
                {
                    Vector3 a = t.TransformPoint(rec.points[i - 1]);
                    Vector3 b = t.TransformPoint(rec.points[i]);
                    normal += Vector3.Cross(a - seed, b - seed);
                }
            }
            if (normal.sqrMagnitude < 1e-10f)
            {
                u = v = Vector3.zero;
                return false;
            }
            normal.Normalize();
            u = Mathf.Abs(normal.y) < 0.99f
                ? Vector3.Cross(normal, Vector3.up).normalized
                : Vector3.Cross(normal, Vector3.right).normalized;
            v = Vector3.Cross(normal, u);
            return true;
        }

        static Vector2 Project(Vector3 p, Vector3 seed, Vector3 u, Vector3 v)
        {
            Vector3 d = p - seed;
            return new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v));
        }

        // Punto dentro un poligono 2D (crossing number).
        static bool PointInPoly(List<Vector2> poly, Vector2 pt)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 pi = poly[i], pj = poly[j];
                if ((pi.y > pt.y) != (pj.y > pt.y) &&
                    pt.x < (pj.x - pi.x) * (pt.y - pi.y) / (pj.y - pi.y) + pi.x)
                    inside = !inside;
            }
            return inside;
        }

        // ---- Ricolora-in-posto: riempimenti la cui area coperta contiene il seed ----

        /// <summary>
        /// Riempimenti (isFill) la cui AREA COPERTA (dentro il contorno, fuori dai buchi)
        /// contiene <paramref name="seed"/>, escluso <paramref name="exclude"/>. Per il
        /// ricolora-in-posto: si sostituisce il vecchio riempimento invece di sovrapporlo.
        /// </summary>
        public static List<GameObject> CoveringFills(Vector3 seed, GameObject exclude)
        {
            var result = new List<GameObject>();
            foreach (var rec in Object.FindObjectsByType<StrokeRecord>(FindObjectsInactive.Exclude))
            {
                if (!rec.isFill || rec.gameObject == exclude)
                    continue;
                if (PointInFill(rec, seed))
                    result.Add(rec.gameObject);
            }
            return result;
        }

        static bool PointInFill(StrokeRecord rec, Vector3 seed)
        {
            if (rec.points.Count < 3)
                return false;
            var t = rec.transform;
            var outer = new List<Vector3>(rec.points.Count);
            foreach (var p in rec.points)
                outer.Add(t.TransformPoint(p));
            if (!NewellPlane(outer, out var c, out var n, out var u, out var v))
                return false;
            if (Mathf.Abs(Vector3.Dot(seed - c, n)) > 0.15f)
                return false;
            Vector2 s = new(Vector3.Dot(seed - c, u), Vector3.Dot(seed - c, v));
            if (!PointInPoly(ProjectAll(outer, c, u, v), s))
                return false;
            foreach (var hole in rec.holes)
            {
                var hw = new List<Vector3>(hole.Count);
                foreach (var p in hole)
                    hw.Add(t.TransformPoint(p));
                if (PointInPoly(ProjectAll(hw, c, u, v), s))
                    return false; // nel buco: lì non copre
            }
            return true;
        }

        static List<Vector2> ProjectAll(List<Vector3> pts, Vector3 c, Vector3 u, Vector3 v)
        {
            var r = new List<Vector2>(pts.Count);
            foreach (var p in pts)
                r.Add(new Vector2(Vector3.Dot(p - c, u), Vector3.Dot(p - c, v)));
            return r;
        }

        static bool NewellPlane(List<Vector3> points, out Vector3 centroid,
            out Vector3 normal, out Vector3 u, out Vector3 v)
        {
            centroid = Vector3.zero;
            foreach (var p in points) centroid += p;
            centroid /= points.Count;
            normal = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i]; var b = points[(i + 1) % points.Count];
                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
            }
            if (normal.sqrMagnitude < 1e-12f)
            {
                normal = Vector3.up; u = v = Vector3.zero; return false;
            }
            normal.Normalize();
            u = Mathf.Abs(normal.y) < 0.99f
                ? Vector3.Cross(normal, Vector3.up).normalized
                : Vector3.Cross(normal, Vector3.right).normalized;
            v = Vector3.Cross(normal, u);
            return true;
        }
    }
}
