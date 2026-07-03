using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Interazione a distanza con la palette: dalla mano del pennello parte un raggio
    /// che, quando colpisce un controllo della palette (pulsanti, slider, ruota — tutti
    /// con collider trigger), mostra una linea e lo aziona col trigger. Mentre il raggio
    /// punta la palette il pennello non disegna (vedi BrushController.SuppressDrawing),
    /// così il trigger "clicca" invece di tracciare. Da vicino resta valido anche il
    /// poke con la punta: i due modi convivono (PaletteButton ha un debounce).
    ///
    /// [NUOVO] Snap-to-button assistito: se nessun controllo è colpito direttamente
    /// ma un PaletteButton si trova entro snapAngleDeg gradi dalla direzione del ray,
    /// il raggio scatta verso quel pulsante. Riduce gli errori di selezione su target
    /// piccoli o a distanza (ispirato a Gabel et al., SUI 2024 — raycast redirection).
    /// </summary>
    public class PaletteRay : MonoBehaviour
    {
        public enum HandRole { Brush, Palette }

        // Brush: mano del pennello (origine dalla punta, sopprime il disegno mentre punta). Palette:
        // mano che tiene la palette, usata SOLO quando la palette è fissata nella stanza (Placed),
        // per poterla comandare col secondo controller. Origine dal controller, niente disegno.
        public HandRole Role { get; set; } = HandRole.Brush;

        // Solo per il ray della mano-palette: attivo solo quando la palette è fissata nella stanza.
        public bool RequiresPlaced { get; set; }

        // True quando il ray della mano-palette sta puntando la palette: PaletteController lo
        // consulta per NON ri-agganciare se il trigger serviva a cliccare un controllo.
        public static bool PaletteHandOnPalette;

        public BrushController Brush { get; set; }

        // Controller risolto dinamicamente, così segue il cambio di mano dominante.
        OVRInput.Controller Controller =>
            Role == HandRole.Brush ? StrokeSettings.BrushHand : StrokeSettings.PaletteHand;

        [SerializeField] float maxDistance = 2f;
        [SerializeField] float pressThreshold = 0.55f;

        [Tooltip("Angolo massimo (gradi) entro cui il raggio scatta verso un PaletteButton vicino.")]
        [SerializeField] float snapAngleDeg = 5f;

        LineRenderer line;
        bool wasPressed;
        bool startedOnPalette; // modalità "latchata" al fronte di salita del trigger
        PaletteButton hoveredButton;

        // Il raycast considera SOLO il layer della palette: ignora del tutto i tratti
        // disegnati. Risolve anche il rischio che, in un disegno denso, il buffer di hit
        // si riempisse di collider dei tratti prima di raggiungere il controllo.
        static int PaletteMask => 1 << PaletteController.PaletteLayer;

        void Awake()
        {
            var go = new GameObject("PaletteRayLine");
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.003f;
            line.numCapVertices = 4;
            line.material = BrushMaterials.CreateUnlit(new Color(0.55f, 0.45f, 0.95f, 0.9f));
            line.enabled = false;
        }

        void Update()
        {
            // La mano-palette comanda la palette SOLO quando è fissata nella stanza (Placed):
            // appesa alla mano non avrebbe senso e darebbe pressioni accidentali.
            if (RequiresPlaced && !PaletteController.Placed)
            {
                line.enabled = false;
                SetHoveredButton(null);
                if (Role == HandRole.Palette)
                    PaletteHandOnPalette = false;
                return;
            }

            // Palette o foglio a quadretti trascinati col grip: i controlli della palette
            // passano davanti al ray, che cliccherebbe da solo. Ray spento e niente press
            // finché non si rilascia; il trigger della mano-pennello non disegna (il grip
            // sta spostando il pannello).
            if (PaletteController.IsGrabbing || ReferenceGrid.IsGrabbing)
            {
                line.enabled = false;
                SetHoveredButton(null);
                if (Role == HandRole.Palette)
                    PaletteHandOnPalette = false;
                else if (Brush != null)
                    Brush.SuppressDrawing = true;
                wasPressed = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, Controller) >= pressThreshold;
                return;
            }

            // Origine/direzione del ray: dalla punta del pennello se presente (collineare con
            // tip+controller, così "esce" da dove si punta); altrimenti dal controller in avanti
            // (mano-palette, che non ha punta del pennello).
            bool hasTip = Brush != null && Brush.Tip != null;
            Vector3 origin = hasTip ? Brush.Tip.position : transform.position;
            Vector3 dir = hasTip
                ? (Brush.Tip.position - transform.position).normalized
                : transform.forward;

            // Prima cerca un CONTROLLO (pulsante/slider/picker) lungo il ray; se non c'è ma
            // un menu modale è aperto, accetta un hit sul suo SFONDO così il ray resta
            // visibile su tutta l'area del pannello (non solo sopra la ✕), senza renderlo
            // premibile. Risolve "il ray sparisce sulle Shortcuts".
            bool onControl = TryHitPalette(origin, dir, out var hit);
            bool onModalSurface = !onControl && TryHitModalSurface(origin, dir, out hit);
            bool onPalette = onControl || onModalSurface;

            if (Role == HandRole.Palette)
                PaletteHandOnPalette = onPalette;

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, Controller);
            bool pressed = trigger >= pressThreshold;
            bool justPressed = pressed && !wasPressed;

            // Latch della modalità al fronte di salita del trigger: se inizi a premere
            // puntando la palette → interazione palette per tutta la pressione; se inizi a
            // disegnare (non sulla palette) → la palette viene IGNORATA fino al rilascio,
            // anche se il raggio la attraversa a metà tratto.
            if (justPressed)
                startedOnPalette = onPalette;
            wasPressed = pressed;

            // Disegno soppresso: a trigger PREMUTO vale il latch (iniziato sulla palette),
            // ANCHE se il bersaglio sparisce a metà pressione (es. la ✕ che chiude il pannello
            // o "View Shortcuts" che lo cambia): senza questo, appena onPalette torna falso il
            // pennello riprendeva a disegnare un "pallino" finché si teneva il trigger. A riposo
            // il raggio fa solo da hint quando punta la palette.
            if (Brush != null) // la mano-palette non disegna: niente da sopprimere
                Brush.SuppressDrawing = pressed ? startedOnPalette : onPalette;

            // Il raggio è visibile quando punta la palette ed è coerente con la modalità latchata.
            bool showRay = onPalette && (!pressed || startedOnPalette);
            if (!showRay)
            {
                line.enabled = false;
                SetHoveredButton(null);
                return;
            }

            line.enabled = true;
            line.SetPosition(0, origin);
            line.SetPosition(1, hit.point);

            // Solo sui controlli c'è hover/press: sullo sfondo modale il ray si vede ma non preme.
            if (!onControl)
            {
                SetHoveredButton(null);
                return;
            }

            // Hover: evidenzia il pulsante puntato anche prima di premere.
            SetHoveredButton(hit.collider.GetComponent<PaletteButton>());

            if (!pressed)
                return;

            var go = hit.collider.gameObject;

            // Pulsanti: scattano sul fronte di salita (un click). Slider/picker: trascinano
            // di continuo finché il trigger è premuto.
            if (go.TryGetComponent<PaletteButton>(out var button))
            {
                if (justPressed)
                    button.Press();
            }
            else if (go.TryGetComponent<IPaletteControl>(out var control))
            {
                control.PressAt(hit.point);
            }
        }

        // Hit sul fondo del pannello modale aperto (Options/Shortcuts): serve solo a tenere
        // il raggio visibile sull'intera area del pannello. Non è un controllo: niente press.
        bool TryHitModalSurface(Vector3 origin, Vector3 dir, out RaycastHit hit)
        {
            hit = default;
            var modal = PaletteController.ModalRoot;
            if (modal == null)
                return false;

            int count = Physics.RaycastNonAlloc(origin, dir, rayBuffer, maxDistance, PaletteMask, QueryTriggerInteraction.Collide);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = rayBuffer[i];
                if (!h.transform.IsChildOf(modal)) // include il root del pannello stesso
                    continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    hit = h;
                    found = true;
                }
            }
            return found;
        }

        void SetHoveredButton(PaletteButton button)
        {
            if (hoveredButton == button)
                return;
            if (hoveredButton != null)
                hoveredButton.SetHover(false);
            hoveredButton = button;
            if (hoveredButton != null)
                hoveredButton.SetHover(true);
        }

        // Cerca il controllo della palette più vicino lungo il raggio, ignorando
        // collider non-palette (incluso il proprio BrushTip). Se non trova un hit
        // diretto, tenta lo snap-to-button assistito (vedi TrySnapToPaletteButton).
        // Buffer riusato: RaycastNonAlloc non alloca un array ad ogni frame come RaycastAll.
        static readonly RaycastHit[] rayBuffer = new RaycastHit[32];

        bool TryHitPalette(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, dir, rayBuffer, maxDistance, PaletteMask, QueryTriggerInteraction.Collide);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = rayBuffer[i];
                if (!IsPaletteControl(h.collider.gameObject))
                    continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    best = h;
                    found = true;
                }
            }

            // Snap-to-button: se il raycast diretto non ha trovato nulla, cerca un
            // PaletteButton nel cono di snapAngleDeg gradi attorno alla direzione del ray.
            // Ispirato alla raycast redirection di Gabel et al. (SUI 2024).
            if (!found)
                found = TrySnapToPaletteButton(origin, dir, out best);

            return found;
        }

        /// <summary>
        /// Scatta verso il PaletteButton più vicino all'asse del ray, se entro snapAngleDeg.
        /// Rilancia un raycast diretto verso il centro del button per ottenere un hit valido.
        /// </summary>
        bool TrySnapToPaletteButton(Vector3 origin, Vector3 dir, out RaycastHit result)
        {
            result = default;
            float bestAngle = snapAngleDeg;
            Collider bestCol = null;

            // Scorre il registro dei PaletteButton (pochi e noti) invece di interrogare
            // la fisica su tutta la scena: nessun Physics.OverlapSphere da 2 m per frame.
            var buttons = PaletteButton.Instances;
            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                if (btn == null || btn.Col == null) continue;
                // Niente snap verso i controlli "sotto" un menu modale aperto.
                if (!PaletteController.IsInteractable(btn.gameObject)) continue;
                var to = btn.Col.bounds.center - origin;
                if (to.sqrMagnitude > maxDistance * maxDistance) continue;
                float angle = Vector3.Angle(dir, to);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    bestCol = btn.Col;
                }
            }

            if (bestCol == null) return false;

            // Lancia un ray diretto verso il centro del button per ottenere l'hit esatto
            var snapDir = (bestCol.bounds.center - origin).normalized;
            return Physics.Raycast(origin, snapDir, out result, maxDistance, PaletteMask, QueryTriggerInteraction.Collide)
                   && result.collider == bestCol;
        }

        static bool IsPaletteControl(GameObject go) =>
            (go.GetComponent<PaletteButton>() != null
             || go.GetComponent<IPaletteControl>() != null)
            // Con un menu modale (Options) aperto, i controlli "sotto" non sono bersagli.
            && PaletteController.IsInteractable(go);
    }
}
