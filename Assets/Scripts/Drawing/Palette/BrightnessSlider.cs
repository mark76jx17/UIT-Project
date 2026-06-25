using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Slider verticale di luminosità (la V di HSV) accanto alla ruota dei
    /// colori: gradiente nero→bianco generato in codice, pomello che segue il
    /// valore corrente. Si usa toccandolo (visore) o trascinando (simulatore).
    /// </summary>
    public class BrightnessSlider : MonoBehaviour, IPaletteControl
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        float height;
        Transform knob;
        Material material;

        public void Build(Vector2 size)
        {
            height = size.y;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            material.SetTexture("_BaseMap", GenerateTexture(64));
            renderer.material = material;

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
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            float value = Mathf.Clamp01(local.y / height + 0.5f);
            StrokeSettings.SetHSV(StrokeSettings.Hue, StrokeSettings.Sat, value);
        }

        void Update()
        {
            knob.localPosition = new Vector3(0f, (StrokeSettings.Val - 0.5f) * height, -0.003f);
            // Tinge il gradiente: nero→colore pieno alla tinta/saturazione correnti,
            // così la barra mostra "l'intensità di QUESTO colore".
            if (material != null)
                material.SetColor(BaseColorId, Color.HSVToRGB(StrokeSettings.Hue, StrokeSettings.Sat, 1f));
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }

        static Texture2D GenerateTexture(int size)
        {
            var texture = new Texture2D(2, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[2 * size];
            for (int y = 0; y < size; y++)
            {
                float v = (float)y / (size - 1);
                var color = new Color(v, v, v, 1f);
                pixels[y * 2] = color;
                pixels[y * 2 + 1] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
