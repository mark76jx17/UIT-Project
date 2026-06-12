using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MixedRealityProject
{
    public enum PaletteTool { Brush, Eraser, Select }

    /// <summary>
    /// Unica fonte di verità sullo stato della palette: strumento attivo,
    /// colore selezionato, dimensione pennello. Il sistema di disegno si
    /// abbona agli eventi senza conoscere i controlli UI.
    /// Cablaggio incrementale: 4a strumenti, 4b colori, 4c slider.
    /// </summary>
    public class PaletteState : MonoBehaviour
    {
        [Header("Strumenti")]
        [SerializeField] Toggle brushToggle;
        [SerializeField] Toggle eraserToggle;
        [SerializeField] Toggle selectToggle;

        [Header("Sezione colori")]
        [Tooltip("CanvasGroup su ColorsGrid: attenuato quando lo strumento non è il pennello.")]
        [SerializeField] CanvasGroup colorsGroup;
        [SerializeField, Range(0f, 1f)] float colorsDimmedAlpha = 0.4f;

        [Header("Colori")]
        [Tooltip("Gli 8 swatch della griglia: il colore attivo è quello dell'Image del toggle selezionato.")]
        [SerializeField] List<Toggle> swatchToggles;

        [Tooltip("Toggle della cella 'colore personalizzato'.")]
        [SerializeField] Toggle customColorToggle;

        [SerializeField] FlexibleColorPicker colorPicker;

        [Tooltip("Overlay sulla cella custom: mostra il colore scelto nel picker (parte disattivato, arcobaleno visibile).")]
        [SerializeField] Image customColorFill;

        [Header("Dimensione pennello")]
        [SerializeField] Slider sizeSlider;

        [Tooltip("Etichetta TextRight dello slider: mostra la percentuale corrente.")]
        [SerializeField] TMP_Text sizeLabel;

        public PaletteTool Tool { get; private set; } = PaletteTool.Brush;
        public Color Color { get; private set; } = UnityEngine.Color.white;
        public float BrushSize { get; private set; } = 0.5f;

        public event Action<PaletteTool> ToolChanged;
        public event Action<Color> ColorChanged;
        public event Action<float> BrushSizeChanged;

        void Awake()
        {
            brushToggle.onValueChanged.AddListener(on => { if (on) SetTool(PaletteTool.Brush); });
            eraserToggle.onValueChanged.AddListener(on => { if (on) SetTool(PaletteTool.Eraser); });
            selectToggle.onValueChanged.AddListener(on => { if (on) SetTool(PaletteTool.Select); });
            ApplyColorsDim();

            foreach (Toggle swatch in swatchToggles)
            {
                Image swatchImage = swatch.GetComponent<Image>();
                swatch.onValueChanged.AddListener(on => { if (on) SetColor(swatchImage.color); });
            }
            customColorToggle.onValueChanged.AddListener(on => { if (on) SetColor(colorPicker.color); });
            // Il picker emette anche all'apertura (colore iniziale): da quel momento
            // la cella custom mostra il colore corrente al posto dell'arcobaleno.
            colorPicker.onColorChange.AddListener(OnPickerColorChanged);

            sizeSlider.onValueChanged.AddListener(SetBrushSize);
            SetBrushSize(sizeSlider.value); // allinea etichetta e stato al valore in scena
        }

        void SetBrushSize(float value)
        {
            sizeLabel.text = Mathf.RoundToInt(value * 100f) + "%";
            if (Mathf.Approximately(value, BrushSize))
            {
                return;
            }
            BrushSize = value;
            BrushSizeChanged?.Invoke(value);
        }

        void OnPickerColorChanged(Color picked)
        {
            customColorFill.gameObject.SetActive(true);
            customColorFill.color = picked;
            if (customColorToggle.isOn)
            {
                SetColor(picked);
            }
        }

        void SetColor(Color value)
        {
            if (value == Color)
            {
                return;
            }
            Color = value;
            ColorChanged?.Invoke(value);
        }

        void SetTool(PaletteTool tool)
        {
            if (tool == Tool)
            {
                return;
            }
            Tool = tool;
            ApplyColorsDim();
            ToolChanged?.Invoke(tool);
        }

        // La sezione colori resta visibile ma spenta con gomma/selezione:
        // la selezione del colore non deve andare persa al cambio strumento.
        void ApplyColorsDim()
        {
            bool active = Tool == PaletteTool.Brush;
            colorsGroup.alpha = active ? 1f : colorsDimmedAlpha;
            colorsGroup.interactable = active;
            colorsGroup.blocksRaycasts = active;
        }
    }
}
