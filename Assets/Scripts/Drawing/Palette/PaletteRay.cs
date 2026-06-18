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
    /// </summary>
    public class PaletteRay : MonoBehaviour
    {
        public BrushController Brush { get; set; }

        [SerializeField] float maxDistance = 2f;
        [SerializeField] float pressThreshold = 0.55f;

        LineRenderer line;
        bool wasPressed;

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
            Brush.SuppressDrawing = onPalette;

            if (!onPalette)
            {
                line.enabled = false;
                wasPressed = false;
                return;
            }

            line.enabled = true;
            line.SetPosition(0, origin);
            line.SetPosition(1, hit.point);

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, StrokeSettings.BrushHand);
            bool pressed = trigger >= pressThreshold;
            var go = hit.collider.gameObject;

            // Pulsanti: scattano sul fronte di salita (un click). Slider/ruota: trascinano
            // di continuo finché il trigger è premuto.
            if (go.TryGetComponent<PaletteButton>(out var button))
            {
                if (pressed && !wasPressed)
                    button.Press();
            }
            else if (pressed)
            {
                if (go.TryGetComponent<ColorWheel>(out var wheel)) wheel.PressAt(hit.point);
                else if (go.TryGetComponent<AlphaSlider>(out var alpha)) alpha.PressAt(hit.point);
                else if (go.TryGetComponent<SizeSlider>(out var size)) size.PressAt(hit.point);
                else if (go.TryGetComponent<BrightnessSlider>(out var bright)) bright.PressAt(hit.point);
            }

            wasPressed = pressed;
        }

        // Cerca il controllo della palette più vicino lungo il raggio, ignorando
        // collider non-palette (incluso il proprio BrushTip).
        bool TryHitPalette(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            var hits = Physics.RaycastAll(origin, dir, maxDistance, ~0, QueryTriggerInteraction.Collide);
            float nearest = float.MaxValue;
            bool found = false;
            foreach (var h in hits)
            {
                if (!IsPaletteControl(h.collider.gameObject))
                    continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    best = h;
                    found = true;
                }
            }
            return found;
        }

        static bool IsPaletteControl(GameObject go) =>
            go.GetComponent<PaletteButton>() != null
            || go.GetComponent<ColorWheel>() != null
            || go.GetComponent<AlphaSlider>() != null
            || go.GetComponent<SizeSlider>() != null
            || go.GetComponent<BrightnessSlider>() != null;
    }
}
