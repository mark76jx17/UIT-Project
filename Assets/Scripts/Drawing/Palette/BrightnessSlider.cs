using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Slider verticale di luminosità accanto alla ruota dei colori. Convenzione a 3
    /// fermate: in basso nero, al centro il colore pieno scelto sulla ruota, in alto
    /// bianco. Gradiente generato in codice e rigenerato quando cambia la tinta. Si usa
    /// toccandolo (visore) o trascinando (simulatore).
    /// </summary>
    public class BrightnessSlider : MonoBehaviour, IPaletteControl
    {
        float height;
        Transform knob;
        Material material;
        Texture2D texture;
        float builtHue = -1f, builtSat = -1f;

        bool built;

        void Awake()
        {
            enabled = false;
        }

        public void Build(Vector2 size)
        {
            height = size.y;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            renderer.material = material;
            Regenerate(); // crea il gradiente nero-colore-bianco e lo assegna

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            // Più largo del visivo: più facile da prendere.
            collider.size = new Vector3(size.x * 2f, size.y, 0.012f);

            var knobGO = new GameObject("Knob");
            knobGO.transform.SetParent(transform, false);
            var knobFilter = knobGO.AddComponent<MeshFilter>();
            knobFilter.mesh = RoundedMesh.Rect(size.x * 1.6f, 0.005f, 0.0025f);
            knobGO.AddComponent<MeshRenderer>().material =
                BrushMaterials.CreateUnlit(Color.white, opaque: true);
            knob = knobGO.transform;

            built = true;
            enabled = true;
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            float value = Mathf.Clamp01(local.y / height + 0.5f);
            StrokeSettings.SetHSV(StrokeSettings.Hue, StrokeSettings.Sat, value);
        }

        void Update()
        {
            if (!built || knob == null || material == null)
                return;

            knob.localPosition = new Vector3(0f, (StrokeSettings.Val - 0.5f) * height, -0.003f);

            if (!Mathf.Approximately(builtHue, StrokeSettings.Hue) ||
                !Mathf.Approximately(builtSat, StrokeSettings.Sat))
                Regenerate();
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }

        void Regenerate()
        {
            builtHue = StrokeSettings.Hue;
            builtSat = StrokeSettings.Sat;
            texture = GenerateTexture(64, StrokeSettings.PureColor, texture);
            material.SetTexture("_BaseMap", texture);
        }

        // Gradiente verticale: nero (basso) - colore pieno (centro) - bianco (alto).
        static Texture2D GenerateTexture(int size, Color pure, Texture2D reuse)
        {
            var texture = reuse != null ? reuse
                : new Texture2D(2, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[2 * size];
            for (int y = 0; y < size; y++)
            {
                float t = (float)y / (size - 1); // 0 basso, 1 alto
                Color color = t <= 0.5f
                    ? Color.Lerp(Color.black, pure, t * 2f)
                    : Color.Lerp(pure, Color.white, (t - 0.5f) * 2f);
                color.a = 1f;
                pixels[y * 2] = color;
                pixels[y * 2 + 1] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
