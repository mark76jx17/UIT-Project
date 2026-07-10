using System.Collections.Generic;
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
        [SerializeField] Vector3 tipOffset = new(-0.012f, -0.01f, 0.08f);

        [Header("Poke palette (zona di interazione)")]
        [Tooltip("Lunghezza della zona di poke a forma di segmento, dalla pallina indietro verso " +
                 "il controller. Così oltrepassando la pallina l'interazione non si perde. " +
                 "0 = solo la pallina (vecchio comportamento).")]
        [SerializeField] float pokeReach = 0.07f;

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

        [Header("Mirror")]
        [Tooltip("Se inizio/fine tratto sono entro questa distanza dal piano specchio, vengono agganciati esattamente al piano.")]
        [SerializeField] float mirrorPlaneSnapRadius = 0.025f;

        [Header("Fill (secchiello)")]
        [Tooltip("Distanza massima tra gli estremi perché un tratto sia 'chiuso' e riempibile.")]
        [SerializeField] float fillCloseThreshold = 0.05f;
        [Tooltip("Raggio di ricerca delle linee per riempire la CELLA puntata " +
                 "(reticoli / linee che si incrociano nel mezzo).")]
        [SerializeField] float regionSearchRadius = 0.6f;
        [Tooltip("ON = riempimento 'col pennello' ad anelli concentrici (più leggero ma con " +
                 "buchi tra gli anelli). OFF (consigliato) = riempimento PIENO che copia il " +
                 "tipo di pennello (es. Glow brilla): fitto, preciso, attaccato al contorno.")]
        [SerializeField] bool contourRingFill = false;

        [Header("Gomma")]
        [Tooltip("Margine aggiunto al raggio della punta per la gomma: il raggio effettivo " +
                 "segue lo slider Size (area coperta dalla punta) più questo margine per " +
                 "raggiungere la linea mediana dei tratti.")]
        [SerializeField] float eraseMargin = 0.006f;

        // La gomma cancella l'area coperta dalla punta SECONDO LA DIMENSIONE SCELTA
        // (slider Size), non un raggio fisso: punta piccola = gomma di precisione,
        // punta grande = gomma larga.
        float EraseRadius => StrokeSettings.FixedRadius + eraseMargin;

        [Tooltip("Elimina: una volta evidenziato in rosso, il bersaglio resta agganciato " +
                 "finché la punta è entro questo raggio (isteresi: niente rosso che va e " +
                 "viene ai bordi della zona di rilevamento).")]
        [SerializeField] float deleteHoverExit = 0.06f;

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
        bool mirrorStartOnPlane;
        // Snapshot dei fill catturato a inizio tratto: per rilevare, durante il disegno, se il
        // tratto passa sopra un'area riempita (i fill non hanno collider e non cambiano nel
        // tratto). Null se non serve (magnete off o già agganciato alla partenza).
        List<StrokeRecord> fillSnapshot;

        bool fillConsumed;     // un solo riempimento per pressione del trigger
        Transform fillHover;   // tratto chiuso attualmente evidenziato in modo Fill

        // Anteprima della cella (modo Fill, reticoli): mesh semitrasparente di cosa
        // riempiresti, ricalcolata a intervalli (l'arrangement è troppo caro a ogni frame).
        GameObject fillPreview;
        Material fillPreviewMat;
        Vector3 lastPreviewSeed;
        float previewTimer;

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
        bool lastMoveHint;                 // stato "icona sposta" mostrato, per aggiornare solo al cambio
        const float MoveIconScale = 1.7f;  // icona 4 frecce più grande dell'icona strumento (ben visibile)
        Transform headCached;

        // Anteprima gomma (oggetto sotto la punta) e hint del magnete (punto di aggancio).
        // Bersaglio del rosso di Elimina. La property tiene sincronizzato ActiveEraseHover,
        // letto dal GrabController: il suo hover di presa NON deve toccare questo oggetto
        // (Set/Clear condividono lo stesso MaterialPropertyBlock e spegnerebbero il rosso).
        Transform eraseHoverField;
        Transform eraseHover
        {
            get => eraseHoverField;
            set { eraseHoverField = value; ActiveEraseHover = value; }
        }

        /// <summary>L'oggetto attualmente tinto di rosso da Elimina (null se nessuno).</summary>
        public static Transform ActiveEraseHover { get; private set; }
        Transform snapHint;

        void Awake()
        {
            // Punta del pennello: piccolo trigger collider con cui premere i
            // pulsanti fisici della palette sull'altra mano.
            var tip = new GameObject("BrushTip");
            tip.transform.SetParent(transform, false);
            tip.transform.localPosition = tipOffset; // punta in avanti, fuori dal controller
            tip.AddComponent<BrushTip>();
            // Zona di poke a forma di segmento (capsula invisibile lungo l'asse Z, cioè
            // avanti dal controller): va dalla pallina indietro verso il controller. Se
            // l'utente spinge il controller oltre la pallina, parte del segmento resta
            // dentro il controllo e l'interazione non si perde (prima era solo la pallina,
            // un singolo punto). Slider e ruota leggono comunque X/Y della pallina, quindi
            // il valore scelto resta corretto: la profondità Z non lo influenza.
            var collider = tip.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.direction = 2; // asse Z locale
            collider.radius = 0.012f;
            float pokeFront = collider.radius;   // poco davanti alla pallina (come la vecchia sfera)
            float pokeBack = -pokeReach;          // indietro verso il controller
            collider.height = pokeFront - pokeBack;
            collider.center = new Vector3(0f, 0f, (pokeFront + pokeBack) * 0.5f);
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
            // L'icona fluttua libera sopra la punta: senza questo lo sfondo trasparente del glifo
            // sovrascrive l'alpha del framebuffer e, sopra la palette/un disegno, "buca" mostrando
            // il passthrough invece di ciò che sta dietro (si vedeva il quadrato dell'immagine).
            BrushMaterials.CompositeAlphaOver(cursorIconMat);
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
            if (fillPreview != null)
                Destroy(fillPreview);
            if (fillPreviewMat != null)
                Destroy(fillPreviewMat);
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
            // L'anteprima rossa vive solo in ELIMINA: fuori da lì va spenta.
            if (!StrokeSettings.DeleteMode && eraseHover != null)
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
                HideFillPreview();
                PickColorAt(position);
                return;
            }

            // Mentre il ray punta la palette (o altro consumer lo richiede) il pennello
            // non disegna: chiudi un eventuale tratto in corso ed esci.
            if (SuppressDrawing)
            {
                if (pressed)
                    EndPress(position);
                HideFillPreview();
                return;
            }

            if (StrokeSettings.FillMode)
            {
                // Secchiello: il trigger riempie la linea chiusa puntata, non disegna.
                if (pressed)
                    EndPress(position);
                UpdateFillHover(position);
                UpdateFillPreview(position, trigger);
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
            HideFillPreview();

            if (StrokeSettings.DeleteMode)
            {
                // Delete: cancella l'intero oggetto toccato, non solo una porzione.
                if (pressed)
                    EndPress(position);

                UpdateEraseHover(position);

                if (trigger >= pressThreshold)
                    DeleteAt(position);

                return;
            }

            if (StrokeSettings.EraserMode)
            {
                // Cambio modalità a metà tratto: chiudi il tratto in corso.
                if (pressed)
                    EndPress(position);
                // Niente anteprima rossa in Cancella (richiesta utente): la gomma toglie solo
                // l'area coperta, tingere la struttura era fuorviante. Il rosso resta in Elimina.
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

        // Icona sulla punta: normalmente lo strumento corrente; nel raggio di grab della palette
        // diventa le 4 frecce "sposta" (più grande). Cambia texture solo al cambio di stato,
        // si nasconde mentre disegni e si gira verso la testa.
        void UpdateCursorIcon()
        {
            if (cursorIcon == null)
                return;
            var tool = StrokeSettings.Tool;
            bool moveHint = PaletteController.MoveHintActive;
            if (moveHint != lastMoveHint || tool != lastIconTool)
            {
                lastMoveHint = moveHint;
                lastIconTool = tool;
                string icon = moveHint ? "move"
                            : tool == ToolMode.Delete ? "close"
                            : tool == ToolMode.Eraser ? "eraser"
                            : tool == ToolMode.Fill ? "droplet"
                            : "pencil";
                cursorIconMat.SetTexture(BaseMapId, ToolIcon.Get(icon));
            }
            // In modalità "sposta" l'icona è ingrandita per essere ben leggibile.
            cursorIcon.localScale = Vector3.one * (moveHint ? MoveIconScale : 1f);
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

        // Evidenzia in rosso l'INTERO oggetto che ELIMINA rimuoverebbe sotto la punta.
        // (Solo per Delete: in Cancella l'anteprima rossa è stata tolta su richiesta —
        // la gomma toglie l'area coperta, non l'oggetto.)
        // ISTERESI: l'aggancio avviene entro snapRadius (preciso), ma una volta rosso il
        // bersaglio resta agganciato finché la punta è entro deleteHoverExit (più ampio) —
        // niente evidenziazione che va e viene ai bordi; un tick aptico conferma l'aggancio.
        void UpdateEraseHover(Vector3 position)
        {
            Transform found = null;
            if (eraseHover != null && IsWithin(position, deleteHoverExit, eraseHover))
                found = eraseHover; // resta agganciato al bersaglio corrente
            if (found == null)
                found = FindNearbyItem(position, out var target, out _) ? target : null;
            if (eraseHover == found)
                return;
            if (eraseHover != null)
                StrokeHighlight.Clear(eraseHover);
            eraseHover = found;
            if (eraseHover != null)
            {
                StrokeHighlight.SetEraseHover(eraseHover);
                HapticPulse(0.02f, frequency: 0.5f, amplitude: 0.35f); // "bersaglio agganciato"
            }
        }

        // La punta è entro 'radius' da un collider di QUESTO oggetto?
        bool IsWithin(Vector3 position, float radius, Transform root)
        {
            int count = Physics.OverlapSphereNonAlloc(position, radius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var item = overlapBuffer[i].GetComponentInParent<DrawnItem>();
                if (item != null && item.transform == root)
                    return true;
            }
            return false;
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

        // Modalità Line, latchata per tratto a inizio pressione (un toggle di Line a metà
        // tratto — es. col ray dell'altra mano — non deve cambiare il tratto in corso).
        bool lineStroke;
        // Il tratto è "sulla carta": iniziato col foglio a quadretti in range, resta
        // proiettato sul foglio fino al rilascio (latch, come il ray della palette).
        bool lineOnSheet;

        void BeginPress(Vector3 position, float trigger)
        {
            pressed = true;
            IsDrawing = true;
            currentRadius = TriggerToRadius(trigger);

            mergeTarget = null;
            mirrorStartOnPlane = false;

            // Se parto vicino al piano specchio, parto ESATTAMENTE sul piano.
            // Questo evita il micro-gap tra stroke originale e stroke speculare.
            if (Mirror.Enabled && Mirror.TryProject(position, mirrorPlaneSnapRadius, out var mirrorStart))
            {
                position = mirrorStart;
                mirrorStartOnPlane = true;
            }

            if (mergeNearbyStrokes && FindNearbyItem(position, out var target, out var snapped))
            {
                mergeTarget = target;
                position = snapped; // il tratto parte attaccato all'oggetto esistente

                // Se lo snap al tratto vicino è comunque vicino al piano, riallinealo al piano.
                if (Mirror.Enabled && Mirror.TryProject(position, mirrorPlaneSnapRadius, out var projectedSnapped))
                {
                    position = projectedSnapped;
                    mirrorStartOnPlane = true;
                }

                // Snap-merge: impulso distintivo (più lungo e forte) per conferma tattile
                HapticPulse(hapticSnapDuration, frequency: 0.8f, amplitude: 0.8f);
            }
            else
            {
                // Inizio normale del tratto: impulso breve
                HapticPulse(hapticStrokeDuration, frequency: 0.4f, amplitude: 0.5f);
            }

            // Snapshot dei fill per rilevare, durante il disegno, se il tratto passa sopra
            // un'area colorata; serve solo se non ci si è già agganciati alla partenza.
            fillSnapshot = (mergeNearbyStrokes && mergeTarget == null) ? FillRegion.ActiveFills() : null;

            // Line + Grid: se la punta è in range del foglio, il tratto nasce appoggiato
            // alla carta (e ci resta: vedi lineOnSheet). La proiezione vince sul magnete.
            lineStroke = StrokeSettings.SnapAxis;
            lineOnSheet = false;
            if (lineStroke && ReferenceGrid.TryProject(position, out var onSheet))
            {
                lineOnSheet = true;
                position = onSheet;
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

            // Fusione all'intersezione: se non sono ancora agganciato, la prima figura che il
            // tratto attraversa (contorno o area riempita) diventa il bersaglio della fusione.
            if (mergeTarget == null && mergeNearbyStrokes)
                DetectMergeWhileDrawing(position);

            // Line: linea ELASTICA — il tratto è il segmento start→punta che segue il
            // controller in qualsiasi direzione (oblique incluse; il vecchio ConstrainToAxis
            // vincolava all'asse del mondo dominante, ricalcolato punto per punto → niente
            // oblique e "salti" di asse a inizio tratto). Con la Grid in range a inizio
            // tratto, gli estremi restano appoggiati al foglio (lineOnSheet).
            if (lineStroke)
            {
                prevPosition = position;
                Vector3 end = lineOnSheet ? ReferenceGrid.ProjectClamped(smoothed) : smoothed;
                current.SetLineEnd(end, currentRadius);
                if (mirrored != null)
                    mirrored.SetLineEnd(Mirror.Reflect(end), currentRadius);
                return; // niente campionamento adattivo: SetLineEnd ricostruisce il segmento
            }

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

            bool mirrorEndOnPlane = false;
            Vector3 finalPosition = position;

            // Se finisco vicino al piano specchio, l'ultimo punto viene agganciato al piano.
            if (Mirror.Enabled && Mirror.TryProject(finalPosition, mirrorPlaneSnapRadius, out var mirrorEnd))
            {
                finalPosition = mirrorEnd;
                mirrorEndOnPlane = true;
            }

            bool isTap = Time.time - pressTime <= tapMaxDuration
                        && Vector3.Distance(finalPosition, pressPosition) <= tapMaxMovement;

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
                // Se il tratto finisce sul piano mirror, aggiungi un ultimo punto esatto sul piano.
                // Così originale e speculare condividono davvero l'estremo finale.
                // Lo facciamo solo per stroke normali, non per lineStroke: le linee elastiche
                // hanno già i loro due estremi gestiti separatamente.
                if (!lineStroke && mirrorEndOnPlane && Vector3.Distance(current.LastPoint, finalPosition) > 0.001f)
                {
                    current.AddPoint(finalPosition, currentRadius);

                    if (mirrored != null)
                        mirrored.AddPoint(Mirror.Reflect(finalPosition), currentRadius);
                }

                // Le linee elastiche chiudono con FinishLine: aggiunge i collider di presa
                // lungo il segmento (i punti non passano da AddPoint che li semina).
                if (lineStroke) current.FinishLine(); else current.Finish();
                finished = current.gameObject;

                if (mirrored != null)
                {
                    if (lineStroke) mirrored.FinishLine(); else mirrored.Finish();
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
            bool mirrorSelfMerged = false;

            // Caso speciale: ho disegnato da un punto del piano specchio a un altro punto del piano.
            // Originale e speculare devono diventare un unico oggetto logico.
            if (mirroredObj != null && mirrorStartOnPlane && mirrorEndOnPlane)
            {
                mirroredObj.transform.SetParent(finished.transform, true);

                var item = mirroredObj.GetComponent<DrawnItem>();
                if (item != null)
                    Destroy(item);

                mirrorSelfMerged = true;
            }

            // Tratto + eventuale gemello speculare come UN solo passo di undo.
            // Se li abbiamo fusi in gerarchia, basta pushare la radice `finished`.
            if (mirroredObj != null && !mirrorSelfMerged)
                StrokeHistory.PushGroup(finished, mirroredObj);
            else
                StrokeHistory.Push(finished);

            StrokeSettings.PushRecentColor(StrokeSettings.BaseColor); // colore appena usato → recenti
            current = null;
            mirrored = null;
            mergeTarget = null;
            mirrorStartOnPlane = false;
        }

        void DeleteAt(Vector3 position)
        {
            // Coerenza col rosso: si elimina ESATTAMENTE ciò che è evidenziato (l'isteresi
            // dell'hover può tenerlo agganciato poco oltre il raggio d'ingresso).
            Transform target = eraseHover;
            if (target == null && !FindNearbyItem(position, out target, out _))
                return;

            StrokeHighlight.Clear(target);

            if (eraseHover == target)
                eraseHover = null;

            target.gameObject.SetActive(false);
            StrokeHistory.PushErase(target.gameObject);

            HapticPulse(hapticStrokeDuration, frequency: 0.5f, amplitude: 0.6f);
        }

        void EraseAt(Vector3 position)
        {
            if (!FindNearbyPart(position, EraseRadius, out var target, out var part))
                return;
            // Togli prima l'evidenziazione rossa, o riapparirebbe rossa dopo un undo.
            StrokeHighlight.Clear(target);
            if (eraseHover != null)
            {
                StrokeHighlight.Clear(eraseHover);
                eraseHover = null;
            }

            // Cancellazione PARZIALE: si toglie solo l'area coperta dalla punta (raggio dallo
            // slider Size). Sui gruppi fusi si erode la SOLA parte colpita, non l'intera entità.
            if (part == target && !Stroke.HasRealChildren(target))
            {
                // Oggetto semplice (nessun sotto-oggetto fuso): comportamento storico —
                // i pezzi rimasti diventano oggetti indipendenti (la gomma "taglia in due").
                var s = target.GetComponent<Stroke>();
                if (s != null && s.TryEraseSphere(position, EraseRadius, out var pieces, ensureProgress: true))
                {
                    target.gameObject.SetActive(false);
                    StrokeHistory.PushReplace(new[] { target.gameObject }, pieces.ToArray());
                }
                else
                {
                    // Non erodibile (punto, fill, anelli): si toglie l'oggetto intero.
                    target.gameObject.SetActive(false);
                    StrokeHistory.PushErase(target.gameObject);
                }
            }
            else if (part == target)
            {
                // Colpita la geometria della RADICE di un gruppo: si ricostruisce il nodo
                // (pezzi superstiti + cloni dei figli fusi) e si sostituisce atomicamente.
                var rebuilt = Stroke.EraseNodeAndRebuild(target, position, EraseRadius, out bool erodible);
                if (!erodible)
                {
                    // Radice non erodibile (punto/fillata): non c'è parziale sensata — via tutto.
                    target.gameObject.SetActive(false);
                    StrokeHistory.PushErase(target.gameObject);
                }
                else
                {
                    target.gameObject.SetActive(false);
                    if (rebuilt != null)
                        StrokeHistory.PushReplace(new[] { target.gameObject }, new[] { rebuilt });
                    else
                        StrokeHistory.PushErase(target.gameObject); // eroso tutto, nessun superstite
                }
            }
            else
            {
                // Colpito un SOTTO-OGGETTO fuso: si nasconde solo quello. I pezzi superstiti
                // che TOCCANO ancora il resto della struttura rientrano nel blocco; quelli
                // rimasti fisicamente STACCATI diventano oggetti indipendenti — così Elimina
                // (e il grab) li trattano da soli invece di prendere l'intera struttura.
                var stroke = part.GetComponent<Stroke>();
                var pieces = new System.Collections.Generic.List<GameObject>();
                bool erodible;
                if (stroke != null && !Stroke.HasRealChildren(part))
                {
                    // Caso tipico (parte semplice): pezzi granulari, ognuno classificato da sé.
                    erodible = stroke.TryEraseSphere(position, EraseRadius, out pieces, ensureProgress: true);
                }
                else
                {
                    // Parte con sotto-oggetti annidati: ricostruzione unica (pezzi + cloni).
                    var rebuilt = Stroke.EraseNodeAndRebuild(part, position, EraseRadius, out erodible);
                    if (rebuilt != null)
                        pieces.Add(rebuilt);
                }

                part.gameObject.SetActive(false);
                if (!erodible || pieces.Count == 0)
                {
                    // Parte non erodibile (fill/anelli/punto) o erosa del tutto: via solo lei.
                    StrokeHistory.PushErase(part.gameObject);
                }
                else
                {
                    foreach (var piece in pieces)
                        AttachIfTouching(piece, target);
                    StrokeHistory.PushReplace(new[] { part.gameObject }, pieces.ToArray());
                }
            }
            HapticPulse(hapticStrokeDuration, frequency: 0.5f, amplitude: 0.6f);
        }

        // Il pezzo superstite resta nel blocco solo se tocca ancora il resto della
        // struttura; altrimenti resta l'oggetto indipendente creato da Rebuild (col suo
        // DrawnItem), eliminabile/afferrabile da solo.
        void AttachIfTouching(GameObject piece, Transform group)
        {
            if (!TouchesStructure(piece, group))
                return;
            piece.transform.SetParent(group, true); // mantiene la posa nel mondo
            var item = piece.GetComponent<DrawnItem>();
            if (item != null)
                Destroy(item);
        }

        // Contatto pezzo↔struttura: un punto della polilinea del pezzo entro ContactRadius
        // dai collider (di presa) della struttura. Si interrogano i collider ESISTENTI del
        // gruppo dai PUNTI del pezzo, non il contrario: i collider appena creati dal Rebuild
        // entrano nel broadphase fisico solo al prossimo update e una query li mancherebbe.
        // (Per lo stesso motivo un pezzo che tocca il gruppo solo attraverso un ALTRO pezzo
        // appena creato non viene concatenato: caso raro, resta indipendente.)
        bool TouchesStructure(GameObject piece, Transform group)
        {
            const float ContactRadius = 0.005f;
            foreach (var rec in piece.GetComponentsInChildren<StrokeRecord>())
            {
                var t = rec.transform;
                for (int i = 0; i < rec.points.Count; i += 2)
                {
                    Vector3 w = t.TransformPoint(rec.points[i]);
                    int hits = Physics.OverlapSphereNonAlloc(w, ContactRadius, overlapBuffer,
                        Physics.AllLayers, QueryTriggerInteraction.Collide);
                    for (int h = 0; h < hits; h++)
                    {
                        var item = overlapBuffer[h].GetComponentInParent<DrawnItem>();
                        if (item != null && item.transform == group)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Oggetto disegnato vicino alla punta E la sua "parte" colpita: il sotto-oggetto
        /// (tratto/fill/anelli fusi nel gruppo) a cui appartiene il collider toccato.
        /// part == target per oggetti semplici o quando si tocca la geometria della radice
        /// (i suoi Cap/GrabCollider). La gomma erode la parte, Delete rimuove il target.
        /// </summary>
        bool FindNearbyPart(Vector3 position, float radius, out Transform target, out Transform part)
        {
            target = part = null;
            int count = Physics.OverlapSphereNonAlloc(position, radius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hit = overlapBuffer[i];
                var item = hit.GetComponentInParent<DrawnItem>();
                if (item == null)
                    continue;
                target = item.transform;
                // La parte = il portatore di StrokeRecord più vicino al collider (il singolo
                // tratto/fill, anche annidato). Gli anelli del fill "col pennello" non hanno
                // record: si risale al figlio diretto della radice che li contiene.
                var rec = hit.GetComponentInParent<StrokeRecord>();
                if (rec != null)
                    part = rec.transform;
                else
                {
                    var p = hit.transform;
                    while (p != target && p.parent != target)
                        p = p.parent;
                    part = p;
                }
                return true;
            }
            return false;
        }

        // Secchiello: riempie l'area chiusa puntata col colore corrente.
        void FillAt(Vector3 position)
        {
            GameObject fill = null;

            if (contourRingFill)
            {
                // "Col pennello": anelli concentrici che seguono il contorno, col pennello
                // (tipo + spessore) copiato dall'oggetto. Si aggancia all'oggetto se ne
                // delimita uno solo (si muove con lui).
                var rings = FillRegion.FindRings(position, regionSearchRadius,
                    out var ringRoot, out var ringBrush, out var ringRadius);
                if (rings != null)
                {
                    fill = Stroke.MakeRingFill(rings, StrokeSettings.Color, ringBrush, ringRadius);
                    if (ringRoot != null)
                        fill.transform.SetParent(ringRoot, true);
                }
            }
            else
            {
                if (FindNearbyItem(position, out var target, out _))
                {
                    var stroke = target.GetComponent<Stroke>();
                    if (stroke != null)
                        fill = stroke.FillWith(StrokeSettings.Color, fillCloseThreshold);
                    if (fill == null) // tratto non chiuso da solo: prova ad unire i tratti del gruppo
                        fill = Stroke.FillGroup(target, StrokeSettings.Color, fillCloseThreshold);
                }
                if (fill == null) // nessun anello: riempi la REGIONE puntata (cella/ciambella)
                    fill = FillRegion.FillCellAt(position, StrokeSettings.Color, regionSearchRadius, fillCloseThreshold);
            }

            if (fill == null)
                return;

            // Ricolora-in-posto: se qui c'era già un riempimento, sostituiscilo invece di
            // sovrapporlo (niente z-fight; un solo passo di undo).
            var covered = FillRegion.CoveringFills(position, fill);
            if (covered.Count > 0)
            {
                foreach (var go in covered)
                    go.SetActive(false);
                StrokeHistory.PushReplace(covered.ToArray(), new[] { fill });
            }
            else
            {
                StrokeHistory.Push(fill);
            }
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
                else if (Stroke.IsGroupFillable(target, fillCloseThreshold)) // anello da più tratti uniti
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

        // Anteprima della cella del reticolo che riempiresti: mesh semitrasparente col colore
        // corrente. L'arrangement è caro, quindi si ricalcola solo se la punta si è spostata
        // o ogni ~0.15 s. Niente anteprima se stai già puntando un tratto/gruppo o premendo.
        void UpdateFillPreview(Vector3 position, float trigger)
        {
            if (fillHover != null || trigger >= pressThreshold)
            {
                HideFillPreview();
                return;
            }

            previewTimer -= Time.deltaTime;
            bool moved = (position - lastPreviewSeed).sqrMagnitude > 0.02f * 0.02f;
            if (!moved && previewTimer > 0f)
                return;
            previewTimer = 0.15f;
            lastPreviewSeed = position;

            var region = FillRegion.FindRegion(position, regionSearchRadius, fillCloseThreshold);
            HideFillPreview(); // via la vecchia mesh (e il materiale istanziato che porta con sé)
            if (region == null)
                return;

            if (fillPreviewMat == null)
                fillPreviewMat = BrushMaterials.CreateUnlit(PreviewColor());
            else
                fillPreviewMat.SetColor(BaseColorId, PreviewColor());
            fillPreview = FillSurface.BuildWithHoles(region.outer, region.holes, fillPreviewMat);
        }

        void HideFillPreview()
        {
            if (fillPreview != null)
                Destroy(fillPreview);
            fillPreview = null;
        }

        // Colore corrente, semitrasparente: l'anteprima mostra cosa riempiresti.
        Color PreviewColor()
        {
            var c = StrokeSettings.BaseColor;
            c.a = 0.35f;
            return c;
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

        // Durante il disegno: se il tratto non è ancora agganciato a una figura, la prima che
        // interseca diventa il bersaglio della fusione (a fine tratto entra nella sua gerarchia,
        // vedi EndPress). Prova prima il contorno (collider di un altro tratto entro snapRadius),
        // poi l'area riempita (snapshot dei fill). Il tratto in corso e il gemello speculare sono
        // esclusi: man mano che li disegno hanno già i loro collider.
        void DetectMergeWhileDrawing(Vector3 position)
        {
            Transform found = null;

            int count = Physics.OverlapSphereNonAlloc(position, snapRadius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count && found == null; i++)
            {
                var item = overlapBuffer[i].GetComponentInParent<DrawnItem>();
                if (item != null && !IsCurrentStroke(item.transform))
                    found = item.transform;
            }

            if (found == null && fillSnapshot != null)
                foreach (var fill in fillSnapshot)
                {
                    if (fill == null || !fill.gameObject.activeInHierarchy || !FillRegion.Covers(fill, position))
                        continue;
                    var item = fill.GetComponentInParent<DrawnItem>();
                    if (item != null && !IsCurrentStroke(item.transform))
                    {
                        found = item.transform;
                        break;
                    }
                }

            if (found == null)
                return;
            mergeTarget = found;
            HapticPulse(hapticSnapDuration, frequency: 0.8f, amplitude: 0.8f); // conferma tattile dell'aggancio
        }

        // Il tratto che sto disegnando (o il suo gemello speculare)?
        bool IsCurrentStroke(Transform root) =>
            (current != null && root == current.transform)
            || (mirrored != null && root == mirrored.transform);
    }
}
