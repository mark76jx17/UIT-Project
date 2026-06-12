using UnityEngine;

namespace MixedRealityProject
{
    /// <summary>
    /// Primo consumer di PaletteState: una pallina sulla punta del controller
    /// destro che mostra il colore attivo della palette. Visibile solo quando
    /// la palette è chiusa (non deve intralciare l'interazione col pannello).
    /// Non sa nulla dei controlli UI: legge solo PaletteState.
    /// </summary>
    public class BrushTip : MonoBehaviour
    {
        [SerializeField] PaletteState palette;

        [Tooltip("PaletteCanvas: quando è attivo (palette aperta) la pallina si nasconde.")]
        [SerializeField] GameObject paletteContent;

        [Tooltip("Renderer della sfera-anteprima (figlia, viene attivata/disattivata).")]
        [SerializeField] Renderer tipRenderer;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock block;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            palette.ColorChanged += ApplyColor;
        }

        void Start()
        {
            ApplyColor(palette.Color);
        }

        void OnDestroy()
        {
            palette.ColorChanged -= ApplyColor;
        }

        void Update()
        {
            bool show = !paletteContent.activeInHierarchy;
            if (tipRenderer.gameObject.activeSelf != show)
            {
                tipRenderer.gameObject.SetActive(show);
            }
        }

        // MaterialPropertyBlock: niente istanze di material per il cambio colore.
        void ApplyColor(Color color)
        {
            block.SetColor(BaseColorId, color);
            tipRenderer.SetPropertyBlock(block);
        }
    }
}
