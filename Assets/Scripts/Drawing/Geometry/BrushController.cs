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
    ///
    /// [NUOVO] Haptic feedback: impulsi tattili su inizio/fine tratto e snap-merge.
    /// [NUOVO] Campionamento adattivo: la distanza minima tra campioni scala con la
    ///         velocità del controller, producendo più dettaglio nei movimenti veloci.
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
        [SerializeField] float minPressureFraction = 0.10f;
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

        [Header("Gomma")]
        [Tooltip("Raggio entro cui la gomma toglie i punti del tratto (cancellazione parziale).")]
        [SerializeField] float eraseRadius = 0.03f;

        [Header("Campionamento adattivo")]
        [Tooltip("Velocità controller (m/s) oltre cui la distanza minima scende al valore minimo.")]
        [SerializeField] float adaptiveSpeedMax = 0.5f;
        [Tooltip("Distanza minima a bassa velocità (pennello fermo = meno punti).")]
        [SerializeField] float adaptiveMinSlow = 0.010f;
        [Tooltip("Distanza minima ad alta velocità (movimento rapido = più dettaglio).")]
        [SerializeField] float adaptiveMinFast = 0.002f;

        [Header("Haptic feedback")]
        [Tooltip("Durata dell'impulso su inizio/fine tratto (secondi).")]
        [SerializeField] float hapticStrokeDuration = 0.04f;
        [Tooltip("Durata dell'impulso speciale snap-merge (secondi).")]
        [SerializeField] float hapticSnapDuration = 0.08f;

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

        // Haptic state
        float hapticTimer;
        float hapticFreq;
        float hapticAmp;

        // Per campionamento adattivo: posizione precedente del controller
        Vector3 prevPosition;

        Transform cursor;
        Material cursorMaterial;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        // HUD sulla punta: icona dello strumento corrente (penna/secchiello/gomma),
        // così sai in che modalità sei senza guardare la palette.
        Transform cursorIcon;
        Material cursorIconMat;
        ToolMode lastIconTool = (ToolMode)(-1);
        Transform headCached;

        // Anteprima gomma (oggetto sotto la punta) e hint del magnete (punto di aggancio).
        Transform eraseHover;
        Transform snapHint;

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

            // Icona dello strumento, fluttuante sopra la punta e rivolta alla testa.
            // Non figlia del cursore (che scala con lo spessore): non deve ingrandirsi.
            var iconGO = new GameObject("CursorTool");
            iconGO.transform.SetParent(transform, false);
            iconGO.transform.localPosition = tipOffset + new Vector3(0f, 0.035f, 0f);
            iconGO.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(0.02f, 0.02f);
            cursorIconMat = BrushMaterials.CreateUnlit(Color.white);
            iconGO.AddComponent<MeshRenderer>().material = cursorIconMat;
            cursorIcon = iconGO.transform;

            // Hint del magnete: piccola sfera nel punto dove il prossimo tratto si
            // aggancerebbe a un oggetto esistente. In world space, posizionata a runtime.
            var snapGO = new GameObject("SnapHint");
            snapGO.AddComponent<MeshFilter>().sharedMesh = BrushMeshes.Sphere();
            snapGO.AddComponent<MeshRenderer>().material =
                BrushMaterials.CreateUnlit(new Color(0.55f, 0.45f, 0.95f, 0.85f));
            snapGO.transform.localScale = Vector3.one * 0.02f;
            snapHint = snapGO.transform;
            snapHint.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (snapHint != null)
                Destroy(snapHint.gameObject); // non è figlio del rig: va liberato a mano
        }

        void Update()
        {
            float trigger = TriggerOverride
                            ?? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, StrokeSettings.BrushHand);
            Vector3 position = Tip != null ? Tip.position : transform.position;

            // Haptic timer: mantieni la vibrazione per la durata programmata, poi spegni.
            if (hapticTimer > 0f)
            {
                OVRInput.SetControllerVibration(hapticFreq, hapticAmp, StrokeSettings.BrushHand);
                hapticTimer -= Time.deltaTime;
                if (hapticTimer <= 0f)
                    OVRInput.SetControllerVibration(0f, 0f, StrokeSettings.BrushHand);
            }

            UpdateCursor(trigger);
            UpdateCursorIcon();

            // Aiuti visivi che vanno spenti ogni frame e riaccesi solo dove servono.
            if (snapHint != null)
                snapHint.gameObject.SetActive(false);
            if (!StrokeSettings.EraserMode && eraseHover != null)
            {
                StrokeHighlight.Clear(eraseHover);
                eraseHover = null;
            }

            // Contagocce: tenendo A/X (mano del pennello) con la punta vicino a un tratto
            // se ne preleva il colore. Non disegna mentre prelevi.
            if (OVRInput.Get(OVRInput.Button.One, StrokeSettings.BrushHand))
            {
                if (pressed)
                    EndPress(position);
                PickColorAt(position);
                return;
            }

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
                UpdateEraseHover(position); // evidenzia in rosso cosa cancellerai
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
            else
                UpdateSnapHint(position); // pen mode, fermo: mostra dove si aggancerebbe
        }

        /// <summary>
        /// Impulso aptico sul controller del pennello.
        /// frequency e amplitude sono nel range [0, 1].
        /// </summary>
        void HapticPulse(float duration, float frequency = 0.5f, float amplitude = 0.6f)
        {
            hapticFreq = frequency;
            hapticAmp = amplitude;
            hapticTimer = duration;
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
            // Mentre disegni: spessore reale corrente. Fermo: mostra lo spessore BASE
            // (massimo in modalità pressione), non il minimo — prima il cursore in
            // pressione mostrava sempre il 25% ed era fuorviante.
            float preview = pressed ? currentRadius : StrokeSettings.FixedRadius;
            cursor.localScale = Vector3.one * preview * 2f;
            cursorMaterial.SetColor(BaseColorId,
                StrokeSettings.EraserMode ? EraserCursorColor : StrokeSettings.Color);
        }

        // Icona dello strumento sulla punta: cambia texture solo al cambio di strumento,
        // si nasconde mentre disegni e si gira verso la testa.
        void UpdateCursorIcon()
        {
            if (cursorIcon == null)
                return;
            var tool = StrokeSettings.Tool;
            if (tool != lastIconTool)
            {
                lastIconTool = tool;
                string icon = tool == ToolMode.Eraser ? "eraser"
                            : tool == ToolMode.Fill ? "droplet" : "pencil";
                cursorIconMat.SetTexture(BaseMapId, ToolIcon.Get(icon));
            }
            bool show = !IsDrawing;
            if (cursorIcon.gameObject.activeSelf != show)
                cursorIcon.gameObject.SetActive(show);
            if (!show)
                return;
            if (headCached == null && Camera.main != null)
                headCached = Camera.main.transform;
            if (headCached != null)
            {
                var dir = cursorIcon.position - headCached.position;
                if (dir.sqrMagnitude > 1e-6f)
                    cursorIcon.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        // Evidenzia in rosso l'oggetto che la gomma cancellerebbe sotto la punta.
        void UpdateEraseHover(Vector3 position)
        {
            Transform found = FindNearbyItem(position, out var target, out _) ? target : null;
            if (eraseHover == found)
                return;
            if (eraseHover != null)
                StrokeHighlight.Clear(eraseHover);
            eraseHover = found;
            if (eraseHover != null)
                StrokeHighlight.SetEraseHover(eraseHover);
        }

        // Contagocce: preleva il colore del tratto sotto la punta e lo imposta come corrente.
        void PickColorAt(Vector3 position)
        {
            if (FindNearbyItem(position, out var target, out _))
            {
                var record = target.GetComponentInChildren<StrokeRecord>();
                if (record != null)
                    StrokeSettings.SetColor(record.color);
            }
        }

        // Mostra dove il prossimo tratto si aggancerebbe a un oggetto esistente (magnete).
        void UpdateSnapHint(Vector3 position)
        {
            if (snapHint == null || !mergeNearbyStrokes)
                return;
            if (FindNearbyItem(position, out _, out var snapped))
            {
                snapHint.position = snapped;
                snapHint.gameObject.SetActive(true);
            }
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
                // Snap-merge: impulso distintivo (più lungo e forte) per conferma tattile
                HapticPulse(hapticSnapDuration, frequency: 0.8f, amplitude: 0.8f);
            }
            else
            {
                // Inizio normale del tratto: impulso breve
                HapticPulse(hapticStrokeDuration, frequency: 0.4f, amplitude: 0.5f);
            }

            pressPosition = position;
            pressTime = Time.time;
            smoothed = position;
            prevPosition = position;
            Vector3 up = transform.up; // "alto" del controller: orienta il Nastro
            current = Stroke.Begin(position, currentRadius, up);
            mirrored = Mirror.Enabled ? Stroke.Begin(Mirror.Reflect(position), currentRadius, up) : null;
        }

        void ContinuePress(Vector3 position, float trigger)
        {
            // Anche l'inseguimento del raggio è reso indipendente dal frame-rate
            // (come la posizione), così la pressione modula lo spessore allo stesso
            // modo a 72/90/120 Hz e in editor.
            float radiusAlpha = 1f - Mathf.Pow(1f - radiusSmoothing, Time.deltaTime * 72f);
            currentRadius = Mathf.Lerp(currentRadius, TriggerToRadius(trigger), radiusAlpha);
            // Smoothing indipendente dal frame-rate: 'smoothing' è il peso per-frame a
            // 72 Hz (refresh di riferimento del Quest). Senza correzione, a 90/120 Hz il
            // tratto verrebbe più liscio che a 72 Hz (più Lerp al secondo) e diverso in
            // editor; l'esponente su Time.deltaTime normalizza il risultato.
            float smoothAlpha = 1f - Mathf.Pow(1f - smoothing, Time.deltaTime * 72f);
            smoothed = Vector3.Lerp(smoothed, position, smoothAlpha);

            // Snap ad assi: vincola il tratto all'asse del mondo dominante (linee dritte).
            if (StrokeSettings.SnapAxis)
                smoothed = ConstrainToAxis(smoothed);

            // Campionamento adattivo alla velocità: movimenti veloci → distanza minima ridotta
            // (più campioni = maggiore fedeltà del tratto); movimenti lenti → distanza maggiore
            // (meno punti inutili quando la mano è quasi ferma).
            float speed = Vector3.Distance(position, prevPosition) / Mathf.Max(Time.deltaTime, 1e-4f);
            float adaptiveMin = Mathf.Lerp(adaptiveMinSlow, adaptiveMinFast,
                                            Mathf.Clamp01(speed / adaptiveSpeedMax));
            prevPosition = position;

            if (Vector3.Distance(smoothed, current.LastPoint) >= adaptiveMin)
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
            // Fine tratto: impulso breve di conferma
            HapticPulse(hapticStrokeDuration, frequency: 0.3f, amplitude: 0.4f);

            bool isTap = Time.time - pressTime <= tapMaxDuration
                         && Vector3.Distance(position, pressPosition) <= tapMaxMovement;

            GameObject finished;
            GameObject mirroredObj = null;
            if (isTap || current.PointCount < 2)
            {
                Destroy(current.gameObject);
                finished = Stroke.CreatePoint(pressPosition, currentRadius);
                if (mirrored != null)
                {
                    Destroy(mirrored.gameObject);
                    mirroredObj = Stroke.CreatePoint(Mirror.Reflect(pressPosition), currentRadius);
                }
            }
            else
            {
                current.Finish();
                finished = current.gameObject;

                if (mirrored != null)
                {
                    mirrored.Finish();
                    mirroredObj = mirrored.gameObject;
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

            // Tratto + eventuale gemello speculare come UN solo passo di undo.
            if (mirroredObj != null)
                StrokeHistory.PushGroup(finished, mirroredObj);
            else
                StrokeHistory.Push(finished);
            StrokeSettings.PushRecentColor(StrokeSettings.BaseColor); // colore appena usato → recenti
            current = null;
            mirrored = null;
            mergeTarget = null;
        }

        void EraseAt(Vector3 position)
        {
            if (!FindNearbyItem(position, out var target, out _))
                return;
            // Togli prima l'evidenziazione rossa, o riapparirebbe rossa dopo un undo.
            StrokeHighlight.Clear(target);
            if (eraseHover == target)
                eraseHover = null;

            // Cancellazione PARZIALE su un tratto semplice (non unito col magnete): toglie
            // solo la porzione toccata e ricostruisce i pezzi rimasti. Altrimenti
            // cancella tutto l'oggetto. In entrambi i casi è annullabile.
            var strokes = target.GetComponentsInChildren<Stroke>();
            if (strokes.Length == 1 && strokes[0].TryEraseSphere(position, eraseRadius, out var pieces))
            {
                target.gameObject.SetActive(false);
                StrokeHistory.PushReplace(new[] { target.gameObject }, pieces.ToArray());
            }
            else
            {
                target.gameObject.SetActive(false);
                StrokeHistory.PushErase(target.gameObject);
            }
            HapticPulse(hapticStrokeDuration, frequency: 0.5f, amplitude: 0.6f);
        }

        // Asse del mondo dominante dal punto di partenza: per disegnare linee dritte.
        Vector3 ConstrainToAxis(Vector3 p)
        {
            Vector3 d = p - pressPosition;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) return pressPosition + new Vector3(d.x, 0f, 0f);
            if (ay >= az) return pressPosition + new Vector3(0f, d.y, 0f);
            return pressPosition + new Vector3(0f, 0f, d.z);
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
            if (fill == null) // riprova: anello formato da più tratti uniti col magnete
                fill = Stroke.FillGroup(target, StrokeSettings.Color, fillCloseThreshold);
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

        // Buffer riusato per le query di prossimità: la variante NonAlloc non genera
        // garbage ogni frame (la OverlapSphere normale alloca un array a ogni chiamata).
        static readonly Collider[] overlapBuffer = new Collider[32];

        bool FindNearbyItem(Vector3 position, out Transform target, out Vector3 snapped)
        {
            int count = Physics.OverlapSphereNonAlloc(position, snapRadius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hit = overlapBuffer[i];
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
