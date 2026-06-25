using UnityEngine;
using UnityEngine.InputSystem;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Simulatore desktop per testare il disegno in editor senza visore:
    /// il pennello segue il mouse nella Game view a una distanza regolabile
    /// con la rotella. Aggiunto automaticamente da DrawingRig solo in editor.
    ///
    /// Comandi:
    ///   click sinistro (tenuto)  = trigger / disegna; click rapido = punto
    ///   click su un pulsante della palette = lo preme (non disegna)
    ///   click destro (tenuto) su un tratto = lo trascina
    ///   rotella                  = pressione (mentre disegni) / distanza pennello (a riposo)
    ///   1..8                     = colori della palette
    ///   , / .                    = pressione simulata giù/su (= spessore)
    ///   A                        = cicla la trasparenza (100/70/40%)
    ///   Z / X                    = undo / redo
    ///   E                        = modalità gomma on/off (col click cancelli)
    ///   F                        = strumento Riempi on/off (il contorno si riempie)
    ///   B                        = cicla il tipo di pennello (Tubo/Nastro/Glow/Tratto)
    ///   M                        = specchio/simmetria on/off
    ///   G                        = griglia di riferimento a pavimento on/off
    ///   D                        = duplica l'oggetto sotto il cursore
    ///   I                        = contagocce: preleva il colore dell'oggetto sotto il cursore
    ///   P                        = apri/chiudi il pannello della palette
    ///   F5 / F9                  = salva / carica il disegno
    ///   N                        = nuovo disegno: svuota la scena (con backup automatico)
    ///   O                        = esporta scena come OBJ in persistentDataPath
    /// </summary>
    [DefaultExecutionOrder(-10)] // posiziona il pennello prima che BrushController campioni
    [RequireComponent(typeof(BrushController))]
    public class DesktopBrushSimulator : MonoBehaviour
    {
        [SerializeField] float distance = 0.6f;
        [Range(0.6f, 1f)]
        [Tooltip("Pressione simulata del trigger: il mouse non ce l'ha, si regola con , e .")]
        [SerializeField] float pressure = 0.85f;

        // Colori rapidi per i tasti 1..8 (la palette ora usa la ruota HSV).
        static readonly Color[] QuickColors =
        {
            Color.white, Color.black, Color.red, new(1f, 0.55f, 0f),
            Color.yellow, Color.green, new(0.2f, 0.45f, 1f), Color.magenta,
        };

        BrushController brush;
        Transform dragged;
        Transform hovered;
        float dragDistance;
        Vector3 dragOffset;

        // In editor, anche se c'è il simulatore XR, forziamo i controlli desktop per testare senza visore
        [SerializeField] bool forceDesktopControlsInEditor = true;

        void Awake()
        {
            brush = GetComponent<BrushController>();
        }

        void Update()
        {
            // Se c'è un runtime XR attivo (es. Meta XR Simulator), comandano i
            // controller simulati: il mouse non deve sovrascrivere il trigger.
            if (UnityEngine.XR.XRSettings.isDeviceActive && !forceDesktopControlsInEditor)
            {
                brush.TriggerOverride = null;
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var camera = Camera.main;
            if (mouse == null || keyboard == null || camera == null)
                return;

            // Rotella: mentre disegni regola la PRESSIONE (così provi la modulazione di
            // spessore in editor); altrimenti avvicina/allontana il pennello.
            float scrollY = mouse.scroll.ReadValue().y;
            if (mouse.leftButton.isPressed)
                pressure = Mathf.Clamp(pressure + scrollY * 0.001f, 0.55f, 1f);
            else
                distance = Mathf.Clamp(distance + scrollY * 0.0005f, 0.2f, 3f);
            var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            transform.position = ray.origin + ray.direction * distance;

            // Un solo raycast: cosa c'è sotto il cursore?
            PaletteButton hoveredButton = null;
            IPaletteControl hoveredControl = null; // ruota colori / slider (dispatch unificato)

            Transform hoveredItem = null;   // oggetto disegnato (gomma/contagocce/hover)
            Transform hoveredGrab = null;   // afferrabile (anche il piano specchio)
            if (Physics.Raycast(ray, out var hit, 10f))
            {
                hit.collider.TryGetComponent(out hoveredButton);
                hit.collider.TryGetComponent<IPaletteControl>(out hoveredControl);
                var item = hit.collider.GetComponentInParent<DrawnItem>();
                if (item != null)
                    hoveredItem = item.transform;
                hoveredGrab = GrabController.GrabRoot(hit.collider);
            }
            SetHover(hoveredItem);

            // Tasto destro: trascina l'oggetto (o il piano specchio) sotto il cursore.
            if (mouse.rightButton.wasPressedThisFrame && hoveredGrab != null)
            {
                dragged = hoveredGrab;
                dragDistance = hit.distance;
                dragOffset = dragged.position - hit.point;
                StrokeHighlight.Set(dragged, 1.45f);
            }
            if (dragged != null)
            {
                if (!mouse.rightButton.isPressed)
                {
                    StrokeHighlight.Clear(dragged);
                    dragged = null;
                }
                else
                {
                    dragged.position = ray.origin + ray.direction * dragDistance + dragOffset;
                }
            }

            if (hoveredButton != null)
            {
                // Sopra un pulsante della palette il click preme il pulsante
                // invece di disegnarci sopra.
                brush.TriggerOverride = 0f;
                if (mouse.leftButton.wasPressedThisFrame)
                    hoveredButton.Press();
            }

            // Sopra il picker colore o gli slider, il click interagisce con loro invece di disegnare
            else if (hoveredControl != null)
            {
                // Ruota colori o slider: il click trascina invece di disegnare.
                brush.TriggerOverride = 0f;
                if (mouse.leftButton.isPressed)
                    hoveredControl.PressAt(hit.point);
            }

            else if (StrokeSettings.EraserMode)
            {
                // Gomma: il click nasconde l'oggetto puntato (cancellazione annullabile).
                brush.TriggerOverride = 0f;
                if (mouse.leftButton.wasPressedThisFrame && hoveredItem != null)
                {
                    StrokeHighlight.Clear(hoveredItem);
                    if (hovered == hoveredItem) hovered = null;
                    hoveredItem.gameObject.SetActive(false);
                    StrokeHistory.PushErase(hoveredItem.gameObject);
                }
            }
            else
            {
                brush.TriggerOverride = mouse.leftButton.isPressed ? pressure : 0f;
            }

            if (keyboard.zKey.wasPressedThisFrame) StrokeHistory.Undo();
            if (keyboard.xKey.wasPressedThisFrame) StrokeHistory.Redo();
            if (keyboard.dKey.wasPressedThisFrame)
            {
                var target = dragged != null ? dragged : hovered;
                if (target != null)
                {
                    var copy = DrawingStore.Duplicate(target);
                    if (copy != null)
                    {
                        copy.transform.position += Vector3.up * 0.04f;
                        StrokeHistory.Push(copy);
                    }
                }
            }
            if (keyboard.iKey.wasPressedThisFrame && hoveredItem != null)
            {
                // Contagocce: preleva il colore dell'oggetto sotto il cursore.
                var record = hoveredItem.GetComponentInChildren<StrokeRecord>();
                if (record != null) StrokeSettings.SetColor(record.color);
            }
            if (keyboard.commaKey.wasPressedThisFrame) pressure = Mathf.Max(0.6f, pressure - 0.08f);
            if (keyboard.periodKey.wasPressedThisFrame) pressure = Mathf.Min(1f, pressure + 0.08f);
            if (keyboard.aKey.wasPressedThisFrame) CycleAlpha();
            if (keyboard.eKey.wasPressedThisFrame)
                StrokeSettings.Tool = StrokeSettings.EraserMode ? ToolMode.Pen : ToolMode.Eraser;
            if (keyboard.fKey.wasPressedThisFrame)
                StrokeSettings.Tool = StrokeSettings.FillMode ? ToolMode.Pen : ToolMode.Fill;
            if (keyboard.mKey.wasPressedThisFrame)
                Mirror.Toggle(camera.transform);
            if (keyboard.gKey.wasPressedThisFrame)
                ReferenceGrid.Toggle(camera.transform);
            if (keyboard.bKey.wasPressedThisFrame)
                StrokeSettings.Type = (BrushType)(((int)StrokeSettings.Type + 1) % 4);
            if (keyboard.f5Key.wasPressedThisFrame) DrawingStore.Save();
            if (keyboard.f9Key.wasPressedThisFrame) DrawingStore.Load();
            if (keyboard.nKey.wasPressedThisFrame)  DrawingStore.NewScene();
            if (keyboard.oKey.wasPressedThisFrame)  DrawingStore.ExportOBJ();

            for (int i = 0; i < QuickColors.Length; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                    StrokeSettings.SetColor(QuickColors[i]);
        }

        void SetHover(Transform target)
        {
            if (hovered == target || dragged != null)
                return;
            if (hovered != null)
                StrokeHighlight.Clear(hovered);
            hovered = target;
            if (hovered != null)
                StrokeHighlight.Set(hovered, 1.2f);
        }

        // La trasparenza ora è continua (slider): il tasto A la fa scendere a
        // gradini e poi torna piena, per provarla velocemente in editor.
        static void CycleAlpha()
        {
            float a = StrokeSettings.Alpha - 0.3f;
            StrokeSettings.Alpha = a < 0.25f ? 1f : a;
        }
    }
}
