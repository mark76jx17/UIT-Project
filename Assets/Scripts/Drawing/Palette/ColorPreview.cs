using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Anteprima del tratto: un rettangolo arrotondato che assume in tempo reale
    /// <b>colore</b>, <b>opacità</b> e <b>dimensione</b> correnti del pennello, così
    /// l'utente capisce a colpo d'occhio con cosa sta per disegnare. Sta sopra una
    /// scacchiera opaca: la trasparenza del colore si legge come scacchiera (niente
    /// see-through sul passthrough). Il rettangolo cresce/rimpicciolisce con lo slider
    /// della dimensione.
    /// </summary>
    public class ColorPreview : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Texture2D checkerTexture;

        Transform swatch;
        Material swatchMat;
        float minSize, maxSize;

        public void Build(Vector2 box)
        {
            // Sfondo a scacchiera opaco: la parte trasparente del colore si vede come
            // scacchiera invece che come "buco" sul passthrough.
            var bg = new GameObject("Checker");
            bg.transform.SetParent(transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.0006f);
            bg.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(box.x, box.y);
            var bgMat = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            bgMat.SetTexture("_BaseMap", Checker());
            bg.AddComponent<MeshRenderer>().material = bgMat;

            // Rettangolo del colore (trasparente, così l'alpha lascia vedere la scacchiera):
            // mesh unitaria scalata ogni frame in base alla dimensione del pennello.
            var sw = new GameObject("Swatch");
            sw.transform.SetParent(transform, false);
            sw.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            sw.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(1f, 1f, 0.25f);
            swatchMat = BrushMaterials.CreateUnlit(Color.white);
            sw.AddComponent<MeshRenderer>().material = swatchMat;
            swatch = sw.transform;

            maxSize = Mathf.Min(box.x, box.y) * 0.88f;
            minSize = maxSize * 0.22f;

            Apply(); // stato iniziale (utile anche in edit mode, dove Update non gira)
        }

        void Update() => Apply();

        void Apply()
        {
            if (swatchMat == null)
                return;
            swatchMat.SetColor(BaseColorId, StrokeSettings.Color); // colore + alpha correnti
            float s = Mathf.Lerp(minSize, maxSize, StrokeSettings.Size01);
            swatch.localScale = new Vector3(s, s, 1f);
        }

        // Scacchiera opaca (indica la trasparenza senza far vedere il mondo dietro).
        static Texture2D Checker()
        {
            if (checkerTexture != null)
                return checkerTexture;
            const int w = 16, h = 16, cell = 4;
            checkerTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            var dark = new Color(0.50f, 0.50f, 0.54f, 1f);
            var light = new Color(0.74f, 0.74f, 0.78f, 1f);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = (((x / cell) + (y / cell)) & 1) == 0 ? dark : light;
            checkerTexture.SetPixels(px);
            checkerTexture.Apply();
            return checkerTexture;
        }
    }
}
