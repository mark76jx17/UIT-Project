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
        float startRadius;        // raggio al momento di Begin(), usato per il LOD
        int samplesSinceCollider;

        // LOD adattivo: tratti sottili usano meno lati (risparmio vertici),
        // tratti spessi ne usano di più (sembrano più rotondi).
        // Il Nastro è il "tubo" degenere a 2 lati: due facce contrapposte.
        int MeshSides
        {
            get
            {
                if (brushType == BrushType.Ribbon) return 2;
                if (startRadius < 0.003f) return 6;   // tratto sottile (<3 mm)
                if (startRadius > 0.008f) return 12;  // tratto spesso (>8 mm)
                return 8;                              // spessore medio
            }
        }

        readonly List<Vector3> rawPoints = new();
        readonly List<float> rawRadii = new();

        public Vector3 LastPoint { get; private set; }
        public int PointCount => mesher.PointCount;

        /// <summary>Inizia un nuovo tratto con colore e pennello correnti della palette.</summary>
        public static Stroke Begin(Vector3 start, float radius)
        {
            var stroke = Create(StrokeSettings.Color, StrokeSettings.Type);
            stroke.currentRadius = radius;
            stroke.startRadius = radius; // memorizzato per il LOD adattivo di MeshSides
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

        public void Finish()
        {
            if (rawPoints.Count >= 4)
            {
                // Lisciatura: ricampiona la polilinea e rigenera la mesh in un
                // colpo solo (un unico upload, vedi TubeMesher.AddRange).
                var smoothPoints = new List<Vector3>();
                var smoothRadii = new List<float>();
                StrokeSmoothing.Resample(rawPoints, rawRadii, SmoothSpacing, smoothPoints, smoothRadii);
                ApplyTaper(smoothPoints, smoothRadii);

                filter.mesh.Clear();
                mesher = new TubeMesher(filter.mesh, MeshSides);
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
