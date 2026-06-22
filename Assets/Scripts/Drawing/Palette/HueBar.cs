using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Barra orizzontale della tinta (Hue): asse X = tinta 0..1 (arcobaleno).
    /// Tocco col pennello (OnTriggerStay) o mouse (PressAt) imposta la tinta
    /// mantenendo saturazione e valore correnti; il <see cref="ColorSquare"/> si
    /// ricolora di conseguenza. Materiale opaco. Un pomello mostra la tinta scelta.
    /// </summary>
    public class HueBar : MonoBehaviour
    {
        Vector2 size;
        Transform knob;

        public void Build(float width, float height)
        {
            size = new Vector2(width, height);

            gameObject.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(width, height);
            var material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            material.SetTexture("_BaseMap", GenerateTexture(256));
            gameObject.AddComponent<MeshRenderer>().material = material;

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(width, height, 0.012f);

            var knobGO = new GameObject("Knob");
            knobGO.transform.SetParent(transform, false);
            knobGO.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(0.008f, height * 1.3f, 0.003f);
            knobGO.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            knob = knobGO.transform;
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            float hue = Mathf.Clamp01(local.x / size.x + 0.5f);
            StrokeSettings.SetHSV(hue, StrokeSettings.Sat, StrokeSettings.Val);
        }

        void Update()
        {
            knob.localPosition = new Vector3((StrokeSettings.Hue - 0.5f) * size.x, 0f, -0.003f);
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }

        // Striscia 1px di altezza: tinta che scorre lungo X a saturazione e valore pieni.
        static Texture2D GenerateTexture(int width)
        {
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width];
            for (int x = 0; x < width; x++)
                pixels[x] = Color.HSVToRGB((float)x / (width - 1), 1f, 1f);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
