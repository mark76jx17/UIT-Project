using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Anteprima del tratto: un rettangolo arrotondato che assume in tempo reale
    /// <b>colore</b>, <b>opacità</b> e <b>dimensione</b> correnti del pennello, così
    /// l'utente capisce a colpo d'occhio con cosa sta per disegnare. Sta direttamente
    /// sopra lo sfondo classico del pannello: la trasparenza del colore si legge come
    /// trasparenza sul colore del pannello. Il rettangolo cresce/rimpicciolisce con lo
    /// slider della dimensione.
    /// </summary>
    public class ColorPreview : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // In Cancella/Elimina il colore non c'entra: l'anteprima diventa un grigio
        // trasparente con l'icona dello strumento (gomma o ✕, le stesse dei bottoni).
        // Il grigio è condiviso con lo slider Size (fonte unica in StrokeSettings).

        Transform swatch;
        Material swatchMat;
        float minSize, maxSize;
        Transform border;
        float borderPadding;
        GameObject icon;
        Material iconMat;
        ToolMode lastIconTool = (ToolMode)(-1);

        public void Build(Vector2 box)
        {
            // Bordo esterno: resta visibile anche quando il colore scelto è nero.
            // Uso lo stesso lilla/accent della palette selezionata.
            var bd = new GameObject("SwatchBorder");
            bd.transform.SetParent(transform, false);
            bd.transform.localPosition = new Vector3(0f, 0f, -0.0045f);
            bd.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(1f, 1f, 0.25f);
            bd.AddComponent<MeshRenderer>().material =
                BrushMaterials.CreateUnlit(new Color(0.55f, 0.45f, 0.95f, 1f), opaque: true);
            border = bd.transform;

            // Rettangolo del colore: leggermente più piccolo del bordo.
            var sw = new GameObject("Swatch");
            sw.transform.SetParent(transform, false);
            sw.transform.localPosition = new Vector3(0f, 0f, -0.005f);
            sw.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(1f, 1f, 0.25f);
            swatchMat = BrushMaterials.CreateUnlit(Color.white);
            // Lo swatch è trasparente (per l'anteprima dell'opacità e il grigio-gomma 0.40):
            // senza questo scriverebbe il proprio alpha nel framebuffer e "bucherebbe"
            // l'occlusione del passthrough (see-through), anche se la bordatura opaca dietro
            // lo copre. PreserveDestAlpha lascia intatto l'alpha=1 del bordo sotto.
            BrushMaterials.PreserveDestAlpha(swatchMat);
            sw.AddComponent<MeshRenderer>().material = swatchMat;
            swatch = sw.transform;

            maxSize = Mathf.Min(box.x, box.y) * 0.88f;
            minSize = maxSize * 0.22f;
            borderPadding = maxSize * 0.08f;

            // Icona dello strumento (gomma/✕), mostrata solo in Cancella/Elimina.
            icon = new GameObject("ToolIcon");
            icon.transform.SetParent(transform, false);
            icon.transform.localPosition = new Vector3(0f, 0f, -0.006f);
            float iconSize = maxSize * 0.55f;
            icon.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(iconSize, iconSize);
            iconMat = BrushMaterials.CreateUnlit(Color.white, opaque: false);
            BrushMaterials.PreserveDestAlpha(iconMat); // niente "buco" nel passthrough
            icon.AddComponent<MeshRenderer>().material = iconMat;
            icon.SetActive(false);

            Apply(); // stato iniziale (utile anche in edit mode, dove Update non gira)
        }

        void Update() => Apply();

        void Apply()
        {
            if (swatchMat == null || swatch == null || border == null)
                return;

            var tool = StrokeSettings.Tool;
            bool erasing = tool == ToolMode.Eraser || tool == ToolMode.Delete;

            if (erasing)
            {
                // Grigio trasparente + icona dello strumento: "qui non si sceglie un colore".
                swatchMat.SetColor(BaseColorId, StrokeSettings.EraserSwatchColor);
                float sz = maxSize * 0.85f;
                border.localScale = new Vector3(sz + borderPadding, sz + borderPadding, 1f);
                swatch.localScale = new Vector3(sz, sz, 1f);
                if (icon != null && tool != lastIconTool)
                {
                    lastIconTool = tool;
                    iconMat.mainTexture = ToolIcon.Get(tool == ToolMode.Delete ? "close" : "eraser");
                    icon.SetActive(true);
                }
                return;
            }

            if (icon != null && icon.activeSelf)
            {
                icon.SetActive(false);
                lastIconTool = (ToolMode)(-1);
            }

            swatchMat.SetColor(BaseColorId, StrokeSettings.Color);

            float s = Mathf.Lerp(minSize, maxSize, StrokeSettings.Size01);

            border.localScale = new Vector3(s + borderPadding, s + borderPadding, 1f);
            swatch.localScale = new Vector3(s, s, 1f);
        }
    }
}
