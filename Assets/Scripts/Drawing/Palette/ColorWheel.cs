using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Ruota dei colori HSV: texture generata in codice (tinta = angolo,
    /// saturazione = raggio), si "pittura" toccandola con la punta del pennello
    /// (OnTriggerStay) o, nel simulatore, trascinando col mouse (PressAt).
    /// Un pomello bianco mostra la selezione corrente e si sincronizza anche
    /// quando il colore cambia da tastiera.
    /// </summary>
    public class ColorWheel : MonoBehaviour, IPaletteControl
    {
        float radius;
        Transform knob;

        // Zoom di prossimità: la ruota si ingrandisce quando la punta del pennello
        // si avvicina (per scegliere il colore con precisione) e si rimpicciolisce
        // quando il controller si allontana.
        Transform proximityTarget;
        const float ZoomMin = 1f, ZoomMax = 1.9f, NearDist = 0.07f, FarDist = 0.20f;
        float currentZoom = 1f;

        public void SetProximityTarget(Transform target) => proximityTarget = target;

        public void Build(float diameter)
        {
            radius = diameter * 0.5f;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = RoundedMesh.TexturedQuad(diameter, diameter);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            var material = BrushMaterials.CreateUnlit(Color.white);
            material.SetTexture("_BaseMap", GenerateTexture(192));
            renderer.material = material;

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(diameter, diameter, 0.012f);

            var knobGO = new GameObject("Knob");
            knobGO.transform.SetParent(transform, false);
            var knobFilter = knobGO.AddComponent<MeshFilter>();
            knobFilter.mesh = RoundedMesh.Rect(0.009f, 0.009f, 0.0045f);
            knobGO.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            knob = knobGO.transform;
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            var uv = new Vector2(local.x, local.y) / radius; // -1..1
            float r = uv.magnitude;
            if (r > 1f)
                uv /= r;
            float hue = Mathf.Atan2(uv.y, uv.x) / (Mathf.PI * 2f);
            if (hue < 0f)
                hue += 1f;
            StrokeSettings.SetHSV(hue, Mathf.Min(r, 1f), StrokeSettings.Val);
        }

        void Update()
        {
            float angle = StrokeSettings.Hue * Mathf.PI * 2f;
            float r = StrokeSettings.Sat * radius;
            knob.localPosition = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, -0.003f);

            if (proximityTarget != null)
            {
                float d = Vector3.Distance(proximityTarget.position, transform.position);
                float near = Mathf.InverseLerp(FarDist, NearDist, d); // 1 vicino, 0 lontano
                float target = Mathf.Lerp(ZoomMin, ZoomMax, near);
                currentZoom = Mathf.Lerp(currentZoom, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
                transform.localScale = Vector3.one * currentZoom;
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }

        static Texture2D GenerateTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float hue = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f);
                    if (hue < 0f)
                        hue += 1f;
                    var color = Color.HSVToRGB(hue, Mathf.Min(r, 1f), 1f);
                    // Bordo morbido del disco.
                    color.a = Mathf.Clamp01((1f - r) * half * 0.5f);
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
