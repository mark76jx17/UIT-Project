using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Palette stile Gravity Sketch (layout orizzontale): la striscia dei 4 pennelli
    /// è ancorata a destra del pannello principale (lato della mano che disegna); nel
    /// pannello: ruota colori + luminosità (alto-sx), toggle Pressure/Mirror (alto-dx),
    /// colori recenti, slider trasparenza, strumenti Draw/Fill/Erase, slider spessore,
    /// e in basso Undo/Redo/Save/Load. Si premono con la punta del pennello.
    /// Il tasto X (mano della palette) apre/chiude il pannello con un'animazione di
    /// scala; il pannello si nasconde anche mentre si disegna.
    /// </summary>
    public class PaletteController : MonoBehaviour
    {
        [Header("Aggancio al polso (regolabili da Inspector)")]
        public Vector3 localOffset = new(0f, 0.06f, 0.04f);
        public Vector3 localEuler = new(-40f, 180f, 0f);
        [Tooltip("Sul visore: quanto sopra la mano fluttua il pannello. Deve superare la " +
                 "metà altezza del pannello (~0.22) così il bordo inferiore resta sopra la " +
                 "mano e il controller non lo trapassa.")]
        public float heightAboveHand = 0.22f;

#if UNITY_EDITOR
        [SerializeField] bool pinPaletteInEditor = false;
        [SerializeField] float editorDistance = 0.72f;
        [SerializeField] Vector3 editorOffset = new(0.0f, 0f, 0f);
        [SerializeField] float editorScale = 1.25f;
#endif

        public BrushController Brush { get; set; }
        public Transform HandAnchor { get; set; }

        // Layer dedicato a TUTTI i controlli della palette: il PaletteRay raycasta solo
        // questo, ignorando i tratti disegnati. È un user-layer libero (senza nome
        // nell'Inspector, ma funzionale): cambialo se 30 fosse già usato nel progetto.
        public const int PaletteLayer = 30;

        static readonly Color PanelColor = new(0.10f, 0.10f, 0.12f, 0.96f);
        static readonly Color ButtonColor = new(0.22f, 0.22f, 0.27f, 1f);
        static readonly Color AccentColor = new(0.55f, 0.45f, 0.95f, 1f);
        static readonly Color TrackColor = new(0.32f, 0.32f, 0.38f, 1f);
        static readonly Color EmptySwatch = new(0.16f, 0.16f, 0.20f, 1f); // slot vuoto: grigio scuro opaco
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Ordini di disegno (tutti i materiali UI sono trasparenti con ZWrite off:
        // serve un render queue esplicito perché lo sfondo non copra i controlli).
        const int QueuePanel = 3000;   // sfondo pannello/striscia
        const int QueueControl = 3002; // bottoni, swatch, ruota, slider
        const int QueueIcon = 3003;    // icone/anteprime
        const int QueueText = 3004;    // etichette TMP

        // Dimensioni font (point-size TMP in unità locali metriche: ~0.003 m per unità).
        const float ButtonFont = 0.20f;   // Draw/Fill/Erase (riga larga)
        const float ToggleFont = 0.24f;
        const float SliderLabelFont = 0.13f; // label degli slider: piccola ma leggibile

        // Icone nei bottoni testuali (Draw/Fill/Erase, Undo/Redo/Save/Load): disattivate
        // su richiesta — solo label testuale. Il codice icone (ToolIcon/MakeIconImage)
        // resta nel repo: basta rimettere true per riattivarle.
        static readonly bool ShowButtonIcons = false;

        // anteprime tratto per i 4 pennelli, in ordine BrushType (Round/Ribbon/Glow/Dashed)
        static readonly BrushType[] BrushOrder = { BrushType.Round, BrushType.Ribbon, BrushType.Glow, BrushType.Dashed };

        GameObject panel;
        Renderer penButton, fillButton, eraserButton;
        Renderer[] brushButtons;
        Material[] brushPreviewMats; // anteprime tratto: la selezionata si tinge col colore corrente
        Renderer[] recentSwatches;
        readonly List<System.Action> toggleSync = new();

        // Camera.main fa una FindGameObjectWithTag interna a ogni chiamata: la testa
        // non cambia, quindi la cachiamo una volta invece di interrogarla per frame.
        Camera head;
        Camera Head => head != null ? head : (head = Camera.main);

        // Ultimo stato applicato a SyncSelection: riscriviamo il colore dei bottoni
        // solo quando cambia davvero, non a ogni frame.
        ToolMode lastTool = (ToolMode)(-1);
        int lastType = -1;

        // ---- apertura/chiusura animata col tasto X ----
        bool isOpen = true;
        float visibility = 1f;
        const float AnimDuration = 0.16f;

        void Start()
        {
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.Euler(localEuler);
            gameObject.AddComponent<UiFeedback>(); // suono + vibrazione centralizzati per la palette
            BuildPanel();
            // Tutti i controlli appena creati vanno sul layer della palette (per il ray).
            SetLayerRecursively(gameObject, PaletteLayer);
            StrokeSettings.RecentColorsChanged += RefreshRecents;
            RefreshRecents();
#if UNITY_EDITOR
            if (pinPaletteInEditor)
                PlacePaletteForEditor();
#endif
        }

        void OnDestroy() => StrokeSettings.RecentColorsChanged -= RefreshRecents;

#if UNITY_EDITOR
        void PlacePaletteForEditor()
        {
            if (Head == null)
                return;
            transform.SetParent(null, true);
            transform.position = Head.transform.position
                + Head.transform.forward * editorDistance
                + Head.transform.right * editorOffset.x
                + Head.transform.up * editorOffset.y;
            transform.rotation = Quaternion.LookRotation(transform.position - Head.transform.position, Vector3.up);
            transform.localScale = Vector3.one * editorScale;
        }
#endif

        void Update()
        {
            // Trigger della mano palette (sinistra) = apri/chiudi manualmente.
            // La mano palette non disegna, quindi il suo trigger è libero.
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, StrokeSettings.PaletteHand))
            {
                isOpen = !isOpen;
                UiFeedback.Instance?.PanelToggle(isOpen);
            }
#if UNITY_EDITOR
            // Nel simulatore non c'è il trigger del visore: il tasto P apre/chiude.
            if (UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.pKey.wasPressedThisFrame)
            {
                isOpen = !isOpen;
                UiFeedback.Instance?.PanelToggle(isOpen);
            }
#endif

            AnimateVisibility();
            SyncSelection();
        }

        void AnimateVisibility()
        {
            if (panel == null)
                return;
            // La palette resta visibile anche mentre si disegna: la apre/chiude solo
            // l'utente col trigger sinistro.
            bool wantVisible = isOpen;
            visibility = Mathf.MoveTowards(visibility, wantVisible ? 1f : 0f, Time.deltaTime / AnimDuration);

            float s = Mathf.SmoothStep(0f, 1f, visibility);
            panel.transform.localScale = new Vector3(s, s, s);

            bool active = visibility > 0.001f;
            if (panel.activeSelf != active)
                panel.SetActive(active);
        }

        void LateUpdate()
        {
#if UNITY_EDITOR
            if (pinPaletteInEditor)
                return;
#endif
            if (!UnityEngine.XR.XRSettings.isDeviceActive || HandAnchor == null)
                return;
            if (Head == null)
                return;
            var position = HandAnchor.position + Vector3.up * heightAboveHand;
            transform.position = position;
            var toPanel = position - Head.transform.position;
            if (toPanel.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
        }

        void SyncSelection()
        {
            if (penButton == null)
                return;
            // Strumento corrente: aggiorna i colori solo quando cambia, non per frame.
            if (StrokeSettings.Tool != lastTool)
            {
                lastTool = StrokeSettings.Tool;
                penButton.material.SetColor(BaseColorId, lastTool == ToolMode.Pen ? AccentColor : ButtonColor);
                fillButton.material.SetColor(BaseColorId, lastTool == ToolMode.Fill ? AccentColor : ButtonColor);
                eraserButton.material.SetColor(BaseColorId, lastTool == ToolMode.Eraser ? AccentColor : ButtonColor);
            }
            if ((int)StrokeSettings.Type != lastType)
            {
                lastType = (int)StrokeSettings.Type;
                for (int i = 0; i < brushButtons.Length; i++)
                    brushButtons[i].material.SetColor(BaseColorId, lastType == i ? AccentColor : ButtonColor);
            }
            // Indicatore "pennello corrente": l'anteprima del tipo selezionato è tinta col
            // colore attuale (le altre restano bianche) → colore + tipo a colpo d'occhio.
            if (brushPreviewMats != null)
            {
                var col = StrokeSettings.BaseColor;
                col.a = 1f;
                for (int i = 0; i < brushPreviewMats.Length; i++)
                    brushPreviewMats[i].SetColor(BaseColorId, (int)StrokeSettings.Type == i ? col : Color.white);
            }
            // I toggle si auto-aggiornano solo al cambio (vedi MakeToggleButton).
            foreach (var sync in toggleSync)
                sync();
        }

        void RefreshRecents()
        {
            if (recentSwatches == null)
                return;
            var recents = StrokeSettings.RecentColors;
            for (int i = 0; i < recentSwatches.Length; i++)
                recentSwatches[i].material.SetColor(BaseColorId, i < recents.Count ? recents[i] : EmptySwatch);
        }

        void BuildPanel()
        {
            panel = new GameObject("Panel");
            panel.transform.SetParent(transform, false);

            var panelSize = new Vector2(0.26f, 0.46f);
            const float pad = 0.012f, rowGap = 0.008f, colGap = 0.012f, z = -0.002f;

            MakeRounded(panel.transform, "MainPanel", Vector3.zero, panelSize, 0.030f, PanelColor, QueuePanel);

            BuildBrushStrip(panelSize);
            BuildActionStrip(panelSize);

            var layout = new PaletteLayout(panelSize, pad, rowGap, colGap, z);

            // ---------- COLOR (ruota a sx, anteprima tratto al centro, luminosità a dx) ----------
            var colorRow = layout.Row(0.12f);
            var wheelCell = colorRow.Left(0.10f);     // ruota a sinistra
            var brightCol = colorRow.Right(0.055f);   // luminosità (con label sopra) a destra
            var previewCell = colorRow.Fill();        // anteprima al centro (zona liberata)

            // Ruota colori
            var wheel = new GameObject("ColorWheel");
            wheel.transform.SetParent(panel.transform, false);
            wheel.transform.localPosition = wheelCell.Center;
            var colorWheel = wheel.AddComponent<ColorWheel>();
            float wheelDiameter = Mathf.Min(wheelCell.Size.x, wheelCell.Size.y);
            // Lo zoom di prossimità si ferma quando il bordo della ruota raggiunge il
            // bordo del pannello: limite = distanza dal centro ruota al bordo più vicino
            // del pannello, divisa per il raggio base.
            float halfX = panelSize.x * 0.5f, halfY = panelSize.y * 0.5f;
            float edgeDist = Mathf.Min(
                Mathf.Min(wheelCell.Center.x + halfX, halfX - wheelCell.Center.x),
                Mathf.Min(wheelCell.Center.y + halfY, halfY - wheelCell.Center.y));
            // Margine (< 1) per fermarsi un filo prima del bordo del pannello. REGOLA QUI.
            const float wheelZoomMargin = 0.92f;
            float wheelMaxZoom = edgeDist / (wheelDiameter * 0.5f) * wheelZoomMargin;
            colorWheel.Build(wheelDiameter, PanelColor, wheelMaxZoom);
            colorWheel.SetProximityTarget(Brush != null ? Brush.Tip : null);

            // Anteprima del tratto: rettangolo che assume colore + opacità + dimensione correnti.
            var previewGO = new GameObject("ColorPreview");
            previewGO.transform.SetParent(panel.transform, false);
            previewGO.transform.localPosition = previewCell.Center;
            previewGO.AddComponent<ColorPreview>().Build(
                new Vector2(previewCell.Size.x * 0.92f, previewCell.Size.y * 0.80f));

            // Slider luminosità a destra, con label "Bright" sopra.
            const float brightLabelH = 0.020f;
            float barH = brightCol.Size.y - brightLabelH - 0.004f;
            var bright = new GameObject("BrightnessSlider");
            bright.transform.SetParent(panel.transform, false);
            bright.transform.localPosition = new Vector3(
                brightCol.Center.x, brightCol.Center.y - brightLabelH * 0.5f - 0.002f, brightCol.Center.z);
            bright.AddComponent<BrightnessSlider>().Build(new Vector2(0.016f, barH));
            MakeLabel(panel.transform, "Bright",
                new Vector3(brightCol.Center.x, brightCol.Center.y + brightCol.Size.y * 0.5f - brightLabelH * 0.5f, -0.006f),
                new Vector2(brightCol.Size.x, brightLabelH), SliderLabelFont, TextAlignmentOptions.Center);
            layout.Gap(0.007f);

            var recentRow = layout.Row(0.030f);
            var recentCells = recentRow.Split(5);
            recentSwatches = new Renderer[5];
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var c = recentCells[i];
                var sw = MakeRoundedButton(panel.transform, $"Recent{i}", c.Center, c.Size,
                    0.006f, EmptySwatch, () =>
                    {
                        var r = StrokeSettings.RecentColors;
                        if (idx < r.Count) StrokeSettings.SetColor(r[idx]);
                    });
                recentSwatches[i] = sw.GetComponent<Renderer>();
            }
            layout.Gap(0.007f);

            var alphaRow = layout.Row(0.024f);
            var alphaLabel = alphaRow.Left(0.055f);
            var alphaCell = alphaRow.Fill();
            MakeLabel(panel.transform, "Opacity",
                new Vector3(alphaLabel.Center.x, alphaLabel.Center.y, -0.006f),
                alphaLabel.Size, SliderLabelFont, TextAlignmentOptions.Left);
            var alpha = new GameObject("AlphaSlider");
            alpha.transform.SetParent(panel.transform, false);
            alpha.transform.localPosition = alphaCell.Center;
            alpha.AddComponent<AlphaSlider>().Build(new Vector2(alphaCell.Size.x, 0.016f));

            // separator
            layout.Gap(0.008f);
            MakeRounded(panel.transform, "Sep1",
                new Vector3(0f, layout.Cursor, z),
                new Vector2(panelSize.x - pad * 2f, 0.002f),
                0.001f, TrackColor, QueueControl);
            layout.Gap(0.014f);


            // ---------- BRUSH ----------
            var pressureRow = layout.Row(0.034f);
            MakeToggleButton("Pressure", pressureRow.Fill(),
                () => StrokeSettings.SizeMode == SizeMode.PressureBrush,
                () => StrokeSettings.SizeMode = StrokeSettings.SizeMode == SizeMode.PressureBrush
                    ? SizeMode.FixedPen : SizeMode.PressureBrush);

            var sizeRow = layout.Row(0.024f);
            var sizeLabel = sizeRow.Left(0.055f);
            var sizeCell = sizeRow.Fill();
            MakeLabel(panel.transform, "Size",
                new Vector3(sizeLabel.Center.x, sizeLabel.Center.y, -0.006f),
                sizeLabel.Size, SliderLabelFont, TextAlignmentOptions.Left);
            var size = new GameObject("SizeSlider");
            size.transform.SetParent(panel.transform, false);
            size.transform.localPosition = sizeCell.Center;
            size.AddComponent<SizeSlider>().Build(new Vector2(sizeCell.Size.x, 0.018f));

            // separator
            layout.Gap(0.012f);
            MakeRounded(panel.transform, "Sep2",
                new Vector3(0f, layout.Cursor, z),
                new Vector2(panelSize.x - pad * 2f, 0.002f),
                0.001f, TrackColor, QueueControl);
            layout.Gap(0.014f);


            // ---------- TOOL ----------
            var toolsRow = layout.Row(0.038f);
            var toolCells = toolsRow.Split(3);
            penButton = MakeTextButton("Draw", "pencil", toolCells[0],
                () => StrokeSettings.Tool = ToolMode.Pen, ButtonFont);
            fillButton = MakeTextButton("Fill", "droplet", toolCells[1],
                () => StrokeSettings.Tool = ToolMode.Fill, ButtonFont);
            eraserButton = MakeTextButton("Erase", "eraser", toolCells[2],
                () => StrokeSettings.Tool = ToolMode.Eraser, ButtonFont);

            layout.Gap(0.010f);

            var toggleRow = layout.Row(0.034f);
            var toggleCells = toggleRow.Split(3);
            MakeToggleButton("Mirror", toggleCells[0],
                () => Mirror.Enabled,
                () => Mirror.Toggle(Head != null ? Head.transform : transform));
            MakeToggleButton("Grid", toggleCells[1],
                () => ReferenceGrid.Enabled,
                () => ReferenceGrid.Toggle(Head != null ? Head.transform : transform));
            MakeToggleButton("Snap", toggleCells[2],
                () => StrokeSettings.SnapAxis,
                () => StrokeSettings.SnapAxis = !StrokeSettings.SnapAxis);
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        // Striscia verticale dei 4 tipi di pennello, ancorata a destra del pannello
        // (lato della mano che disegna). Ogni bottone mostra un'anteprima del tratto.
        void BuildBrushStrip(Vector2 panelSize)
        {
            const float bSize = 0.048f, bGap = 0.012f;
            int n = BrushOrder.Length;
            float contentH = n * bSize + (n - 1) * bGap;
            var stripSize = new Vector2(bSize + 0.014f, contentH + 0.014f);
            float stripX = -panelSize.x * 0.5f - 0.012f - stripSize.x * 0.5f;

            var strip = MakeRounded(panel.transform, "BrushStrip", new Vector3(stripX, 0f, 0f),
                stripSize, 0.016f, PanelColor, QueuePanel);

            brushButtons = new Renderer[n];
            brushPreviewMats = new Material[n];
            float y0 = (contentH - bSize) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var type = BrushOrder[i];
                float y = y0 - i * (bSize + bGap);
                var b = MakeRoundedButton(strip.transform, type.ToString(), new Vector3(0f, y, -0.004f),
                    new Vector2(bSize, bSize), 0.011f, ButtonColor, () => StrokeSettings.Type = type);
                brushPreviewMats[i] = MakeTexQuad(b.transform, BrushPreview.Get(type),
                    new Vector3(0f, 0f, -0.004f), new Vector2(bSize * 0.74f, bSize * 0.44f));
                brushButtons[i] = b.GetComponent<Renderer>();
            }
        }

        // Striscia verticale Undo/Redo/Save/Load, ancorata a destra del pannello.
        // Solo icone (niente testo), per accorciare il pannello principale.
        void BuildActionStrip(Vector2 panelSize)
        {
            var actions = new (string name, System.Action action)[]
            {
                ("undo", StrokeHistory.Undo),
                ("redo", StrokeHistory.Redo),
                ("save", DrawingStore.Save),
                ("load", DrawingStore.Load),
                ("clear", DrawingStore.NewScene), // svuota la scena (con backup automatico)
            };
            const float bSize = 0.040f, bGap = 0.010f;
            int n = actions.Length;
            float contentH = n * bSize + (n - 1) * bGap;
            var stripSize = new Vector2(bSize + 0.012f, contentH + 0.012f);
            float stripX = panelSize.x * 0.5f + 0.012f + stripSize.x * 0.5f;

            var strip = MakeRounded(panel.transform, "ActionStrip", new Vector3(stripX, 0f, 0f),
                stripSize, 0.014f, PanelColor, QueuePanel);

            float y0 = (contentH - bSize) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float y = y0 - i * (bSize + bGap);
                var b = MakeRoundedButton(strip.transform, actions[i].name, new Vector3(0f, y, -0.004f),
                    new Vector2(bSize, bSize), 0.010f, ButtonColor, actions[i].action);
                MakeIconImage(b.transform, actions[i].name, new Vector3(0f, 0f, -0.004f), bSize * 0.55f);
            }
        }

        // Pulsante con icona a sinistra + testo a destra, senza sovrapposizioni
        // (Draw/Fill/Erase, Undo/Redo/Save/Load).
        Renderer MakeTextButton(string label, string iconName, Cell cell, System.Action action, float fontSize)
        {
            var b = MakeRoundedButton(panel.transform, label, cell.Center, cell.Size,
                Mathf.Min(0.008f, cell.Size.y * 0.3f), ButtonColor, action);

            float w = cell.Size.x;
            if (ShowButtonIcons)
            {
                float iconSize = Mathf.Min(cell.Size.y * 0.55f, 0.015f);
                float iconX = -w * 0.5f + iconSize * 0.5f + 0.005f;
                MakeIconImage(b.transform, iconName, new Vector3(iconX, 0f, -0.004f), iconSize);
            }

            // Solo label testuale, centrata sull'intero bottone (allineamento Center su box
            // a piena larghezza). Icone disattivate via ShowButtonIcons.
            MakeLabel(b.transform, label, new Vector3(0f, 0f, -0.004f),
                new Vector2(w, cell.Size.y), fontSize, TextAlignmentOptions.Center);
            return b.GetComponent<Renderer>();
        }

        // Toggle compatto: bottone che si illumina (accent) quando attivo. Niente pillola.
        void MakeToggleButton(string label, Cell cell, System.Func<bool> isOn, System.Action onToggle)
        {
            var b = MakeRoundedButton(panel.transform, label + "Toggle", cell.Center, cell.Size,
                Mathf.Min(0.012f, cell.Size.y * 0.4f), ButtonColor, onToggle);
            // È un toggle: il feedback userà il suono on/off in base al nuovo stato.
            b.GetComponent<PaletteButton>().ToggleState = isOn;
            MakeLabel(b.transform, label, new Vector3(0f, 0f, -0.004f), cell.Size, ToggleFont, TextAlignmentOptions.Center);
            var r = b.GetComponent<Renderer>();
            // Aggiorna il colore solo quando lo stato del toggle cambia davvero.
            bool initialized = false;
            bool last = false;
            toggleSync.Add(() =>
            {
                bool on = isOn();
                if (initialized && on == last)
                    return;
                initialized = true;
                last = on;
                r.material.SetColor(BaseColorId, on ? AccentColor : ButtonColor);
            });
        }


        // Quad con texture (anteprima tratto, ruota, ecc.) su materiale URP/Unlit.
        Material MakeTexQuad(Transform parent, Texture2D tex, Vector3 localPos, Vector2 size)
        {
            var go = new GameObject("Preview");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var mat = BrushMaterials.CreateUnlit(Color.white);
            mat.SetTexture("_BaseMap", tex);
            mat.renderQueue = QueueIcon;
            // Preserva l'alpha opaco del bottone sotto: niente see-through sul passthrough
            // dove l'icona/anteprima è trasparente.
            BrushMaterials.PreserveDestAlpha(mat);
            go.AddComponent<MeshRenderer>().material = mat;
            return mat;
        }

        // Icona procedurale (ToolIcon) su quad URP/Unlit. Glifi bianchi coerenti,
        // nitidi a ogni dimensione, senza problemi di import/sfondo dei PNG.
        void MakeIconImage(Transform parent, string name, Vector3 localPos, float size)
        {
            MakeTexQuad(parent, ToolIcon.Get(name), localPos, new Vector2(size, size));
        }

        // opaque=true (default) per sfondi/controlli: niente see-through sul passthrough,
        // sorting per depth. La render queue esplicita serve solo ai materiali trasparenti.
        GameObject MakeRounded(Transform parent, string name, Vector3 localPos, Vector2 size, float corner, Color color, int queue = QueueControl, bool opaque = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(size.x, size.y, corner);
            var mat = BrushMaterials.CreateUnlit(color, opaque);
            if (!opaque)
                mat.renderQueue = queue;
            go.AddComponent<MeshRenderer>().material = mat;
            return go;
        }

        GameObject MakeRoundedButton(Transform parent, string name, Vector3 localPos, Vector2 size, float corner, Color color, System.Action action)
        {
            var go = MakeRounded(parent, name, localPos, size, corner, color, QueueControl);
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y, 0.012f);
            go.AddComponent<PaletteButton>().OnPressed += action;
            return go;
        }

        // Etichetta TextMeshPro a dimensione fissa (niente auto-size che gonfia il testo).
        void MakeLabel(Transform parent, string text, Vector3 localPos, Vector2 box, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.color = Color.white;
            tmp.alignment = align;
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.rectTransform.sizeDelta = box;
            // Appena creato via AddComponent, il TextMeshPro può non avere ancora font e
            // material condiviso inizializzati: accedere a tmp.fontMaterial creerebbe
            // un'istanza da una source null → ArgumentNullException che abortisce l'intera
            // BuildPanel (e fa sparire tutti i controlli dopo il primo testo). Garantisco
            // un font valido e imposto la render queue solo se il material è disponibile.
            if (tmp.font == null)
                tmp.font = TMP_Settings.defaultFontAsset;
            if (tmp.fontSharedMaterial != null)
                tmp.fontMaterial.renderQueue = QueueText;
        }
    }
}
