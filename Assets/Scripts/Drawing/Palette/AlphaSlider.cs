using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Slider orizzontale della trasparenza (StrokeSettings.Alpha). Una rampa del colore
    /// corrente sfuma da trasparente (sinistra) a pieno (destra). Dietro c'è una scacchiera
    /// OPACA: così la parte trasparente si legge come scacchiera e non come "buco" sul
    /// passthrough (niente più see-through). Il pomello indica l'alpha scelto; si usa
    /// toccandolo con la punta del pennello (visore) o trascinando (simulatore).
    /// </summary>
    public class AlphaSlider : MonoBehaviour, IPaletteControl
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Texture2D rampTexture;
        static Texture2D checkerTexture;

        float width;
        Transform knob;
        Material material;

        public void Build(Vector2 size)
        {
            width = size.x;

            // Sfondo opaco a scacchiera (dietro la rampa).
            var bg = new GameObject("Checker");
            bg.transform.SetParent(transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.0006f);
            bg.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var bgMat = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            bgMat.SetTexture("_BaseMap", Checker());
            bg.AddComponent<MeshRenderer>().material = bgMat;

            // Davanti: rampa colore (RGB corrente, alpha 0→1) trasparente sopra la scacchiera.
            gameObject.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            material = BrushMaterials.CreateUnlit(Color.white); // trasparente, alpha dalla rampa
            material.SetTexture("_BaseMap", Ramp());
            gameObject.AddComponent<MeshRenderer>().material = material;

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y * 2f, 0.012f);

            // Pomello tondo via RoundedMesh (corner = metà lato ≈ cerchio): niente
            // CreatePrimitive+Destroy, che in edit mode (anteprima) lancia
            // "Destroy may not be called from edit mode".
            var knobGO = new GameObject("AlphaKnob");
            knobGO.transform.SetParent(transform, false);
            float kd = size.y * 1.6f;
            knobGO.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(kd, kd, kd * 0.5f);
            knobGO.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            knob = knobGO.transform;
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            StrokeSettings.Alpha = Mathf.Clamp01(local.x / width + 0.5f);
        }

        void Update()
        {
            knob.localPosition = new Vector3((StrokeSettings.Alpha - 0.5f) * width, 0f, -0.004f);
            // Tinge la rampa col colore corrente in tempo reale: la rampa dà l'alpha 0→1.
            if (material != null)
            {
                var c = StrokeSettings.BaseColor;
                c.a = 1f;
                material.SetColor(BaseColorId, c);
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }

        // Rampa orizzontale: RGB bianco, alpha che cresce 0→1 da sinistra a destra.
        static Texture2D Ramp()
        {
            if (rampTexture != null)
                return rampTexture;
            const int w = 64;
            rampTexture = new Texture2D(w, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * 2];
            for (int x = 0; x < w; x++)
            {
                float a = (float)x / (w - 1);
                px[x] = px[x + w] = new Color(1f, 1f, 1f, a);
            }
            rampTexture.SetPixels(px);
            rampTexture.Apply();
            return rampTexture;
        }

        // Scacchiera opaca (indica "trasparente" senza far vedere il mondo dietro).
        static Texture2D Checker()
        {
            if (checkerTexture != null)
                return checkerTexture;
            const int w = 64, h = 16, cell = 8;
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
