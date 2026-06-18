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
        [Tooltip("Sul visore: quanto sopra la mano fluttua il pannello.")]
        public float heightAboveHand = 0.15f;

#if UNITY_EDITOR
        [SerializeField] bool pinPaletteInEditor = true;
        [SerializeField] float editorDistance = 0.72f;
        [SerializeField] Vector3 editorOffset = new(0.0f, 0f, 0f);
        [SerializeField] float editorScale = 1.25f;
#endif

        public BrushController Brush { get; set; }
        public Transform HandAnchor { get; set; }

        static readonly Color PanelColor = new(0.10f, 0.10f, 0.12f, 0.96f);
        static readonly Color ButtonColor = new(0.22f, 0.22f, 0.27f, 1f);
        static readonly Color AccentColor = new(0.55f, 0.45f, 0.95f, 1f);
        static readonly Color TrackColor = new(0.32f, 0.32f, 0.38f, 1f);
        static readonly Color EmptySwatch = new(1f, 1f, 1f, 0.15f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Ordini di disegno (tutti i materiali UI sono trasparenti con ZWrite off:
        // serve un render queue esplicito perché lo sfondo non copra i controlli).
        const int QueuePanel = 3000;   // sfondo pannello/striscia
        const int QueueControl = 3002; // bottoni, swatch, ruota, slider
        const int QueueIcon = 3003;    // icone/anteprime
        const int QueueText = 3004;    // etichette TMP

        // Dimensioni font (point-size TMP in unità locali metriche: ~0.003 m per unità).
        const float SectionFont = 0.175f;
        const float ButtonFont = 0.20f;   // Draw/Fill/Erase (riga larga)
        const float ActionFont = 0.16f;   // Undo/Redo/Save/Load (riga stretta)
        const float ToggleFont = 0.24f;

        // anteprime tratto per i 4 pennelli, in ordine BrushType (Round/Ribbon/Glow/Dashed)
        static readonly BrushType[] BrushOrder = { BrushType.Round, BrushType.Ribbon, BrushType.Glow, BrushType.Dashed };

        GameObject panel;
        Renderer penButton, fillButton, eraserButton;
        Renderer[] brushButtons;
        Renderer[] recentSwatches;
        readonly List<System.Action> toggleSync = new();

        // ---- apertura/chiusura animata col tasto X ----
        bool isOpen = true;
        float visibility = 1f;
        const float AnimDuration = 0.16f;

        void Start()
        {
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.Euler(localEuler);
            BuildPanel();
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
            var head = Camera.main;
            if (head == null)
                return;
            transform.SetParent(null, true);
            transform.position = head.transform.position
                + head.transform.forward * editorDistance
                + head.transform.right * editorOffset.x
                + head.transform.up * editorOffset.y;
            transform.rotation = Quaternion.LookRotation(transform.position - head.transform.position, Vector3.up);
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
                StartCoroutine(HapticPulse(0.04f));
            }

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

        IEnumerator HapticPulse(float duration)
        {
            OVRInput.SetControllerVibration(0.4f, 0.5f, StrokeSettings.PaletteHand);
            yield return new WaitForSeconds(duration);
            OVRInput.SetControllerVibration(0f, 0f, StrokeSettings.PaletteHand);
        }

        void LateUpdate()
        {
#if UNITY_EDITOR
            if (pinPaletteInEditor)
                return;
#endif
            if (!UnityEngine.XR.XRSettings.isDeviceActive || HandAnchor == null)
                return;
            var head = Camera.main;
            if (head == null)
                return;
            var position = HandAnchor.position + Vector3.up * heightAboveHand;
            transform.position = position;
            var toPanel = position - head.transform.position;
            if (toPanel.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
        }

        void SyncSelection()
        {
            if (penButton == null)
                return;
            penButton.material.SetColor(BaseColorId, StrokeSettings.Tool == ToolMode.Pen ? AccentColor : ButtonColor);
            fillButton.material.SetColor(BaseColorId, StrokeSettings.Tool == ToolMode.Fill ? AccentColor : ButtonColor);
            eraserButton.material.SetColor(BaseColorId, StrokeSettings.Tool == ToolMode.Eraser ? AccentColor : ButtonColor);
            for (int i = 0; i < brushButtons.Length; i++)
                brushButtons[i].material.SetColor(BaseColorId, (int)StrokeSettings.Type == i ? AccentColor : ButtonColor);
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

            var panelSize = new Vector2(0.34f, 0.34f);
            const float pad = 0.018f, rowGap = 0.012f, colGap = 0.012f, z = -0.002f;

            MakeRounded(panel.transform, "MainPanel", Vector3.zero, panelSize, 0.020f, PanelColor, QueuePanel);

            BuildBrushStrip(panelSize);

            var layout = new PaletteLayout(panelSize, pad, rowGap, colGap, z);

            // ----- riga colore: ruota + luminosità (sx) | Pressure/Mirror impilati (dx) -----
            var colorRow = layout.Row(0.10f);
            var wheelCell = colorRow.Left(0.10f);
            var brightCell = colorRow.Left(0.016f);
            var toggleRegion = colorRow.Fill();

            var wheel = new GameObject("ColorWheel");
            wheel.transform.SetParent(panel.transform, false);
            wheel.transform.localPosition = wheelCell.Center;
            var colorWheel = wheel.AddComponent<ColorWheel>();
            colorWheel.Build(Mathf.Min(wheelCell.Size.x, wheelCell.Size.y));
            colorWheel.SetProximityTarget(Brush != null ? Brush.Tip : null);
            SetQueue(wheel, QueueControl);

            var bright = new GameObject("BrightnessSlider");
            bright.transform.SetParent(panel.transform, false);
            bright.transform.localPosition = brightCell.Center;
            bright.AddComponent<BrightnessSlider>().Build(new Vector2(0.018f, brightCell.Size.y * 0.92f));
            SetQueue(bright, QueueControl);

            var toggles = toggleRegion.StackV(2, 0.014f);
            MakeToggleButton("Pressure", toggles[0],
                () => StrokeSettings.SizeMode == SizeMode.PressureBrush,
                () => StrokeSettings.SizeMode = StrokeSettings.SizeMode == SizeMode.PressureBrush
                    ? SizeMode.FixedPen : SizeMode.PressureBrush);
            MakeToggleButton("Mirror", toggles[1],
                () => Mirror.Enabled,
                () => Mirror.Toggle(Camera.main != null ? Camera.main.transform : transform));

            // ----- colori recenti -----
            var recentRow = layout.Row(0.030f);
            var recentLabel = recentRow.Left(0.058f);
            MakeLabel(panel.transform, "recenti", recentLabel.Center, recentLabel.Size, SectionFont, TextAlignmentOptions.Left);
            recentSwatches = new Renderer[5];
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var c = recentRow.Left(recentRow.Height);
                var sw = MakeRoundedButton(panel.transform, $"Recent{i}", c.Center, c.Size, 0.006f, EmptySwatch,
                    () =>
                    {
                        var r = StrokeSettings.RecentColors;
                        if (idx < r.Count) StrokeSettings.SetColor(r[idx]);
                    });
                recentSwatches[i] = sw.GetComponent<Renderer>();
            }

            // ----- trasparenza (slider) -----
            var alphaRow = layout.Row(0.022f);
            var alphaLabel = alphaRow.Left(0.105f);
            MakeLabel(panel.transform, "transparency", alphaLabel.Center, alphaLabel.Size, SectionFont, TextAlignmentOptions.Left);
            var alphaCell = alphaRow.Fill();
            var alpha = new GameObject("AlphaSlider");
            alpha.transform.SetParent(panel.transform, false);
            alpha.transform.localPosition = alphaCell.Center;
            alpha.AddComponent<AlphaSlider>().Build(new Vector2(alphaCell.Size.x, 0.012f));
            SetQueue(alpha, QueueControl);

            // ----- strumenti Draw / Fill / Erase -----
            var toolsRow = layout.Row(0.034f);
            var toolCells = toolsRow.Split(3);
            penButton = MakeTextButton("Draw", "pencil", toolCells[0], () => StrokeSettings.Tool = ToolMode.Pen, ButtonFont);
            fillButton = MakeTextButton("Fill", "droplet", toolCells[1], () => StrokeSettings.Tool = ToolMode.Fill, ButtonFont);
            eraserButton = MakeTextButton("Erase", "eraser", toolCells[2], () => StrokeSettings.Tool = ToolMode.Eraser, ButtonFont);

            // ----- spessore (slider) -----
            var sizeRow = layout.Row(0.022f);
            var sizeLabel = sizeRow.Left(0.045f);
            MakeLabel(panel.transform, "size", sizeLabel.Center, sizeLabel.Size, SectionFont, TextAlignmentOptions.Left);
            var sizeCell = sizeRow.Fill();
            var size = new GameObject("SizeSlider");
            size.transform.SetParent(panel.transform, false);
            size.transform.localPosition = sizeCell.Center;
            size.AddComponent<SizeSlider>().Build(new Vector2(sizeCell.Size.x, 0.012f));
            SetQueue(size, QueueControl);

            // ----- Undo / Redo / Save / Load -----
            var actionRow = layout.Row(0.030f);
            var actionCells = actionRow.Split(4);
            MakeTextButton("Undo", "undo", actionCells[0], StrokeHistory.Undo, ActionFont);
            MakeTextButton("Redo", "redo", actionCells[1], StrokeHistory.Redo, ActionFont);
            MakeTextButton("Save", "save", actionCells[2], DrawingStore.Save, ActionFont);
            MakeTextButton("Load", "load", actionCells[3], DrawingStore.Load, ActionFont);
        }

        // Striscia verticale dei 4 tipi di pennello, ancorata a destra del pannello
        // (lato della mano che disegna). Ogni bottone mostra un'anteprima del tratto.
        void BuildBrushStrip(Vector2 panelSize)
        {
            const float bSize = 0.048f, bGap = 0.012f;
            int n = BrushOrder.Length;
            float contentH = n * bSize + (n - 1) * bGap;
            var stripSize = new Vector2(bSize + 0.014f, contentH + 0.014f);
            float stripX = panelSize.x * 0.5f + 0.012f + stripSize.x * 0.5f;

            var strip = MakeRounded(panel.transform, "BrushStrip", new Vector3(stripX, 0f, 0f),
                stripSize, 0.016f, PanelColor, QueuePanel);

            brushButtons = new Renderer[n];
            float y0 = (contentH - bSize) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var type = BrushOrder[i];
                float y = y0 - i * (bSize + bGap);
                var b = MakeRoundedButton(strip.transform, type.ToString(), new Vector3(0f, y, -0.004f),
                    new Vector2(bSize, bSize), 0.011f, ButtonColor, () => StrokeSettings.Type = type);
                MakeTexQuad(b.transform, BrushPreview.Get(type), new Vector3(0f, 0f, -0.004f),
                    new Vector2(bSize * 0.74f, bSize * 0.44f));
                brushButtons[i] = b.GetComponent<Renderer>();
            }
        }

        // Pulsante con icona a sinistra + testo a destra, senza sovrapposizioni
        // (Draw/Fill/Erase, Undo/Redo/Save/Load).
        Renderer MakeTextButton(string label, string iconName, Cell cell, System.Action action, float fontSize)
        {
            var b = MakeRoundedButton(panel.transform, label, cell.Center, cell.Size,
                Mathf.Min(0.008f, cell.Size.y * 0.3f), ButtonColor, action);

            float w = cell.Size.x;
            float iconSize = Mathf.Min(cell.Size.y * 0.62f, 0.018f);
            float iconX = -w * 0.5f + iconSize * 0.5f + 0.006f;
            MakeIconImage(b.transform, iconName, new Vector3(iconX, 0f, -0.004f), iconSize);

            // Testo centrato nel pulsante (allineato col bottone), con un margine a
            // sinistra che lascia spazio all'icona.
            float leftMargin = iconSize + 0.010f;
            MakeLabel(b.transform, label, new Vector3(leftMargin * 0.5f, 0f, -0.004f),
                new Vector2(w - leftMargin - 0.008f, cell.Size.y), fontSize, TextAlignmentOptions.Center);
            return b.GetComponent<Renderer>();
        }

        // Toggle compatto: bottone che si illumina (accent) quando attivo. Niente pillola.
        void MakeToggleButton(string label, Cell cell, System.Func<bool> isOn, System.Action onToggle)
        {
            var b = MakeRoundedButton(panel.transform, label + "Toggle", cell.Center, cell.Size,
                Mathf.Min(0.012f, cell.Size.y * 0.4f), ButtonColor, onToggle);
            MakeLabel(b.transform, label, new Vector3(0f, 0f, -0.004f), cell.Size, ToggleFont, TextAlignmentOptions.Center);
            var r = b.GetComponent<Renderer>();
            toggleSync.Add(() => r.material.SetColor(BaseColorId, isOn() ? AccentColor : ButtonColor));
        }

        // Porta tutti i renderer di un sotto-albero (ruota/slider e relativi pomelli)
        // sopra lo sfondo del pannello, per evitare il sorting trasparente instabile.
        static void SetQueue(GameObject root, int queue)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.material.renderQueue = queue;
        }

        // Quad con texture (anteprima tratto, ruota, ecc.) su materiale URP/Unlit.
        void MakeTexQuad(Transform parent, Texture2D tex, Vector3 localPos, Vector2 size)
        {
            var go = new GameObject("Preview");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var mat = BrushMaterials.CreateUnlit(Color.white);
            mat.SetTexture("_BaseMap", tex);
            mat.renderQueue = QueueIcon;
            go.AddComponent<MeshRenderer>().material = mat;
        }

        // Icona procedurale (ToolIcon) su quad URP/Unlit. Glifi bianchi coerenti,
        // nitidi a ogni dimensione, senza problemi di import/sfondo dei PNG.
        void MakeIconImage(Transform parent, string name, Vector3 localPos, float size)
        {
            MakeTexQuad(parent, ToolIcon.Get(name), localPos, new Vector2(size, size));
        }

        GameObject MakeRounded(Transform parent, string name, Vector3 localPos, Vector2 size, float corner, Color color, int queue = QueueControl)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(size.x, size.y, corner);
            var mat = BrushMaterials.CreateUnlit(color);
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
            tmp.fontMaterial.renderQueue = QueueText;
        }
    }
}
