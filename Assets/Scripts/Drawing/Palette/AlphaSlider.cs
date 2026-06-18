using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Slider orizzontale della trasparenza (StrokeSettings.Alpha). La barra mostra il
    /// colore corrente che sfuma da trasparente (sinistra) a pieno (destra), così si
    /// capisce a colpo d'occhio cosa fa: il pomello indica l'alpha scelto. Si usa
    /// toccandola con la punta del pennello (visore) o trascinando (simulatore).
    /// </summary>
    public class AlphaSlider : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Texture2D rampTexture;

        float width;
        Transform knob;
        Material material;

        public void Build(Vector2 size)
        {
            width = size.x;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            material = BrushMaterials.CreateUnlit(Color.white);
            material.SetTexture("_BaseMap", Ramp());
            gameObject.AddComponent<MeshRenderer>().material = material;

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y * 2f, 0.012f);

            var knobGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knobGO.name = "AlphaKnob";
            Destroy(knobGO.GetComponent<Collider>());
            knobGO.transform.SetParent(transform, false);
            knobGO.transform.localScale = Vector3.one * size.y * 1.7f;
            knobGO.GetComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(Color.white);
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
            // Tinge la barra col colore corrente: la rampa fornisce l'alpha 0→1.
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
    }
}
