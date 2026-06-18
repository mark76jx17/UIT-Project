using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Pennello sulla mano destra. Trigger premuto = tratto in corso
    /// (campionamento a distanza minima + smoothing), rilascio = fine tratto.
    /// Un tap rapido senza movimento produce un punto singolo (sfera).
    /// La pressione del trigger dà lo spessore, modulato lungo il tratto.
    /// Un cursore sferico sulla punta mostra colore e spessore correnti.
    /// In modalità gomma il trigger cancella l'oggetto toccato.
    /// </summary>
    public class BrushController : MonoBehaviour
    {
        [Header("Punta del pennello (offset in avanti dal controller)")]
        [Tooltip("Posizione della punta rispetto al controller: in avanti (Z) e poco sotto (Y), come la punta di una penna fuori dalla mano.")]
        [SerializeField] Vector3 tipOffset = new(0f, -0.01f, 0.08f);

        [Header("Trigger (isteresi)")]
        [SerializeField] float pressThreshold = 0.55f;
        [SerializeField] float releaseThreshold = 0.35f;

        [Header("Campionamento")]
        [Tooltip("Distanza minima tra due campioni: da fermo non si accumulano punti.")]
        [SerializeField] float minSampleDistance = 0.005f;
        [Range(0f, 1f)]
        [Tooltip("Peso del nuovo campione nella media mobile (1 = nessuno smoothing).")]
        [SerializeField] float smoothing = 0.5f;

        [Header("Tap = punto singolo")]
        [SerializeField] float tapMaxDuration = 0.25f;
        [SerializeField] float tapMaxMovement = 0.01f;

        [Header("Spessore")]
        [Range(0f, 1f)]
        [Tooltip("Frazione minima del raggio premendo appena (solo con Pressure ON).")]
        [SerializeField] float minPressureFraction = 0.25f;
        [Range(0f, 1f)]
        [Tooltip("Quanto velocemente il raggio insegue la pressione (1 = subito).")]
        [SerializeField] float radiusSmoothing = 0.3f;

        [Header("Magnete: unisci al tratto vicino")]
        [SerializeField] bool mergeNearbyStrokes = true;
        [Tooltip("Se il tratto inizia entro questa distanza da un oggetto disegnato, ci si aggancia e diventano un unico oggetto.")]
        [SerializeField] float snapRadius = 0.025f;

        [Header("Fill (secchiello)")]
        [Tooltip("Distanza massima tra gli estremi perché un tratto sia 'chiuso' e riempibile.")]
        [SerializeField] float fillCloseThreshold = 0.05f;

        static readonly Color EraserCursorColor = new(0.45f, 0.45f, 0.45f, 0.7f);

        public bool IsDrawing { get; private set; }

        /// <summary>Punta del pennello (in avanti dal controller): bersaglio di prossimità
        /// per la ruota colori e punto di poke sulla palette.</summary>
        public Transform Tip { get; private set; }

        /// <summary>Quando true il pennello non disegna (es. mentre il ray punta la palette).</summary>
        public bool SuppressDrawing { get; set; }

        /// <summary>
        /// Se impostato, sostituisce la lettura del trigger dal controller:
        /// usato dal simulatore desktop per testare senza visore.
        /// </summary>
        public float? TriggerOverride { get; set; }

        Stroke current;
        Stroke mirrored; // gemello speculare, se lo specchio è attivo
        Vector3 smoothed;
        Vector3 pressPosition;
        float pressTime;
        bool pressed;
        float currentRadius;
        Transform mergeTarget;

        bool fillConsumed;     // un solo riempimento per pressione del trigger
        Transform fillHover;   // tratto chiuso attualmente evidenziato in modo Fill

        Transform cursor;
        Material cursorMaterial;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            // Punta del pennello: piccolo trigger collider con cui premere i
            // pulsanti fisici della palette sull'altra mano.
            var tip = new GameObject("BrushTip");
            tip.transform.SetParent(transform, false);
            tip.transform.localPosition = tipOffset; // punta in avanti, fuori dal controller
            tip.AddComponent<BrushTip>();
            var collider = tip.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.012f;
            var body = tip.AddComponent<Rigidbody>();
            body.isKinematic = true;
            Tip = tip.transform;

            // Cursore: sfera senza collider che mostra colore e spessore correnti,
            // posizionata sulla punta (non al centro del controller).
            var cursorGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cursorGO.name = "BrushCursor";
            Destroy(cursorGO.GetComponent<Collider>());
            cursorGO.transform.SetParent(transform, false);
            cursorGO.transform.localPosition = tipOffset;
            cursorMaterial = BrushMaterials.CreateUnlit(StrokeSettings.Color);
            cursorGO.GetComponent<MeshRenderer>().material = cursorMaterial;
            cursor = cursorGO.transform;
        }

        void Update()
        {
            float trigger = TriggerOverride
                            ?? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, StrokeSettings.BrushHand);
            Vector3 position = Tip != null ? Tip.position : transform.position;

            UpdateCursor(trigger);

            // Mentre il ray punta la palette (o altro consumer lo richiede) il pennello
            // non disegna: chiudi un eventuale tratto in corso ed esci.
            if (SuppressDrawing)
            {
                if (pressed)
                    EndPress(position);
                return;
            }

            if (StrokeSettings.FillMode)
            {
                // Secchiello: il trigger riempie la linea chiusa puntata, non disegna.
                if (pressed)
                    EndPress(position);
                UpdateFillHover(position);
                if (trigger >= pressThreshold && !fillConsumed)
                {
                    FillAt(position);
                    fillConsumed = true;
                }
                else if (trigger <= releaseThreshold)
                {
                    fillConsumed = false;
                }
                return;
            }
            if (fillHover != null) // uscito dal modo Fill: spegni l'evidenziazione
            {
                StrokeHighlight.Clear(fillHover);
                fillHover = null;
            }

            if (StrokeSettings.EraserMode)
            {
                // Cambio modalità a metà tratto: chiudi il tratto in corso.
                if (pressed)
                    EndPress(position);
                if (trigger >= pressThreshold)
                    EraseAt(position);
                return;
            }

            if (!pressed && trigger >= pressThreshold)
                BeginPress(position, trigger);
            else if (pressed && trigger <= releaseThreshold)
                EndPress(position);
            else if (pressed)
                ContinuePress(position, trigger);
        }

        // Lo slider "size" decide lo spessore base (vale sempre); con Pressure ON
        // la pressione del trigger lo modula da minPressureFraction a 1.
        float TriggerToRadius(float trigger)
        {
            float baseRadius = StrokeSettings.FixedRadius;
            if (StrokeSettings.SizeMode != SizeMode.PressureBrush)
                return baseRadius;
            float p = Mathf.InverseLerp(pressThreshold, 1f, trigger);
            return baseRadius * Mathf.Lerp(minPressureFraction, 1f, p);
        }


        void UpdateCursor(float trigger)
        {
            float preview = pressed ? currentRadius
                : TriggerToRadius(Mathf.Max(trigger, pressThreshold));
            cursor.localScale = Vector3.one * preview * 2f;
            cursorMaterial.SetColor(BaseColorId,
                StrokeSettings.EraserMode ? EraserCursorColor : StrokeSettings.Color);
        }

        void BeginPress(Vector3 position, float trigger)
        {
            pressed = true;
            IsDrawing = true;
            currentRadius = TriggerToRadius(trigger);

            mergeTarget = null;
            if (mergeNearbyStrokes && FindNearbyItem(position, out var target, out var snapped))
            {
                mergeTarget = target;
                position = snapped; // il tratto parte attaccato all'oggetto esistente
            }

            pressPosition = position;
            pressTime = Time.time;
            smoothed = position;
            current = Stroke.Begin(position, currentRadius);
            mirrored = Mirror.Enabled ? Stroke.Begin(Mirror.Reflect(position), currentRadius) : null;
        }

        void ContinuePress(Vector3 position, float trigger)
        {
            currentRadius = Mathf.Lerp(currentRadius, TriggerToRadius(trigger), radiusSmoothing);
            smoothed = Vector3.Lerp(smoothed, position, smoothing);
            if (Vector3.Distance(smoothed, current.LastPoint) >= minSampleDistance)
            {
                current.AddPoint(smoothed, currentRadius);
                if (mirrored != null)
                    mirrored.AddPoint(Mirror.Reflect(smoothed), currentRadius);
            }
        }

        void EndPress(Vector3 position)
        {
            pressed = false;
            IsDrawing = false;

            bool isTap = Time.time - pressTime <= tapMaxDuration
                         && Vector3.Distance(position, pressPosition) <= tapMaxMovement;

            GameObject finished;
            if (isTap || current.PointCount < 2)
            {
                Destroy(current.gameObject);
                finished = Stroke.CreatePoint(pressPosition, currentRadius);
                if (mirrored != null)
                {
                    Destroy(mirrored.gameObject);
                    StrokeHistory.Push(Stroke.CreatePoint(Mirror.Reflect(pressPosition), currentRadius));
                }
            }
            else
            {
                current.Finish();
                finished = current.gameObject;

                if (mirrored != null)
                {
                    mirrored.Finish();
                    StrokeHistory.Push(mirrored.gameObject);
                }
            }

            // Magnete: il nuovo tratto entra nella gerarchia di quello esistente
            // e da qui in poi si afferrano/spostano come un unico oggetto.
            // (Solo l'originale: il gemello speculare resta indipendente.)
            if (mergeTarget != null && mergeTarget.gameObject.activeInHierarchy)
            {
                finished.transform.SetParent(mergeTarget, true);
                Destroy(finished.GetComponent<DrawnItem>()); // il "vero" oggetto è la radice
            }

            StrokeHistory.Push(finished);
            StrokeSettings.PushRecentColor(StrokeSettings.BaseColor); // colore appena usato → recenti
            current = null;
            mirrored = null;
            mergeTarget = null;
        }

        void EraseAt(Vector3 position)
        {
            if (FindNearbyItem(position, out var target, out _))
                Destroy(target.gameObject);
        }

        // Secchiello: riempie il tratto chiuso puntato col colore corrente.
        void FillAt(Vector3 position)
        {
            if (!FindNearbyItem(position, out var target, out _))
                return;
            var stroke = target.GetComponent<Stroke>();
            if (stroke == null)
                return;
            var fill = stroke.FillWith(StrokeSettings.Color, fillCloseThreshold);
            if (fill != null)
                StrokeHistory.Push(fill);
        }

        // Evidenzia il tratto chiuso sotto la punta, così sai cosa riempirai.
        void UpdateFillHover(Vector3 position)
        {
            Transform found = null;
            if (FindNearbyItem(position, out var target, out _))
            {
                var stroke = target.GetComponent<Stroke>();
                if (stroke != null && stroke.IsCloseable(fillCloseThreshold))
                    found = target;
            }
            if (fillHover == found)
                return;
            if (fillHover != null)
                StrokeHighlight.Clear(fillHover);
            fillHover = found;
            if (fillHover != null)
                StrokeHighlight.Set(fillHover, 1.3f);
        }

        bool FindNearbyItem(Vector3 position, out Transform target, out Vector3 snapped)
        {
            var hits = Physics.OverlapSphere(position, snapRadius,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            foreach (var hit in hits)
            {
                var item = hit.GetComponentInParent<DrawnItem>();
                if (item == null)
                    continue;
                target = item.transform;
                snapped = hit.ClosestPoint(position);
                return true;
            }
            target = null;
            snapped = position;
            return false;
        }
    }
}
