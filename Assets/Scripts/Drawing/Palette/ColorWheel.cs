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
        Material material;
        Texture2D texture;
        Color background = Color.black;
        float builtVal = -1f;
        const int TexSize = 160;

        // Zoom di prossimità: la ruota si ingrandisce quando la punta del pennello
        // si avvicina (per scegliere il colore con precisione) e si rimpicciolisce
        // quando il controller si allontana.
        Transform proximityTarget;
        const float ZoomMin = 1f, NearDist = 0.07f, FarDist = 0.20f;
        // Zoom massimo: calcolato da BuildPanel in base alla geometria, così la ruota
        // smette di ingrandirsi quando l'angolo del suo quadrato raggiunge l'angolo del
        // pannello (passato a Build). Default prudente se non impostato.
        float zoomMax = 1.24f;
        float currentZoom = 1f;

        bool built;

        public void SetProximityTarget(Transform target) => proximityTarget = target;

        void Awake()
        {
            enabled = false;
        }
        public void Build(float diameter, Color background, float maxZoom)
        {
            radius = diameter * 0.5f;
            zoomMax = Mathf.Max(1f, maxZoom);

            var filter = gameObject.AddComponent<MeshFilter>();
            // Disco (niente quad): ingrandendosi non c'è alcun angolo che sbuca oltre il
            // bordo arrotondato del pannello.
            filter.mesh = RoundedMesh.TexturedDisc(diameter);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            // Opaco: il bordo del disco è dipinto del colore del pannello (vedi
            // GenerateTexture), così non si vede il mondo reale nel passthrough.
            this.background = background;
            material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            renderer.material = material;
            Regenerate(); // la ruota riflette la luminosità corrente (nero-colore-bianco)

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(diameter, diameter, 0.012f);

            var knobGO = new GameObject("Knob");
            knobGO.transform.SetParent(transform, false);
            var knobFilter = knobGO.AddComponent<MeshFilter>();
            knobFilter.mesh = RoundedMesh.Rect(0.009f, 0.009f, 0.0045f);
            knobGO.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            knob = knobGO.transform;

            built = true;
            enabled = true;
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
            if (!built || knob == null || material == null)
                return;

            float angle = StrokeSettings.Hue * Mathf.PI * 2f;
            float r = StrokeSettings.Sat * radius;
            knob.localPosition = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, -0.003f);

            if (proximityTarget != null)
            {
                float d = Vector3.Distance(proximityTarget.position, transform.position);
                float near = Mathf.InverseLerp(FarDist, NearDist, d);
                float target = Mathf.Lerp(ZoomMin, zoomMax, near);
                currentZoom = Mathf.Lerp(currentZoom, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
                transform.localScale = Vector3.one * currentZoom;
            }

            if (!Mathf.Approximately(builtVal, StrokeSettings.Val))
                Regenerate();
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }

        void Regenerate()
        {
            builtVal = StrokeSettings.Val;
            texture = GenerateTexture(TexSize, background, builtVal, texture);
            material.SetTexture("_BaseMap", texture);
        }

        // Texture OPACA: disco HSV su sfondo = colore del pannello. I colori riflettono la
        // LUMINOSITA corrente (val): nero (val 0) - colore pieno (val 0.5) - bianco (val 1),
        // così la ruota si schiarisce/scurisce insieme allo slider. Niente alpha (no
        // see-through sul passthrough); bordo del disco anti-aliasato fondendolo con lo sfondo.
        static Texture2D GenerateTexture(int size, Color background, float val, Texture2D reuse = null)
        {
            var texture = reuse != null ? reuse
                : new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
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
                    var pure = Color.HSVToRGB(hue, Mathf.Min(r, 1f), 1f);
                    // Stessa modulazione del colore disegnato (vedi StrokeSettings.BaseColor).
                    var disc = val <= 0.5f
                        ? Color.Lerp(Color.black, pure, val * 2f)
                        : Color.Lerp(pure, Color.white, (val - 0.5f) * 2f);
                    // Copertura del disco (1 dentro, 0 fuori) con bordo morbido: usata per
                    // fondere disco e sfondo, non come alpha.
                    float coverage = Mathf.Clamp01((1f - r) * half * 0.5f);
                    var color = Color.Lerp(background, disc, coverage);
                    color.a = 1f;
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
