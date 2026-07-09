using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Un singolo tratto disegnato: un GameObject indipendente con la mesh a tubo
    /// generata incrementalmente (raggio per-campione, dalla pressione del
    /// trigger) e cap sferici alle estremità. A fine tratto la polilinea viene
    /// lisciata (Catmull-Rom) e la mesh rigenerata una volta sola.
    /// Tenere ogni tratto separato rende banali undo e cancellazione; i tratti
    /// "agganciati" col magnete vengono reparentati a quello esistente e si
    /// muovono come un unico oggetto.
    /// </summary>
    public class Stroke : MonoBehaviour
    {
        // Un collider di presa ogni N campioni: abbastanza fitti da afferrare
        // il tratto in un punto qualsiasi, abbastanza radi da restare leggeri.
        // (8 invece di 4: dimezza il numero di GameObject/collider per tratto;
        // il raggio di presa più generoso sotto compensa la maggiore spaziatura.)
        const int SamplesPerGrabCollider = 8;
        // Passo del ricampionamento Catmull-Rom a fine tratto.
        const float SmoothSpacing = 0.005f;

        TubeMesher mesher;
        MeshFilter filter;
        Material material;
        StrokeRecord record;
        BrushType brushType;
        float currentRadius;
        float startRadius;        // raggio al momento di Begin()
        float lodRadius;          // raggio MASSIMO del tratto: decide i lati della mesh
        Vector3 frameUp;          // "alto" del controller a inizio tratto: orienta il Nastro
        int samplesSinceCollider;

        // LOD adattivo: tratti sottili usano meno lati (risparmio vertici),
        // tratti spessi ne usano di più (sembrano più rotondi).
        // Il Nastro è il "tubo" degenere a 2 lati: due facce contrapposte.
        // Si usa il raggio MASSIMO (non quello iniziale): in modalità pressione un
        // tratto che parte sottile e si ingrossa non resta sfaccettato.
        int MeshSides
        {
            get
            {
                if (brushType == BrushType.Ribbon) return 2;
                if (lodRadius < 0.003f) return 6;   // tratto sottile (<3 mm)
                if (lodRadius > 0.008f) return 12;  // tratto spesso (>8 mm)
                return 8;                            // spessore medio
            }
        }

        static float MaxRadius(IReadOnlyList<float> radii)
        {
            float m = 0f;
            for (int i = 0; i < radii.Count; i++)
                if (radii[i] > m) m = radii[i];
            return m;
        }

        readonly List<Vector3> rawPoints = new();
        readonly List<float> rawRadii = new();

        public Vector3 LastPoint { get; private set; }
        public int PointCount => mesher.PointCount;

        /// <summary>Inizia un nuovo tratto con colore e pennello correnti della palette.</summary>
        public static Stroke Begin(Vector3 start, float radius, Vector3 up)
        {
            var stroke = Create(StrokeSettings.Color, StrokeSettings.Type);
            stroke.currentRadius = radius;
            stroke.startRadius = radius;
            stroke.lodRadius = radius; // aggiornato al massimo a fine tratto (Finish)
            stroke.frameUp = up;
            stroke.mesher.UpHint = up.sqrMagnitude > 1e-6f ? up : (Vector3?)null;
            stroke.mesher.UvScale = stroke.brushType == BrushType.Dashed ? DashUvScale(radius) : 1f;
            stroke.mesher.AddPoint(start, radius);
            stroke.rawPoints.Add(start);
            stroke.rawRadii.Add(radius);
            stroke.LastPoint = start;
            stroke.AddCap(start, radius);
            stroke.AddGrabCollider(start, radius);
            return stroke;
        }

        public void AddPoint(Vector3 point, float radius)
        {
            currentRadius = radius;
            mesher.AddPoint(point, radius);
            rawPoints.Add(point);
            rawRadii.Add(radius);
            LastPoint = point;
            if (++samplesSinceCollider >= SamplesPerGrabCollider)
            {
                samplesSinceCollider = 0;
                AddGrabCollider(point, radius);
            }
        }

        // Linea elastica: passo del ricampionamento del segmento durante il trascinamento.
        // Punti REALI (non solo 2 estremi) così gomma parziale, magnete e fill vedono una
        // polilinea normale; il tetto ai segmenti tiene basso il costo del rebuild per frame.
        const float LineSpacing = 0.01f;
        const int LineMaxSegs = 100;
        Transform lineEndCap;

        /// <summary>
        /// Linea elastica (modalità Line): sostituisce il contenuto del tratto col segmento
        /// start→end ricampionato. Chiamata ogni frame finché si tiene il trigger: la mesh
        /// viene rigenerata da capo (poche decine di anelli, costo trascurabile). Il cap di
        /// coda segue l'estremo; si chiama "Cap" così Finish lo ricrea con la rastrematura.
        /// </summary>
        public void SetLineEnd(Vector3 end, float radius)
        {
            Vector3 start = rawPoints[0];
            float startR = rawRadii[0];
            rawPoints.Clear();
            rawRadii.Clear();
            float len = Vector3.Distance(start, end);
            int segs = Mathf.Clamp(Mathf.CeilToInt(len / LineSpacing), 1, LineMaxSegs);
            for (int i = 0; i <= segs; i++)
            {
                float k = i / (float)segs;
                rawPoints.Add(Vector3.Lerp(start, end, k));
                rawRadii.Add(Mathf.Lerp(startR, radius, k));
            }
            currentRadius = radius;
            LastPoint = end;
            lodRadius = Mathf.Max(lodRadius, radius);

            filter.mesh.Clear();
            mesher = new TubeMesher(filter.mesh, MeshSides);
            mesher.UpHint = frameUp.sqrMagnitude > 1e-6f ? frameUp : (Vector3?)null;
            mesher.UvScale = brushType == BrushType.Dashed ? DashUvScale(lodRadius) : 1f;
            mesher.AddRange(rawPoints, rawRadii);

            if (lineEndCap == null)
            {
                var cap = new GameObject("Cap");
                cap.transform.SetParent(transform, false);
                cap.AddComponent<MeshFilter>().sharedMesh = BrushMeshes.Sphere();
                cap.AddComponent<MeshRenderer>().sharedMaterial = material;
                lineEndCap = cap.transform;
            }
            lineEndCap.position = end;
            lineEndCap.localScale = Vector3.one * radius * 2f;
        }

        /// <summary>
        /// Chiusura di un tratto in modalità Line: come Finish, più i collider di presa
        /// lungo il segmento (per i tratti normali li semina AddPoint man mano; qui i punti
        /// arrivano tutti insieme da SetLineEnd, quindi vanno aggiunti a posteriori).
        /// </summary>
        public void FinishLine()
        {
            Finish();
            for (int i = SamplesPerGrabCollider; i < rawPoints.Count - 1; i += SamplesPerGrabCollider)
                AddGrabCollider(rawPoints[i], rawRadii[i]);
        }

        public void Finish()
        {
            mesher.Flush(); // carica gli ultimi anelli rimasti per il throttling dell'upload
            if (rawPoints.Count >= 4)
            {
                // Lisciatura: ricampiona la polilinea e rigenera la mesh in un
                // colpo solo (un unico upload, vedi TubeMesher.AddRange).
                var smoothPoints = new List<Vector3>();
                var smoothRadii = new List<float>();
                StrokeSmoothing.Resample(rawPoints, rawRadii, SmoothSpacing, smoothPoints, smoothRadii);
                ApplyTaper(smoothPoints, smoothRadii);

                // I lati della mesh definitiva dipendono dal raggio massimo del tratto
                // (la rastrematura riduce gli estremi: si usa il max PRIMA del taper).
                lodRadius = MaxRadius(rawRadii);
                filter.mesh.Clear();
                mesher = new TubeMesher(filter.mesh, MeshSides);
                mesher.UpHint = frameUp.sqrMagnitude > 1e-6f ? frameUp : (Vector3?)null;
                mesher.UvScale = brushType == BrushType.Dashed ? DashUvScale(lodRadius) : 1f;
                mesher.AddRange(smoothPoints, smoothRadii);

                rawPoints.Clear();
                rawPoints.AddRange(smoothPoints);
                rawRadii.Clear();
                rawRadii.AddRange(smoothRadii);
                LastPoint = smoothPoints[^1];

                // I cap vanno rifatti con i raggi rastremati.
                foreach (Transform child in transform)
                    if (child.name == "Cap")
                        Destroy(child.gameObject);
                AddCap(smoothPoints[0], smoothRadii[0]);
                AddCap(LastPoint, smoothRadii[^1]);
            }
            else
            {
                AddCap(LastPoint, currentRadius);
            }

            AddGrabCollider(LastPoint, currentRadius);

            record.points.AddRange(rawPoints);
            record.radii.AddRange(rawRadii);
        }

        // Rastrematura alle estremità, stile Gravity Sketch: il raggio scende
        // dolcemente verso la punta nei primi/ultimi ~2 cm di percorso.
        static void ApplyTaper(List<Vector3> points, List<float> radii)
        {
            int count = points.Count;
            if (count < 3)
                return;
            var cumulative = new float[count];
            for (int i = 1; i < count; i++)
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i], points[i - 1]);
            float total = cumulative[count - 1];
            float taperLength = Mathf.Min(0.02f, total / 3f);
            if (taperLength <= 1e-5f)
                return;

            for (int i = 0; i < count; i++)
            {
                float fromEnd = Mathf.Min(cumulative[i], total - cumulative[i]);
                float t = fromEnd / taperLength;
                if (t < 1f)
                    radii[i] *= Mathf.Lerp(0.12f, 1f, Mathf.SmoothStep(0f, 1f, t));
            }
        }

        /// <summary>
        /// Genera la superficie di riempimento dal contorno del tratto, usando
        /// il colore salvato in record.fillColor. Chiamato al caricamento e da
        /// FillWith. Ritorna l'oggetto creato (o null se il contorno è degenere).
        /// </summary>
        public GameObject CreateFill()
        {
            var fill = FillSurface.Build(rawPoints, BrushMaterials.Get(record.fillColor, BrushType.Round));
            if (fill == null)
                return null;
            fill.transform.SetParent(transform, false);
            record.filled = true;
            return fill;
        }

        /// <summary>Il tratto forma un anello chiuso (estremi entro la soglia)?</summary>
        public bool IsCloseable(float threshold) =>
            record != null && rawPoints.Count >= 3 &&
            Vector3.Distance(rawPoints[0], rawPoints[^1]) <= threshold;

        /// <summary>
        /// Riempimento "a secchiello": riempie il tratto col colore dato se è
        /// una linea chiusa. Ritorna la superficie creata (per la history) o null.
        /// </summary>
        public GameObject FillWith(Color color, float closeThreshold)
        {
            if (!IsCloseable(closeThreshold))
                return null;
            record.fillColor = color;
            return CreateFill();
        }

        // Tratteggio proporzionale allo spessore: più spesso → trattini più lunghi.
        static float DashUvScale(float radius)
            => Mathf.Clamp(0.005f / Mathf.Max(radius, 0.001f), 0.3f, 2f);

        /// <summary>
        /// Cancellazione PARZIALE: toglie i punti entro 'radius' da 'center' e ricostruisce
        /// i segmenti rimasti come nuovi tratti (0, 1 o 2 pezzi). Ritorna true se ha tolto
        /// qualcosa; 'pieces' sono i nuovi oggetti. Il chiamante nasconde l'originale.
        /// Salta tratti-punto e tratti riempiti (per quelli si cancella tutto).
        /// </summary>
        // Fonte dati per la gomma: lo StrokeRecord serializzato (points/radii coincidono con
        // rawPoints dopo Finish, e — a differenza dei campi runtime privati — sopravvive a
        // un Object.Instantiate del tratto, vedi EraseNodeAndRebuild).
        StrokeRecord Record => record != null ? record : record = GetComponent<StrokeRecord>();

        public bool TryEraseSphere(Vector3 center, float radius, out List<GameObject> pieces,
            bool ensureProgress = false)
        {
            pieces = new List<GameObject>();
            var rec = Record;
            if (rec == null || rec.isPoint || rec.filled || rec.points.Count < 2
                || rec.radii.Count != rec.points.Count)
                return false;

            // I punti sono in spazio locale del tratto: porto centro e raggio lì.
            Vector3 localCenter = transform.InverseTransformPoint(center);
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 1e-4f);
            float r = radius / scale;
            float r2 = r * r;

            // Prima passata: quali punti cadono nella sfera. Con ensureProgress, se la sfera
            // non contiene nessun punto ma il tratto è stato comunque "toccato" (il collider di
            // presa è più largo del passo dei campioni), si toglie almeno il punto più vicino:
            // la gomma che tocca deve sempre mordere qualcosa.
            int n = rec.points.Count;
            var remove = new bool[n];
            bool removedAny = false;
            int nearest = -1;
            float nearestD2 = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d2 = (rec.points[i] - localCenter).sqrMagnitude;
                if (d2 <= r2) { remove[i] = true; removedAny = true; }
                if (d2 < nearestD2) { nearestD2 = d2; nearest = i; }
            }
            if (!removedAny && ensureProgress && nearest >= 0)
            {
                float reach = r + 0.025f / scale; // tolleranza ≈ raggio del collider di presa
                if (nearestD2 <= reach * reach)
                {
                    remove[nearest] = true;
                    removedAny = true;
                }
            }
            if (!removedAny)
                return false;

            var runP = new List<Vector3>();
            var runR = new List<float>();
            for (int i = 0; i < n; i++)
            {
                if (remove[i])
                    EmitPiece(runP, runR, pieces);
                else
                {
                    runP.Add(rec.points[i]);
                    runR.Add(rec.radii[i]);
                }
            }
            EmitPiece(runP, runR, pieces);
            return true;
        }

        // Ricostruisce un pezzo dai punti rimasti e lo riallinea alla posa dell'originale.
        void EmitPiece(List<Vector3> p, List<float> r, List<GameObject> pieces)
        {
            if (p.Count >= 2)
            {
                var piece = Rebuild(p.ToArray(), r.ToArray(), Record.color, Record.brushType);
                piece.transform.SetPositionAndRotation(transform.position, transform.rotation);
                // lossyScale, non localScale: i punti del pezzo sono nello spazio locale del
                // tratto ma il pezzo nasce SENZA genitore — con la sola scala locale un tratto
                // dentro un gruppo scalato (o figlio di una fusione) verrebbe ricostruito
                // rimpicciolito/fuori posto.
                piece.transform.localScale = transform.lossyScale;
                pieces.Add(piece.gameObject);
            }
            p.Clear();
            r.Clear();
        }

        // Distrugge un Object sia a runtime sia in edit mode (i tool diagnostici da Editor
        // esercitano questa logica senza Play, dove Destroy non è permesso).
        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // Figli "di contorno" del tratto (geometria propria, non sotto-oggetti fusi).
        static bool IsDecoration(Transform t) => t.name == "Cap" || t.name == "GrabCollider";

        /// <summary>C'è almeno un sotto-oggetto "vero" (tratto/fill fuso) sotto questo nodo?</summary>
        public static bool HasRealChildren(Transform node)
        {
            foreach (Transform kid in node)
                if (kid.gameObject.activeSelf && !IsDecoration(kid))
                    return true;
            return false;
        }

        /// <summary>
        /// Gomma parziale su un NODO di un gruppo fuso: erode la geometria propria del nodo
        /// (TryEraseSphere) e ricostruisce il nodo come pezzi superstiti + CLONI fedeli
        /// (Object.Instantiate, posa mondo conservata) dei suoi sotto-oggetti fusi. Il chiamante
        /// nasconde il nodo originale e registra PushReplace([nodo], [risultato]) — un solo
        /// passo di undo, gerarchia originale intatta per il ripristino.
        /// erodible = false → il nodo non è un tratto erodibile (fill/punto/anelli): il
        /// chiamante decide (tipicamente lo nasconde per intero). Ritorna null se non
        /// sopravvive nulla.
        /// </summary>
        public static GameObject EraseNodeAndRebuild(Transform node, Vector3 center, float radius,
            out bool erodible)
        {
            var stroke = node.GetComponent<Stroke>();
            var pieces = new List<GameObject>();
            erodible = stroke != null
                && stroke.TryEraseSphere(center, radius, out pieces, ensureProgress: true);
            if (!erodible)
            {
                foreach (var p in pieces)
                    Kill(p);
                return null;
            }

            // Cloni dei sotto-oggetti fusi (tratti/fill/anelli), con posa mondo conservata.
            // I figli inattivi (pezzi già annullati, in mano alla history) non si clonano; i
            // discendenti inattivi dei cloni si potano (sarebbero copie orfane invisibili).
            var clones = new List<GameObject>();
            foreach (Transform kid in node)
            {
                if (!kid.gameObject.activeSelf || IsDecoration(kid))
                    continue;
                var clone = Instantiate(kid.gameObject, kid.position, kid.rotation);
                clone.name = kid.name;
                clone.transform.localScale = kid.lossyScale;
                foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                    if (t != null && t != clone.transform && !t.gameObject.activeSelf)
                        Kill(t.gameObject);
                clones.Add(clone);
            }

            // Radice del risultato: il primo pezzo (ha già DrawnItem da Rebuild), o il primo
            // clone se il tratto del nodo è stato eroso per intero.
            GameObject root = null;
            if (pieces.Count > 0)
            {
                root = pieces[0];
                for (int i = 1; i < pieces.Count; i++)
                    Reparent(pieces[i], root);
            }
            foreach (var clone in clones)
            {
                if (root == null)
                {
                    root = clone;
                    if (root.GetComponent<DrawnItem>() == null)
                        root.AddComponent<DrawnItem>();
                }
                else
                    Reparent(clone, root);
            }
            return root;
        }

        // Aggancia un superstite alla radice del risultato mantenendo la posa nel mondo;
        // un solo DrawnItem per gruppo (quello della radice).
        static void Reparent(GameObject go, GameObject root)
        {
            go.transform.SetParent(root.transform, true);
            var item = go.GetComponent<DrawnItem>();
            if (item != null)
                Kill(item);
        }

        /// <summary>
        /// Riempimento di un anello formato da PIÙ tratti uniti (col magnete a tempo di
        /// disegno o unendo oggetti già disegnati): concatena i contorni dei tratti figli
        /// di 'root' in UN unico anello, CHIUDENDO le piccole fessure (gap-closing), e se
        /// l'anello si chiude costruisce la superficie. Tollera tratti disegnati in ordine
        /// e verso qualsiasi. Ritorna il fill o null (anello aperto/degenere → nessun danno).
        /// </summary>
        public static GameObject FillGroup(Transform root, Color color, float closeThreshold)
        {
            var strokes = root.GetComponentsInChildren<Stroke>();
            if (strokes.Length < 2)
                return null;

            // Converti ogni stroke figlio in punti nello spazio locale della root.
            var segments = new List<List<Vector3>>();

            foreach (var s in strokes)
            {
                if (s.rawPoints.Count < 2)
                    continue;

                var seg = new List<Vector3>(s.rawPoints.Count);
                foreach (var p in s.rawPoints)
                    seg.Add(root.InverseTransformPoint(s.transform.TransformPoint(p)));

                segments.Add(seg);
            }

            if (segments.Count < 2)
                return null;

            // Costruisci il contorno scegliendo ogni volta il segmento più vicino
            // all'estremo corrente. Se conviene, lo aggiunge al contrario.
            var pts = new List<Vector3>(segments[0]);
            segments.RemoveAt(0);

            while (segments.Count > 0)
            {
                Vector3 tail = pts[^1];

                int bestIndex = -1;
                bool reverse = false;
                float bestDist = float.MaxValue;

                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];

                    float dStart = Vector3.Distance(tail, seg[0]);
                    if (dStart < bestDist)
                    {
                        bestDist = dStart;
                        bestIndex = i;
                        reverse = false;
                    }

                    float dEnd = Vector3.Distance(tail, seg[^1]);
                    if (dEnd < bestDist)
                    {
                        bestDist = dEnd;
                        bestIndex = i;
                        reverse = true;
                    }
                }

                if (bestIndex < 0 || bestDist > closeThreshold)
                    return null;

                var chosen = segments[bestIndex];
                segments.RemoveAt(bestIndex);

                if (reverse)
                    chosen.Reverse();

                // Evita di duplicare il punto di giunzione.
                int start = Vector3.Distance(pts[^1], chosen[0]) < 0.005f ? 1 : 0;
                for (int i = start; i < chosen.Count; i++)
                    pts.Add(chosen[i]);
            }

            if (pts.Count < 3 || Vector3.Distance(pts[0], pts[^1]) > closeThreshold)
                return null;

            var fill = FillSurface.Build(pts, BrushMaterials.Get(color, BrushType.Round));
            if (fill == null)
                return null;

            fill.transform.SetParent(root, false);
            return fill;
        }

        /// <summary>I tratti del gruppo sotto 'root' formerebbero un anello riempibile?
        /// Usato per l'anteprima hover, senza costruire la mesh.</summary>
        public static bool IsGroupFillable(Transform root, float closeThreshold)
            => AssembleGroupContour(root, closeThreshold) != null;

        /// <summary>
        /// Concatena i tratti del gruppo sotto 'root' in un unico anello chiuso, nello
        /// spazio locale di 'root'. Catena greedy: parte da un tratto e attacca ogni volta
        /// quello con l'estremo più vicino all'estremo libero corrente (entro
        /// 'joinThreshold', invertendolo se serve). Ritorna l'anello se gli estremi finali
        /// si richiudono entro la soglia, altrimenti null.
        /// </summary>
        static List<Vector3> AssembleGroupContour(Transform root, float joinThreshold)
        {
            // Polilinee dei tratti, portate nello spazio locale della radice (saltando
            // i punti-sfera, i riempimenti e i tratti troppo corti).
            var lines = new List<List<Vector3>>();
            foreach (var s in root.GetComponentsInChildren<Stroke>())
            {
                if (s.record == null || s.record.isPoint || s.rawPoints.Count < 2)
                    continue;
                var line = new List<Vector3>(s.rawPoints.Count);
                foreach (var p in s.rawPoints)
                    line.Add(root.InverseTransformPoint(s.transform.TransformPoint(p)));
                lines.Add(line);
            }
            if (lines.Count == 0)
                return null;

            float joinSqr = joinThreshold * joinThreshold;

            var used = new bool[lines.Count];
            var chain = new List<Vector3>(lines[0]);
            used[0] = true;
            int remaining = lines.Count - 1;

            while (remaining > 0)
            {
                Vector3 tail = chain[^1];
                int best = -1;
                bool reverse = false;
                float bestSqr = joinSqr;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (used[i])
                        continue;
                    float dStart = (lines[i][0] - tail).sqrMagnitude;
                    float dEnd = (lines[i][^1] - tail).sqrMagnitude;
                    if (dStart < bestSqr) { bestSqr = dStart; best = i; reverse = false; }
                    if (dEnd < bestSqr) { bestSqr = dEnd; best = i; reverse = true; }
                }
                if (best < 0)
                    break; // nessun tratto abbastanza vicino: catena interrotta

                var next = lines[best];
                if (reverse)
                    next.Reverse();
                for (int i = 1; i < next.Count; i++) // salta il punto di giunzione (coincide ~ con la coda)
                    chain.Add(next[i]);
                used[best] = true;
                remaining--;
            }

            // Anello chiuso? estremi finali entro la soglia.
            if (chain.Count < 3 || (chain[0] - chain[^1]).sqrMagnitude > joinSqr)
                return null;
            return chain;
        }

        /// <summary>
        /// Crea un oggetto di riempimento di REGIONE indipendente (vedi FillRegion):
        /// contorno esterno + eventuali buchi, afferrabile e salvabile a sé. In spazio mondo.
        /// </summary>
        public static GameObject MakeCellFill(FillRegion.Region region, Color color)
            => BuildFillObject(region.outer, region.holes, color, region.brushType, selectable: true);

        /// <summary>Riempimento (con eventuali buchi) da agganciare a un GRUPPO: nessun
        /// DrawnItem/collider proprio, si afferra dai tratti del gruppo. Il contorno è nello
        /// spazio locale della radice a cui verrà agganciato.</summary>
        public static GameObject MakeGroupFill(IReadOnlyList<Vector3> outer,
            List<List<Vector3>> holes, Color color, BrushType brushType)
            => BuildFillObject(outer, holes, color, brushType, selectable: false);

        /// <summary>
        /// Riempimento "col pennello": anelli concentrici (spazio mondo, vedi
        /// FillRegion.FindRings) disegnati col TIPO di pennello dell'oggetto e col colore dato.
        /// Ogni anello è un tubo col materiale del pennello. NESSUNO StrokeRecord → non è un
        /// "muro" per i riempimenti futuri e non si salva (prototipo). DrawnItem + collider di
        /// presa sull'anello esterno per afferrarlo/cancellarlo.
        /// </summary>
        public static GameObject MakeRingFill(List<List<Vector3>> rings, Color color,
            BrushType brushType, float radius)
        {
            var root = new GameObject("Fill");
            root.AddComponent<DrawnItem>();
            var material = BrushMaterials.Get(color, brushType);
            int sides = brushType == BrushType.Ribbon ? 2
                      : radius < 0.003f ? 6 : radius > 0.008f ? 12 : 8;

            foreach (var ring in rings)
            {
                if (ring.Count < 3)
                    continue;
                var pts = new List<Vector3>(ring) { ring[0] }; // chiude l'anello
                var radii = new List<float>(pts.Count);
                for (int i = 0; i < pts.Count; i++)
                    radii.Add(radius);

                var mesh = new Mesh { name = "FillRing" };
                new TubeMesher(mesh, sides).AddRange(pts, radii);

                var child = new GameObject("Ring");
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            // Collider di presa lungo l'anello esterno (il primo), per afferrare/cancellare.
            if (rings.Count > 0)
                foreach (var p in EverySo(rings[0], 4))
                {
                    var node = new GameObject("GrabCollider");
                    node.transform.SetParent(root.transform, false);
                    node.transform.position = p;
                    var col = node.AddComponent<SphereCollider>();
                    col.isTrigger = true;
                    col.radius = Mathf.Max(radius * 1.5f, 0.022f);
                }
            return root;
        }

        static IEnumerable<Vector3> EverySo(List<Vector3> pts, int step)
        {
            for (int i = 0; i < pts.Count; i += step)
                yield return pts[i];
        }

        /// <summary>Ricostruisce una superficie di riempimento dai dati salvati (vedi
        /// DrawingStore). 'selectable' = riempimento indipendente (con DrawnItem e collider
        /// di presa); false = riempimento di gruppo (figlio della sua radice).</summary>
        public static GameObject RebuildFill(IReadOnlyList<Vector3> outer,
            List<List<Vector3>> holes, Color color, BrushType brushType, bool selectable)
            => BuildFillObject(outer, holes, color, brushType, selectable);

        // Costruisce la mesh di riempimento (con eventuali buchi) + lo StrokeRecord per
        // salvarla/ricaricarla. Il fill COPIA il tipo di pennello dell'oggetto (es. Glow →
        // brilla). Se 'selectable', aggiunge DrawnItem e collider di presa lungo il contorno.
        static GameObject BuildFillObject(IReadOnlyList<Vector3> outer,
            List<List<Vector3>> holes, Color color, BrushType brushType, bool selectable)
        {
            var pts = new List<Vector3>(outer);
            bool hasHoles = holes != null && holes.Count > 0;
            // Il tratteggio su una superficie piatta non ha UV → reso come Round per non sfarfallare.
            var matType = brushType == BrushType.Dashed ? BrushType.Round : brushType;
            var material = BrushMaterials.Get(color, matType);
            var fill = (hasHoles ? FillSurface.BuildWithHoles(pts, holes, material)
                                 : FillSurface.Build(pts, material))
                       ?? new GameObject("Fill"); // degenere: GO vuoto (mantiene l'allineamento indici al load)

            var rec = fill.AddComponent<StrokeRecord>();
            rec.isFill = true;
            rec.color = color;
            rec.fillColor = color;
            rec.brushType = brushType;
            rec.points.AddRange(pts);
            if (hasHoles)
                foreach (var h in holes)
                    rec.holes.Add(new List<Vector3>(h));

            if (selectable && fill.GetComponent<MeshFilter>() != null)
            {
                fill.AddComponent<DrawnItem>();
                for (int i = 0; i < pts.Count; i += 4) // collider radi sul contorno: presa dal bordo
                {
                    var node = new GameObject("GrabCollider");
                    node.transform.SetParent(fill.transform, false);
                    node.transform.localPosition = pts[i];
                    var col = node.AddComponent<SphereCollider>();
                    col.isTrigger = true;
                    col.radius = 0.02f;
                }
            }
            return fill;
        }

        /// <summary>Punto singolo (tap del trigger): una sfera.</summary>
        public static GameObject CreatePoint(Vector3 position, float radius)
            => CreatePoint(position, radius, StrokeSettings.Color, StrokeSettings.Type);

        public static GameObject CreatePoint(Vector3 position, float radius, Color color, BrushType type)
        {
            // Sulle sfere il tratteggio/nastro non hanno senso: resta solo il glow.
            if (type != BrushType.Glow)
                type = BrushType.Round;

            // Sfera low-poly condivisa invece della primitiva Unity (~515 vert):
            // stesso raggio (0.5) e collider, una frazione dei vertici.
            var sphere = new GameObject("StrokePoint");
            sphere.AddComponent<MeshFilter>().sharedMesh = BrushMeshes.Sphere();
            sphere.AddComponent<MeshRenderer>().sharedMaterial = BrushMaterials.Get(color, type);
            var pointCollider = sphere.AddComponent<SphereCollider>();
            pointCollider.isTrigger = true; // raggio default 0.5 (locale) = mesh
            sphere.transform.position = position;
            sphere.transform.localScale = Vector3.one * radius * 3f;
            sphere.AddComponent<DrawnItem>();
            var record = sphere.AddComponent<StrokeRecord>();
            record.isPoint = true;
            record.color = color;
            record.brushType = type;
            record.radii.Add(radius);
            return sphere;
        }

        /// <summary>Ricostruisce un tratto dai dati salvati (vedi DrawingStore).</summary>
        public static Stroke Rebuild(IReadOnlyList<Vector3> points, IReadOnlyList<float> radii,
            Color color, BrushType type)
        {
            var stroke = Create(color, type);
            // Lati corretti in base al raggio massimo salvato (Create parte a 6 lati):
            // ricrea il mesher prima di riempirlo, o i tratti caricati sarebbero sempre
            // a 6 lati anche se spessi.
            stroke.lodRadius = MaxRadius(radii);
            stroke.filter.mesh.Clear();
            stroke.mesher = new TubeMesher(stroke.filter.mesh, stroke.MeshSides);
            stroke.mesher.UvScale = type == BrushType.Dashed ? DashUvScale(stroke.lodRadius) : 1f;
            stroke.mesher.AddRange(points, radii);
            stroke.rawPoints.AddRange(points);
            stroke.rawRadii.AddRange(radii);
            stroke.currentRadius = radii[^1];
            stroke.LastPoint = points[points.Count - 1];

            stroke.AddCap(points[0], radii[0]);
            stroke.AddCap(stroke.LastPoint, stroke.currentRadius);
            for (int i = 0; i < points.Count; i += SamplesPerGrabCollider)
                stroke.AddGrabCollider(points[i], radii[i]);

            stroke.record.points.AddRange(points);
            stroke.record.radii.AddRange(radii);
            return stroke;
        }

        static Stroke Create(Color color, BrushType type)
        {
            var go = new GameObject("Stroke");
            var stroke = go.AddComponent<Stroke>();
            stroke.brushType = type;
            stroke.material = BrushMaterials.Get(color, type);

            stroke.filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.material = stroke.material;
            stroke.filter.mesh = new Mesh { name = "StrokeTube" };
            stroke.mesher = new TubeMesher(stroke.filter.mesh, stroke.MeshSides);

            go.AddComponent<DrawnItem>();
            stroke.record = go.AddComponent<StrokeRecord>();
            stroke.record.color = color;
            stroke.record.fillColor = color; // default: stesso colore del contorno
            stroke.record.brushType = type;
            return stroke;
        }

        // Cap sferico: chiude l'estremità del tubo e arrotonda l'attacco, stile Tilt Brush.
        // Usa la sfera low-poly condivisa (niente primitiva da ~515 vert, niente
        // collider da creare e subito distruggere) e il materiale condiviso del tratto.
        void AddCap(Vector3 position, float radius)
        {
            var cap = new GameObject("Cap");
            cap.transform.SetParent(transform, false);
            cap.transform.position = position;
            cap.transform.localScale = Vector3.one * radius * 2f;
            cap.AddComponent<MeshFilter>().sharedMesh = BrushMeshes.Sphere();
            cap.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        void AddGrabCollider(Vector3 position, float radius)
        {
            var go = new GameObject("GrabCollider");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var collider = go.AddComponent<SphereCollider>();
            collider.isTrigger = true; // niente interferenze con la fisica della scena
            // Raggio più generoso: con i collider più radi (ogni 8 campioni) serve
            // più copertura per non lasciare "buchi" dove il tratto non si afferra.
            collider.radius = Mathf.Max(radius * 1.5f, 0.022f);
        }
    }
}
