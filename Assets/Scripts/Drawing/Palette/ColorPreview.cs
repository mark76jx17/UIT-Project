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

        Transform swatch;
        Material swatchMat;
        float minSize, maxSize;
        Transform border;
        float borderPadding;

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
            sw.AddComponent<MeshRenderer>().material = swatchMat;
            swatch = sw.transform;

            maxSize = Mathf.Min(box.x, box.y) * 0.88f;
            minSize = maxSize * 0.22f;
            borderPadding = maxSize * 0.08f;

            Apply(); // stato iniziale (utile anche in edit mode, dove Update non gira)
        }

        void Update() => Apply();

        void Apply()
        {
            if (swatchMat == null || swatch == null || border == null)
                return;

            swatchMat.SetColor(BaseColorId, StrokeSettings.Color);

            float s = Mathf.Lerp(minSize, maxSize, StrokeSettings.Size01);

            border.localScale = new Vector3(s + borderPadding, s + borderPadding, 1f);
            swatch.localScale = new Vector3(s, s, 1f);
        }
    }
}
