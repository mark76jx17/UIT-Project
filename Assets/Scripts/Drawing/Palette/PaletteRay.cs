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
        public BrushController Brush { get; set; }

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
            if (Brush == null)
                return;

            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;

            bool onPalette = TryHitPalette(origin, dir, out var hit);

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, StrokeSettings.BrushHand);
            bool pressed = trigger >= pressThreshold;
            bool justPressed = pressed && !wasPressed;

            // Latch della modalità al fronte di salita del trigger: se inizi a premere
            // puntando la palette → interazione palette per tutta la pressione; se inizi a
            // disegnare (non sulla palette) → la palette viene IGNORATA fino al rilascio,
            // anche se il raggio la attraversa a metà tratto. Così non si interagisce per
            // sbaglio con la palette mentre si disegna.
            if (justPressed)
                startedOnPalette = onPalette;
            wasPressed = pressed;

            // A trigger premuto vale la modalità latchata; a riposo il raggio fa solo da
            // hint quando punta la palette.
            bool paletteActive = onPalette && (!pressed || startedOnPalette);

            Brush.SuppressDrawing = paletteActive;

            if (!paletteActive)
            {
                line.enabled = false;
                SetHoveredButton(null);
                return;
            }

            line.enabled = true;
            line.SetPosition(0, origin);
            line.SetPosition(1, hit.point);

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
            go.GetComponent<PaletteButton>() != null
            || go.GetComponent<IPaletteControl>() != null;
    }
}
