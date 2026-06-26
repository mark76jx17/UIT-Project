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
                 "metà altezza del pannello (~0.24) così il bordo inferiore resta sopra la " +
                 "mano e il controller che la tiene non lo trapassa.")]
        public float heightAboveHand = 0.30f;

#if UNITY_EDITOR
        [SerializeField] bool pinPaletteInEditor = true;
        [SerializeField] float editorDistance = 0.75f;
        [SerializeField] Vector3 editorOffset = new(-0.10f, 0.45f, 0f);
        [SerializeField] float editorScale = 1.35f;
#endif

        public BrushController Brush { get; set; }
        public Transform HandAnchor { get; set; }

        // Layer dedicato a TUTTI i controlli della palette: il PaletteRay raycasta solo
        // questo, ignorando i tratti disegnati. È un user-layer libero (senza nome
        // nell'Inspector, ma funzionale): cambialo se 30 fosse già usato nel progetto.
        public const int PaletteLayer = 30;

        // Quando un sotto-pannello modale è aperto (es. Options), SOLO i controlli sotto
        // questo nodo sono interagibili: tutto il resto della palette resta visibile ma
        // "sotto" e non risponde a ray/punta, così non si tocca per sbaglio. PaletteRay e
        // PaletteButton consultano IsInteractable prima di agire.
        public static Transform ModalRoot;

        // Il bottone ellipsi (...) che apre/chiude il menu Options resta SEMPRE premibile,
        // anche mentre il menu modale è aperto: così funziona da vero toggle (un click apre,
        // il click successivo sullo stesso bottone chiude). Senza questa eccezione il bottone
        // sarebbe "sotto" il proprio modale e non potrebbe richiuderlo.
        public static GameObject OptionsToggleButton;
        public static bool IsInteractable(GameObject go)
            => ModalRoot == null
               || go.transform.IsChildOf(ModalRoot)
               || go == OptionsToggleButton;

        static readonly Color PanelColor = new(0.10f, 0.10f, 0.12f, 0.96f);
        static readonly Color ButtonColor = new(0.22f, 0.22f, 0.27f, 1f);
        static readonly Color AccentColor = new(0.55f, 0.45f, 0.95f, 1f);
        static readonly Color TrackColor = new(0.32f, 0.32f, 0.38f, 1f);
        static readonly Color EmptySwatch = new(0.16f, 0.16f, 0.20f, 1f); // slot vuoto: grigio scuro opaco

        // Solo per il tool di anteprima: disegna una griglia normalizzata (-1..1) su ogni
        // controller per tarare a occhio gli anchor dei tasti. Mai true a runtime.
        public static bool DebugAnchorGrid;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Ordini di disegno (tutti i materiali UI sono trasparenti con ZWrite off:
        // serve un render queue esplicito perché lo sfondo non copra i controlli).
        const int QueuePanel = 3000;   // sfondo pannello/striscia
        const int QueueControl = 3002; // bottoni, swatch, ruota, slider
        const int QueueIcon = 3003;    // icone/anteprime
        const int QueueText = 3004;    // etichette TMP

        // Dimensioni font (point-size TMP in unità locali metriche: ~0.003 m per unità).
        const float ButtonFont = 0.20f;   // Draw/Fill/Erase (riga larga)
        const float ToggleFont = ButtonFont; // Pressure/Mirror/Grid/Snap uguali a Draw/Fill/Erase
        const float SliderLabelFont = 0.13f; // label degli slider: piccola ma leggibile

        const float ToolSmallLabelFont = 0.13f;

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

        // Sotto-pannello "Options" (impostazioni). Vive a parte rispetto a `panel` così un
        // Rebuild del pannello principale (es. al toggle mancino) non distrugge il controllo
        // sotto la punta del pennello. Il suo sync dei toggle sta in una lista separata che
        // NON viene svuotata dal Rebuild.
        GameObject optionsPanel;
        bool optionsOpen;
        readonly List<System.Action> optionsSync = new();

        // Pannello "View Shortcuts" (sola lettura): elenca le scorciatoie da controller
        // (vedi ControllerShortcuts.All). Aperto dal bottone ⚡ nel menu Options; come
        // optionsPanel vive a parte e non viene distrutto dal Rebuild.
        GameObject shortcutsPanel;
        bool shortcutsOpen;

        // Geometria per ancorare i pop-up (Options/Shortcuts) accanto al bottone ellipsi
        // (...) della striscia azioni: lato (destra per i destri), bordo esterno X della
        // striscia e Y del bottone. Aggiornati a ogni BuildPanel; letti da PositionMenus.
        float menuSide = 1f;
        float actionStripOuterX;
        float optionsButtonY;
        Vector2 optionsSize, shortcutsSize;

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
            BuildOptionsPanel(); // sotto-pannello impostazioni (separato, sopravvive ai Rebuild)
            BuildShortcutsPanel(); // pannello scorciatoie in sola lettura (aperto da Options)
            PositionMenus(); // ancora i pop-up accanto al bottone ellipsi, sul lato giusto
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
                + Vector3.up * editorOffset.y;
            var toPanel = transform.position - Head.transform.position;
            toPanel.y = 0f;

            if (toPanel.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
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

            // I popup Options/Shortcuts seguono il pannello: visibili solo se aperti E la
            // palette è aperta. Shortcuts sta "sopra" Options (lo nasconde mentre è aperto).
            bool showShortcuts = shortcutsOpen && active;
            bool showOptions = optionsOpen && active && !showShortcuts;
            if (optionsPanel != null && optionsPanel.activeSelf != showOptions)
                optionsPanel.SetActive(showOptions);
            if (shortcutsPanel != null && shortcutsPanel.activeSelf != showShortcuts)
                shortcutsPanel.SetActive(showShortcuts);
            // Modale: mentre un popup è mostrato, il resto della palette non è interagibile.
            ModalRoot = showShortcuts ? shortcutsPanel.transform
                      : showOptions ? optionsPanel.transform
                      : null;
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
            // Toggle del sotto-pannello Options (lista separata, non azzerata dal Rebuild).
            foreach (var sync in optionsSync)
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

            var panelSize = new Vector2(0.29f, 0.48f);
            const float pad = 0.012f, rowGap = 0.008f, colGap = 0.012f, z = -0.002f;

            MakeRounded(panel.transform, "MainPanel", Vector3.zero, panelSize, 0.030f, PanelColor, QueuePanel);

            BuildBrushStrip(panelSize);
            BuildActionStrip(panelSize);

            var layout = new PaletteLayout(panelSize, pad, rowGap, colGap, z);

            // ---------- COLOR (ruota · checker · luminosità, allineati; label "Bright" in alto) ----------
            var colorRow = layout.Row(0.12f);
            var wheelCell = colorRow.Left(0.10f);     // ruota a sinistra
            var brightCol = colorRow.Right(0.08f);    // luminosità a destra (largo per la label "Brightness")
            var previewCell = colorRow.Fill();        // anteprima/checker al centro

            // I 3 elementi condividono ALTEZZA e CENTRO verticale → allineati (top/bottom a
            // livello). In alto resta una fascetta per la label "Bright": tutti e tre
            // scendono della stessa quantità, quindi restano allineati tra loro.
            const float brightLabelH = 0.018f;
            float colH = wheelCell.Size.y - brightLabelH;            // altezza comune
            float elemY = wheelCell.Center.y - brightLabelH * 0.5f;  // centro verticale comune

            // Ruota colori
            var wheel = new GameObject("ColorWheel");
            wheel.transform.SetParent(panel.transform, false);
            var wheelPos = new Vector3(wheelCell.Center.x, elemY, wheelCell.Center.z);
            wheel.transform.localPosition = wheelPos;
            var colorWheel = wheel.AddComponent<ColorWheel>();
            float wheelDiameter = Mathf.Min(wheelCell.Size.x, colH);
            // Lo zoom di prossimità si ferma quando il bordo della ruota raggiunge il
            // bordo del pannello: limite = distanza dal centro ruota al bordo più vicino
            // del pannello, divisa per il raggio base.
            float halfX = panelSize.x * 0.5f, halfY = panelSize.y * 0.5f;
            float edgeDist = Mathf.Min(
                Mathf.Min(wheelPos.x + halfX, halfX - wheelPos.x),
                Mathf.Min(wheelPos.y + halfY, halfY - wheelPos.y));
            // Margine (< 1) per fermarsi un filo prima del bordo del pannello. REGOLA QUI.
            const float wheelZoomMargin = 0.92f;
            float wheelMaxZoom = edgeDist / (wheelDiameter * 0.5f) * wheelZoomMargin;
            colorWheel.Build(wheelDiameter, PanelColor, wheelMaxZoom);
            colorWheel.SetProximityTarget(Brush != null ? Brush.Tip : null);

            // Anteprima/checker: stessa altezza della ruota, ma centrata esattamente a
            // METÀ tra il centro della ruota e il centro dello slider luminosità. La cella
            // Fill non basta: lo slider è sottile e sta al centro della sua colonna larga,
            // quindi lo spazio visivo a destra sarebbe maggiore. Uso il punto medio reale.
            // +verso lo slider (destra), -verso la ruota. REGOLA QUI.
            const float previewNudge = 0.012f;
            float previewX = (wheelCell.Center.x + brightCol.Center.x) * 0.5f + previewNudge;
            var previewGO = new GameObject("ColorPreview");
            previewGO.transform.SetParent(panel.transform, false);
            previewGO.transform.localPosition = new Vector3(previewX, elemY, previewCell.Center.z);
            previewGO.AddComponent<ColorPreview>().Build(new Vector2(previewCell.Size.x * 0.9f, colH));

            // Slider luminosità: stessa altezza e centro; label "Bright" nella fascetta in alto.
            var bright = new GameObject("BrightnessSlider");
            bright.transform.SetParent(panel.transform, false);
            bright.transform.localPosition = new Vector3(brightCol.Center.x, elemY, brightCol.Center.z);
            bright.AddComponent<BrightnessSlider>().Build(new Vector2(0.016f, colH));
            MakeLabel(panel.transform, "Brightness",
                new Vector3(brightCol.Center.x, wheelCell.Center.y + wheelCell.Size.y * 0.5f - brightLabelH * 0.5f, -0.006f),
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


            // ---------- MIRROR / GRID / SNAP ----------
            var toggleRow = layout.Row(0.034f);
            var toggleCells = toggleRow.Split(3);

            MakeToggleButton("Mirror", toggleCells[0],
                () => Mirror.Enabled,
                () => Mirror.Toggle(Head != null ? Head.transform : transform));

            MakeToggleButton("Grid", toggleCells[1],
                () => ReferenceGrid.Enabled,
                () => ReferenceGrid.Toggle(Head != null ? Head.transform : transform));

            MakeToggleButton("Line", toggleCells[2],
                () => StrokeSettings.SnapAxis,
                () => StrokeSettings.SnapAxis = !StrokeSettings.SnapAxis);


            // ---------- SEPARATOR ----------
            layout.Gap(0.010f);

            MakeRounded(panel.transform, "Sep3",
                new Vector3(0f, layout.Cursor, z),
                new Vector2(panelSize.x - pad * 2f, 0.002f),
                0.001f, TrackColor, QueueControl);

            layout.Gap(0.019f);


            // ---------- TOOL ----------
            var toolsRow = layout.Row(0.044f);
            var toolCells = toolsRow.Split(3);

            penButton = MakeToolButton("Draw", "pencil", toolCells[0],
                () => StrokeSettings.Tool = ToolMode.Pen);

            fillButton = MakeToolButton("Fill", "droplet", toolCells[1],
                () => StrokeSettings.Tool = ToolMode.Fill);

            eraserButton = MakeToolButton("Erase", "eraser", toolCells[2],
                () => StrokeSettings.Tool = ToolMode.Eraser);
        }

        // Ricostruisce il pannello principale (es. dopo il toggle "Left-Handed Mode": le
        // strisce pennelli/azioni si specchiano sul nuovo lato). Il sotto-pannello Options
        // è separato e resta intatto: il controllo sotto la punta non viene distrutto.
        public void Rebuild()
        {
            if (panel != null)
                Destroy(panel);
            toggleSync.Clear();
            lastTool = (ToolMode)(-1);
            lastType = -1;
            BuildPanel();
            PositionMenus(); // il lato della striscia azioni è cambiato: riancora i pop-up
            SetLayerRecursively(gameObject, PaletteLayer);
            RefreshRecents();
        }

        // Ancora i pop-up Options/Shortcuts accanto al bottone ellipsi (...) della striscia
        // azioni: a destra del bottone per i destri, a sinistra per i mancini (mai sopra la
        // palette). Chiamato all'avvio e a ogni Rebuild, così la posizione segue la mano.
        void PositionMenus()
        {
            const float gap = 0.014f;
            const float paletteHalfH = 0.24f;

            if (optionsPanel != null)
            {
                float cx = actionStripOuterX + menuSide * (gap + optionsSize.x * 0.5f);
                // Centrato sul bottone ellipsi, ma vincolato a restare tutto entro l'altezza
                // della palette (così non sborda sopra il bordo né sotto la mano).
                float cy = Mathf.Clamp(optionsButtonY,
                    -paletteHalfH + optionsSize.y * 0.5f, paletteHalfH - optionsSize.y * 0.5f);
                optionsPanel.transform.localPosition = new Vector3(cx, cy, -0.02f);
            }

            if (shortcutsPanel != null)
            {
                float cx = actionStripOuterX + menuSide * (gap + shortcutsSize.x * 0.5f);
                // Pannello più alto: centrato verticalmente sulla palette così resta intero.
                shortcutsPanel.transform.localPosition = new Vector3(cx, 0f, -0.022f);
            }
        }

        // Sotto-pannello impostazioni, aperto/chiuso dal bottone "Options" (icona tre
        // puntini nella striscia azioni). Figlio della palette accanto a `panel`, così
        // sopravvive ai Rebuild. Per ora contiene solo il toggle "Left-Handed Mode";
        // è il punto d'estensione per le impostazioni future della palette.
        void BuildOptionsPanel()
        {
            var size = new Vector2(0.26f, 0.225f);
            optionsSize = size;
            // La posizione finale (accanto al bottone ellipsi, sul lato della mano) è data
            // da PositionMenus: qui basta crearlo, ci pensa lei ad ancorarlo.
            optionsPanel = MakeRounded(transform, "OptionsPanel", Vector3.zero,
                size, 0.026f, PanelColor, QueuePanel);
            optionsPanel.AddComponent<FacePlayer>(); // guarda sempre l'utente quando aperto
            AddModalSurfaceCollider(optionsPanel, size); // ray visibile su tutta l'area

            float top = size.y * 0.5f;

            // Header: titolo a sinistra + linea separatrice accent sotto.
            MakeLabel(optionsPanel.transform, "Options",
                new Vector3(-0.006f, top - 0.026f, -0.004f),
                new Vector2(size.x - 0.06f, 0.03f), SliderLabelFont * 1.25f, TextAlignmentOptions.Left);

            MakeRounded(optionsPanel.transform, "HeaderSep",
                new Vector3(0f, top - 0.05f, -0.004f),
                new Vector2(size.x - 0.05f, 0.0035f), 0.0017f, AccentColor, QueueControl);

            // Bottone di chiusura (✕) in alto a destra: serve perché, da modale, il bottone
            // "Options" nella striscia azioni non è più premibile (è "sotto" il menu).
            var closeCell = new Cell
            {
                Center = new Vector3(size.x * 0.5f - 0.026f, top - 0.026f, -0.004f),
                Size = new Vector2(0.034f, 0.034f)
            };
            var close = MakeRoundedButton(optionsPanel.transform, "OptionsClose",
                closeCell.Center, closeCell.Size, 0.01f, ButtonColor, CloseOptionsPanel);
            close.GetComponent<PaletteButton>().SilentPress = true; // il suono lo dà MenuToggle
            MakeIconImage(close.transform, "close", new Vector3(0f, 0f, -0.005f), closeCell.Size.x * 0.6f);

            // Voce: handedness come controllo SEGMENTATO a due segmenti (mano sinistra |
            // mano destra). Esattamente un segmento è attivo: quello della mano corrente è
            // evidenziato (accent); toccare un segmento applica subito quella modalità.
            var handCell = new Cell
            {
                Center = new Vector3(0f, 0.022f, -0.004f),
                Size = new Vector2(size.x - 0.05f, 0.05f)
            };
            const float segGap = 0.006f;
            float segW = (handCell.Size.x - segGap) * 0.5f;
            float segCorner = Mathf.Min(0.014f, handCell.Size.y * 0.4f);
            float segIcon = Mathf.Min(handCell.Size.y * 0.7f, 0.034f);
            float segDX = (segW + segGap) * 0.5f;

            // Segmento sinistro = mancino.
            var leftSeg = MakeRoundedButton(optionsPanel.transform, "HandLeftSeg",
                new Vector3(handCell.Center.x - segDX, handCell.Center.y, handCell.Center.z),
                new Vector2(segW, handCell.Size.y), segCorner, ButtonColor,
                () => StrokeSettings.LeftHanded = true);
            MakeTexQuad(leftSeg.transform, ToolIcon.Get("hand-left"),
                new Vector3(0f, 0f, -0.005f), new Vector2(segIcon, segIcon));
            var leftRend = leftSeg.GetComponent<Renderer>();

            // Segmento destro = destrimani.
            var rightSeg = MakeRoundedButton(optionsPanel.transform, "HandRightSeg",
                new Vector3(handCell.Center.x + segDX, handCell.Center.y, handCell.Center.z),
                new Vector2(segW, handCell.Size.y), segCorner, ButtonColor,
                () => StrokeSettings.LeftHanded = false);
            MakeTexQuad(rightSeg.transform, ToolIcon.Get("hand-right"),
                new Vector3(0f, 0f, -0.005f), new Vector2(segIcon, segIcon));
            var rightRend = rightSeg.GetComponent<Renderer>();

            // Evidenzia esattamente il segmento attivo; aggiorna solo quando la mano cambia.
            bool handInit = false, handLast = false;
            optionsSync.Add(() =>
            {
                bool left = StrokeSettings.LeftHanded;
                if (handInit && left == handLast)
                    return;
                handInit = true;
                handLast = left;
                leftRend.material.SetColor(BaseColorId, left ? AccentColor : ButtonColor);
                rightRend.material.SetColor(BaseColorId, left ? ButtonColor : AccentColor);
            });

            // Voce: "View Shortcuts" (icona saetta). Apre il pannello scorciatoie in sola
            // lettura; resta un modale sopra Options, chiudendolo si torna a Options.
            var scCell = new Cell
            {
                Center = new Vector3(0f, -0.05f, -0.004f),
                Size = new Vector2(size.x - 0.05f, 0.05f)
            };
            var sc = MakeRoundedButton(optionsPanel.transform, "ViewShortcuts",
                scCell.Center, scCell.Size, Mathf.Min(0.014f, scCell.Size.y * 0.4f),
                ButtonColor, OpenShortcutsPanel);
            // Il suono lo dà ShortcutsToggle: niente click di default, sarebbe sovrapposto.
            sc.GetComponent<PaletteButton>().SilentPress = true;
            float scIcon = Mathf.Min(scCell.Size.y * 0.6f, 0.03f);
            float scIconX = -scCell.Size.x * 0.5f + scIcon * 0.5f + 0.012f;
            MakeIconImage(sc.transform, "bolt", new Vector3(scIconX, 0f, -0.005f), scIcon);
            MakeLabel(sc.transform, "View Shortcuts", new Vector3(0f, 0f, -0.004f),
                scCell.Size, ToggleFont, TextAlignmentOptions.Center);

            SetLayerRecursively(optionsPanel, PaletteLayer);
            optionsPanel.SetActive(false);
        }

        // Toggle unico del menu (☰ del controller sinistro e bottone "..." nella palette):
        // - se è aperto QUALCOSA del menu (Options, o le Shortcuts che gli stanno sopra) chiude
        //   TUTTO il menu, lasciando la palette aperta;
        // - altrimenti apre Options e, se la palette era chiusa, la apre con sé (il menu vive
        //   sulla palette). SetActive/ModalRoot li sincronizza AnimateVisibility ogni frame.
        void ToggleOptionsPanel()
        {
            if (optionsOpen || shortcutsOpen)
            {
                optionsOpen = false;
                shortcutsOpen = false;
            }
            else
            {
                optionsOpen = true;
                isOpen = true; // palette chiusa: il menu la apre insieme a sé
            }
            UiFeedback.Instance?.MenuToggle(optionsOpen);
        }

        /// <summary>Apri/chiudi il menu Options da fuori (scorciatoia controller A/X).</summary>
        public void ToggleOptions() => ToggleOptionsPanel();

        void CloseOptionsPanel()
        {
            optionsOpen = false;
            shortcutsOpen = false; // chiude anche un'eventuale Shortcuts sopra Options
            if (optionsPanel != null)
                optionsPanel.SetActive(false);
            ModalRoot = null;
            UiFeedback.Instance?.MenuToggle(false);
        }

        // Apre il pannello scorciatoie (sola lettura). Resta sopra Options: lasciando
        // optionsOpen=true, chiudendo le scorciatoie si ritorna al menu Options.
        void OpenShortcutsPanel()
        {
            shortcutsOpen = true;
            UiFeedback.Instance?.ShortcutsToggle(true);
        }

        void CloseShortcutsPanel()
        {
            shortcutsOpen = false;
            UiFeedback.Instance?.ShortcutsToggle(false);
        }

#if UNITY_EDITOR
        // Costruisce SOLO il pannello scorciatoie e lo restituisce attivo: usato dal tool di
        // anteprima (Tools/Drawing/Preview Shortcuts Panel) per renderizzarlo a PNG senza Play.
        // In Edit mode Awake/Start non girano, quindi istanziare e chiamare i builder è sicuro.
        public GameObject EditorBuildShortcutsPanel()
        {
            BuildShortcutsPanel();
            shortcutsPanel.SetActive(true);
            return shortcutsPanel;
        }
#endif

        // Pannello "Shortcuts" in sola lettura: due controller reali (mano palette / mano
        // pennello) con marker/etichette sui tasti, generati da ControllerShortcuts.All.
        // Solo la X è premibile; il resto è informativo. Vive a parte come optionsPanel.
        void BuildShortcutsPanel()
        {
            // Pannello ampio: le scorciatoie usano un font pari alle altre label e devono
            // riempire tutta l'area utile, quindi serve spazio in larghezza e altezza. La
            // posizione finale (accanto al bottone ellipsi) è data da PositionMenus.
            var size = new Vector2(0.68f, 0.58f);
            shortcutsSize = size;
            shortcutsPanel = MakeRounded(transform, "ShortcutsPanel", Vector3.zero,
                size, 0.026f, PanelColor, QueuePanel);
            shortcutsPanel.AddComponent<FacePlayer>(); // guarda sempre l'utente quando aperto
            AddModalSurfaceCollider(shortcutsPanel, size); // ray visibile su tutta l'area

            float top = size.y * 0.5f;

            // Header: icona saetta + titolo, linea accent, X di chiusura.
            MakeIconImage(shortcutsPanel.transform, "bolt",
                new Vector3(-size.x * 0.5f + 0.03f, top - 0.03f, -0.005f), 0.028f);
            MakeLabel(shortcutsPanel.transform, "Shortcuts",
                new Vector3(-size.x * 0.5f + 0.26f, top - 0.03f, -0.004f),
                new Vector2(0.32f, 0.03f), SliderLabelFont * 1.25f, TextAlignmentOptions.Left);
            MakeRounded(shortcutsPanel.transform, "HeaderSep",
                new Vector3(0f, top - 0.058f, -0.004f),
                new Vector2(size.x - 0.05f, 0.0035f), 0.0017f, AccentColor, QueueControl);
            var closeCell = new Cell
            {
                Center = new Vector3(size.x * 0.5f - 0.03f, top - 0.03f, -0.004f),
                Size = new Vector2(0.036f, 0.036f)
            };
            var close = MakeRoundedButton(shortcutsPanel.transform, "ShortcutsClose",
                closeCell.Center, closeCell.Size, 0.01f, ButtonColor, CloseShortcutsPanel);
            close.GetComponent<PaletteButton>().SilentPress = true; // il suono lo dà ShortcutsToggle
            MakeIconImage(close.transform, "close", new Vector3(0f, 0f, -0.005f), closeCell.Size.x * 0.6f);

            // UN solo schema (line-art con entrambi i controller, importato e schiarito),
            // centrato; attorno le etichette delle scorciatoie con linea-guida e anello sul
            // tasto. Il ruolo (palette/pennello) di ogni controller fisico dipende dalla mano.
            bool leftIsPalette = StrokeSettings.PaletteHand == OVRInput.Controller.LTouch;
            string leftRole = leftIsPalette ? ControllerShortcuts.PaletteHandName : ControllerShortcuts.BrushHandName;
            string rightRole = leftIsPalette ? ControllerShortcuts.BrushHandName : ControllerShortcuts.PaletteHandName;

            BuildSchematicDiagram(leftRole, rightRole, top);

            MakeLabel(shortcutsPanel.transform, "Works with the palette closed.",
                new Vector3(0f, -top + 0.016f, -0.004f),
                new Vector2(size.x - 0.04f, 0.02f), SliderLabelFont * 0.78f, TextAlignmentOptions.Center);

            SetLayerRecursively(shortcutsPanel, PaletteLayer);
            shortcutsPanel.SetActive(false);
        }

        // Ancore dei tasti SULLO SCHEMA (frazioni 0..1 dell'immagine: x→destra, y→giù).
        // Tarate col tool di anteprima (DebugAnchorGrid). Sx: stick/Y/menu; dx: stick/B.
        static readonly Vector2 SchLStick = new(0.078f, 0.323f);
        static readonly Vector2 SchY      = new(0.169f, 0.291f);
        static readonly Vector2 SchLMenu  = new(0.090f, 0.490f);
        static readonly Vector2 SchRStick = new(0.919f, 0.323f);
        static readonly Vector2 SchB      = new(0.838f, 0.291f);

        static readonly Color LeaderColor = new(0.62f, 0.56f, 0.92f, 1f); // linea-guida (accent tenue)

        // Cerca l'azione di un tasto per un ruolo nella tabella unica.
        static string ActionFor(string role, ControllerShortcuts.Btn b)
        {
            foreach (var x in ControllerShortcuts.All)
                if (x.Hand == role && x.Button == b)
                    return x.Action;
            return null;
        }

        // Schema unico (entrambi i controller) centrato + un callout per ogni tasto usato:
        // blocchi "Thumbstick" ai lati esterni, Y/B sopra i rispettivi controller, Menu sotto.
        void BuildSchematicDiagram(string leftRole, string rightRole, float top)
        {
            const float diagW = 0.34f;
            float diagH = diagW * 371f / 800f; // proporzioni dello schema
            const float cy = -0.01f;
            var tex = Resources.Load<Texture2D>("Controllers/schematic");
            if (tex != null)
                MakeTexQuad(shortcutsPanel.transform, tex, new Vector3(0f, cy, -0.004f), new Vector2(diagW, diagH));

            Vector3 P(Vector2 f) => new((f.x - 0.5f) * diagW, cy + (0.5f - f.y) * diagH, -0.0065f);

            if (DebugAnchorGrid)
                for (float gx = 0f; gx <= 1.001f; gx += 0.1f)
                for (float gy = 0f; gy <= 1.001f; gy += 0.1f)
                {
                    bool axis = Mathf.Abs(gx - 0.5f) < 0.01f || Mathf.Abs(gy - 0.5f) < 0.01f;
                    MakeRounded(shortcutsPanel.transform, "Grid", P(new Vector2(gx, gy)) + new Vector3(0f, 0f, 0.001f),
                        new Vector2(0.004f, 0.004f), 0.002f, axis ? Color.cyan : new Color(1f, 1f, 1f, 0.5f), QueueIcon);
                }

            string acc = ColorUtility.ToHtmlStringRGB(AccentColor);
            string dim = "#9AA0B5";

            // Etichetta sopra/sotto un tasto, leader verticale + anello.
            void TagV(string text, Vector3 anchor, float ly)
            {
                float endY = ly + (ly > anchor.y ? -0.011f : 0.011f);
                Leader(anchor, new Vector3(anchor.x, endY, -0.0052f));
                AnchorDot(anchor, 0.009f);
                MakeLabel(shortcutsPanel.transform, text, new Vector3(anchor.x, ly, -0.005f),
                    new Vector2(0.24f, 0.022f), SliderLabelFont * 0.85f, TextAlignmentOptions.Center);
            }

            // Blocchi Thumbstick ai lati esterni.
            StickBlock(leftRole, acc, dim, P(SchLStick), -diagW * 0.5f - 0.015f, false);
            StickBlock(rightRole, acc, dim, P(SchRStick), diagW * 0.5f + 0.015f, true);

            float imgTop = cy + diagH * 0.5f, imgBot = cy - diagH * 0.5f;

            // Y (sinistro) = Save, B (destro) = Delete: appena SOPRA lo schema.
            string yA = ActionFor(leftRole, ControllerShortcuts.Btn.FaceB);
            if (yA != null) TagV($"<b><color=#{acc}>Y</color></b> {yA}", P(SchY), imgTop + 0.05f);
            string bA = ActionFor(rightRole, ControllerShortcuts.Btn.FaceB);
            if (bA != null) TagV($"<b><color=#{acc}>B</color></b> {bA}", P(SchB), imgTop + 0.05f);

            // Menu (Options): tasto fisico del controller sinistro, appena SOTTO lo schema.
            string mA = ActionFor(ControllerShortcuts.PaletteHandName, ControllerShortcuts.Btn.Menu);
            if (mA != null) TagV($"<b><color=#{acc}>Menu</color></b> {mA}", P(SchLMenu), imgBot - 0.05f);
        }

        // Blocco testo "Thumbstick" (click + 4 direzioni) a lato dello schema, con leader allo stick.
        void StickBlock(string role, string acc, string dim, Vector3 stickAnchor, float edgeX, bool toRight)
        {
            string click = ActionFor(role, ControllerShortcuts.Btn.StickClick);
            string up = ActionFor(role, ControllerShortcuts.Btn.StickUp);
            string down = ActionFor(role, ControllerShortcuts.Btn.StickDown);
            string lf = ActionFor(role, ControllerShortcuts.Btn.StickLeft);
            string rt = ActionFor(role, ControllerShortcuts.Btn.StickRight);
            string text =
                $"<b><color=#{acc}>Thumbstick</color></b>\n" +
                $"<color={dim}>Click</color> {click}\n" +
                $"<color={dim}>Up</color> {up}\n<color={dim}>Down</color> {down}\n" +
                $"<color={dim}>Left</color> {lf}\n<color={dim}>Right</color> {rt}";
            const float w = 0.15f;
            var align = toRight ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
            float cx = toRight ? edgeX + w * 0.5f : edgeX - w * 0.5f;
            MakeLabel(shortcutsPanel.transform, text, new Vector3(cx, 0f, -0.005f),
                new Vector2(w, 0.13f), SliderLabelFont * 0.8f, align);
            Leader(stickAnchor, new Vector3(edgeX, 0.01f, -0.0052f));
            AnchorDot(stickAnchor, 0.009f);
        }


        // Anello accent sul tasto: disco accent + disco interno scuro (≈ colore del tasto),
        // così evidenzia il tasto senza coprirlo.
        void AnchorDot(Vector3 pos, float d)
        {
            MakeRounded(shortcutsPanel.transform, "Mark", pos, new Vector2(d, d), d * 0.5f, AccentColor, QueueIcon);
            float inner = d * 0.52f;
            var p = pos; p.z = -0.0068f;
            MakeRounded(shortcutsPanel.transform, "MarkInner", p, new Vector2(inner, inner), inner * 0.5f,
                new Color(0.13f, 0.13f, 0.16f, 1f), QueueIcon);
        }

        // Linea-guida sottile tra due punti (barra ruotata).
        void Leader(Vector3 from, Vector3 to)
        {
            Vector3 mid = (from + to) * 0.5f;
            mid.z = -0.0052f;
            float len = Vector2.Distance(new Vector2(from.x, from.y), new Vector2(to.x, to.y));
            var bar = MakeRounded(shortcutsPanel.transform, "Leader", mid,
                new Vector2(len, 0.0018f), 0.0009f, LeaderColor, QueueControl);
            float ang = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
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
            // Bottone più grande per ospitare label (sopra) + icona del tratto (sotto).
            const float bSize = 0.058f, bGap = 0.012f;
            const float BrushLabelFont = 0.10f; // piccola, non invadente
            int n = BrushOrder.Length;

            // TEMP: pennelli disattivati su richiesta (vedi modifiche-temporanee.md). Restano
            // nelle array brushButtons/brushPreviewMats (indici per tipo intatti → SyncSelection
            // ok) ma sono nascosti e NON occupano uno slot nella striscia: i visibili si
            // ricompattano. Per riabilitare un pennello, toglilo da questo set.
            var disabled = new System.Collections.Generic.HashSet<BrushType> { BrushType.Glow };

            int visibleCount = 0;
            for (int i = 0; i < n; i++)
                if (!disabled.Contains(BrushOrder[i]))
                    visibleCount++;

            float contentH = visibleCount * bSize + (visibleCount - 1) * bGap;
            var stripSize = new Vector2(bSize + 0.014f, contentH + 0.014f);
            // Striscia pennelli sul lato della mano che disegna: destra per i destri
            // (X negativa col pannello ruotato di 180°), sinistra per i mancini.
            float side = StrokeSettings.LeftHanded ? 1f : -1f;
            float stripX = side * (panelSize.x * 0.5f + 0.012f + stripSize.x * 0.5f);

            var strip = MakeRounded(panel.transform, "BrushStrip", new Vector3(stripX, 0f, 0f),
                stripSize, 0.016f, PanelColor, QueuePanel);

            brushButtons = new Renderer[n];
            brushPreviewMats = new Material[n];
            float y0 = (contentH - bSize) * 0.5f;
            int slot = 0; // posizione visibile (avanza solo per i pennelli attivi)
            for (int i = 0; i < n; i++)
            {
                var type = BrushOrder[i];
                bool off = disabled.Contains(type);
                // Ordine naturale: dall'alto verso il basso stroke, ribbon, dashed.
                float y = y0 - slot * (bSize + bGap);
                var b = MakeRoundedButton(strip.transform, type.ToString(), new Vector3(0f, y, -0.004f),
                    new Vector2(bSize, bSize), 0.013f, ButtonColor, () => StrokeSettings.Type = type);
                // Label in alto, dentro il bottone.
                MakeLabel(b.transform, BrushLabel(type), new Vector3(0f, bSize * 0.30f, -0.004f),
                    new Vector2(bSize * 0.92f, bSize * 0.30f), BrushLabelFont, TextAlignmentOptions.Center);
                // Icona (anteprima del tratto) sotto la label.
                brushPreviewMats[i] = MakeTexQuad(b.transform, BrushPreview.Get(type),
                    new Vector3(0f, -bSize * 0.13f, -0.004f), new Vector2(bSize * 0.78f, bSize * 0.40f));
                brushButtons[i] = b.GetComponent<Renderer>();
                if (off)
                    b.SetActive(false); // nascosto e non consuma uno slot visibile
                else
                    slot++;
            }
        }

        // Etichetta leggibile del tipo di pennello (Round → "stroke").
        static string BrushLabel(BrushType type) => type switch
        {
            BrushType.Round => "stroke",
            BrushType.Ribbon => "ribbon",
            BrushType.Dashed => "dashed",
            BrushType.Glow => "glow",
            _ => type.ToString().ToLower()
        };

        // Striscia verticale Redo/Undo/Save/Load/Delete all, ancorata a destra del pannello.
        // Stessa dimensione dei bottoni pennello a sinistra: label piccola sopra + icona sotto.
        void BuildActionStrip(Vector2 panelSize)
        {
            var actions = new (string name, string label, System.Action action)[]
            {
                ("redo",  "undo",       StrokeHistory.Redo),
                ("undo",  "redo",       StrokeHistory.Undo),
                ("save",  "save",       DrawingStore.Save),
                ("load",  "load",       DrawingStore.Load),
                ("clear", "delete all", DrawingStore.NewScene),
                ("options", "Options",  ToggleOptionsPanel), // apre il menu impostazioni
            };

            const float bSize = 0.058f, bGap = 0.012f;
            const float ActionLabelFont = 0.10f;

            int n = actions.Length;
            float contentH = n * bSize + (n - 1) * bGap;
            var stripSize = new Vector2(bSize + 0.014f, contentH + 0.014f);
            // Striscia azioni sul lato opposto a quella dei pennelli (mano della palette).
            float side = StrokeSettings.LeftHanded ? -1f : 1f;
            float stripX = side * (panelSize.x * 0.5f + 0.012f + stripSize.x * 0.5f);

            // Geometria per ancorare i pop-up accanto al bottone ellipsi (vedi PositionMenus).
            menuSide = side;
            actionStripOuterX = stripX + side * stripSize.x * 0.5f;

            var strip = MakeRounded(panel.transform, "ActionStrip", new Vector3(stripX, 0f, 0f),
                stripSize, 0.016f, PanelColor, QueuePanel);

            float y0 = (contentH - bSize) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float y = y0 - i * (bSize + bGap);

                var b = MakeRoundedButton(strip.transform, actions[i].name,
                    new Vector3(0f, y, -0.004f),
                    new Vector2(bSize, bSize),
                    0.013f,
                    ButtonColor,
                    actions[i].action);

                // Il bottone ellipsi (...) = ancora dei pop-up e toggle sempre premibile.
                if (actions[i].name == "options")
                {
                    optionsButtonY = y;
                    OptionsToggleButton = b;
                    // Il suono lo dà MenuToggle: niente click di default (evita sovrapposizione).
                    b.GetComponent<PaletteButton>().SilentPress = true;
                }

                // Label piccola in alto, come nella striscia pennelli a sinistra.
                MakeLabel(b.transform, actions[i].label,
                    new Vector3(0f, bSize * 0.30f, -0.004f),
                    new Vector2(bSize * 0.92f, bSize * 0.30f),
                    ActionLabelFont,
                    TextAlignmentOptions.Center);

                // Icona sotto la label.
                MakeIconImage(b.transform, actions[i].name,
                    new Vector3(0f, -bSize * 0.13f, -0.004f),
                    bSize * 0.58f);
            }
        }

        Renderer MakeToolButton(string label, string iconName, Cell cell, System.Action action)
        {
            var b = MakeRoundedButton(panel.transform, label, cell.Center, cell.Size,
                Mathf.Min(0.008f, cell.Size.y * 0.3f), ButtonColor, action);

            // Scritta piccola in alto a sinistra
            MakeLabel(
                b.transform,
                label,
                new Vector3(-cell.Size.x * 0.05f, cell.Size.y * 0.30f, -0.004f),
                new Vector2(cell.Size.x * 0.70f, cell.Size.y * 0.25f),
                ToolSmallLabelFont,
                TextAlignmentOptions.TopLeft
            );

            // Icona grande al centro
            MakeIconImage(
                b.transform,
                iconName,
                new Vector3(0f, -cell.Size.y * 0.08f, -0.004f),
                Mathf.Min(cell.Size.x, cell.Size.y) * 0.62f
            );

            return b.GetComponent<Renderer>();
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
        // I toggle del pannello principale usano panel.transform + toggleSync (svuotato dal
        // Rebuild); il sotto-pannello Options passa il proprio parent e la propria lista.
        void MakeToggleButton(string label, Cell cell, System.Func<bool> isOn, System.Action onToggle)
            => MakeToggleButton(label, cell, isOn, onToggle, panel.transform, toggleSync);

        void MakeToggleButton(string label, Cell cell, System.Func<bool> isOn, System.Action onToggle,
            Transform parent, List<System.Action> sync)
        {
            var b = MakeRoundedButton(parent, label + "Toggle", cell.Center, cell.Size,
                Mathf.Min(0.012f, cell.Size.y * 0.4f), ButtonColor, onToggle);
            // È un toggle: il feedback userà il suono on/off in base al nuovo stato.
            b.GetComponent<PaletteButton>().ToggleState = isOn;
            MakeLabel(b.transform, label, new Vector3(0f, 0f, -0.004f), cell.Size, ToggleFont, TextAlignmentOptions.Center);
            var r = b.GetComponent<Renderer>();
            // Aggiorna il colore solo quando lo stato del toggle cambia davvero.
            bool initialized = false;
            bool last = false;
            sync.Add(() =>
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

        // Collider "di superficie" sul fondo di un pannello modale (Options/Shortcuts): non è
        // un controllo (niente PaletteButton), serve solo a far sì che il PaletteRay veda il
        // pannello e tenga il raggio visibile su tutta la sua area, anche dove non ci sono
        // pulsanti. Sottile e dietro ai pulsanti, che restano i primi bersagli del ray.
        static void AddModalSurfaceCollider(GameObject panel, Vector2 size)
        {
            var col = panel.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(size.x, size.y, 0.006f);
        }

        // Etichetta TextMeshPro a dimensione fissa (niente auto-size che gonfia il testo).
        // Restituisce il TMP così i chiamanti che lo desiderano possono aggiornare il testo.
        TextMeshPro MakeLabel(Transform parent, string text, Vector3 localPos, Vector2 box, float fontSize, TextAlignmentOptions align)
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
            return tmp;
        }
    }
}
