using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Picker rettangolare Saturazione×Valore per la tinta corrente:
    /// asse X = saturazione (0 a sinistra → 1 a destra), asse Y = valore/luminosità
    /// (0 in basso → 1 in alto). Si "pittura" toccandolo con la punta del pennello
    /// (OnTriggerStay) o, nel simulatore, col mouse (PressAt). La texture si rigenera
    /// quando cambia la tinta (scelta dalla <see cref="HueBar"/>). Materiale opaco:
    /// niente see-through sul passthrough. Un pomello mostra la selezione corrente.
    /// </summary>
    public class ColorSquare : MonoBehaviour
    {
        Vector2 size;
        Transform knob;
        Material material;
        Texture2D texture;
        float builtHue = -1f;

        // Zoom di prossimità: il quadrato si ingrandisce un po' quando la punta del
        // pennello si avvicina (per scegliere il colore con più precisione).
        Transform proximityTarget;
        const float ZoomMin = 1f, ZoomMax = 1.12f, NearDist = 0.07f, FarDist = 0.20f;
        float currentZoom = 1f;

        public void SetProximityTarget(Transform target) => proximityTarget = target;

        public void Build(float width, float height)
        {
            size = new Vector2(width, height);

            gameObject.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(width, height);
            material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            gameObject.AddComponent<MeshRenderer>().material = material;
            RegenerateTexture(StrokeSettings.Hue);

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(width, height, 0.012f);

            var knobGO = new GameObject("Knob");
            knobGO.transform.SetParent(transform, false);
            knobGO.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(0.011f, 0.011f, 0.0055f);
            knobGO.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            knob = knobGO.transform;
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            float sat = Mathf.Clamp01(local.x / size.x + 0.5f);
            float val = Mathf.Clamp01(local.y / size.y + 0.5f);
            StrokeSettings.SetHSV(StrokeSettings.Hue, sat, val);
        }

        void Update()
        {
            // La tinta può cambiare dalla HueBar o da tastiera: rigenero la texture S×V.
            if (!Mathf.Approximately(builtHue, StrokeSettings.Hue))
                RegenerateTexture(StrokeSettings.Hue);

            knob.localPosition = new Vector3(
                (StrokeSettings.Sat - 0.5f) * size.x,
                (StrokeSettings.Val - 0.5f) * size.y,
                -0.003f);

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

        void RegenerateTexture(float hue)
        {
            builtHue = hue;
            const int res = 128;
            if (texture == null)
                texture = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            var pixels = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                float val = (float)y / (res - 1);          // basso → alto = 0 → 1
                for (int x = 0; x < res; x++)
                {
                    float sat = (float)x / (res - 1);      // sx → dx = 0 → 1
                    pixels[y * res + x] = Color.HSVToRGB(hue, sat, val);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            material.SetTexture("_BaseMap", texture);
        }
    }
}
