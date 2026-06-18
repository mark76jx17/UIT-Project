using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Slider orizzontale per scegliere lo spessore fisso della penna.
    /// Funziona come BrightnessSlider: tocco con BrushTip o click nel simulatore.
    /// </summary>
    public class SizeSlider : MonoBehaviour
    {
        float width;
        Transform knob;

        public void Build(Vector2 size)
        {
            width = size.x;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = RoundedMesh.Rect(size.x, size.y, size.y * 0.5f);

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.material = BrushMaterials.CreateUnlit(new Color(0.32f, 0.32f, 0.38f, 1f));

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y * 2f, 0.012f);

            var knobGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knobGO.name = "SizeKnob";
            Destroy(knobGO.GetComponent<Collider>());
            knobGO.transform.SetParent(transform, false);

            knobGO.GetComponent<MeshRenderer>().material =
                BrushMaterials.CreateUnlit(Color.white);

            knob = knobGO.transform;
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            StrokeSettings.Size01 = Mathf.Clamp01(local.x / width + 0.5f);
        }

        void Update()
        {
            float x = (StrokeSettings.Size01 - 0.5f) * width;
            knob.localPosition = new Vector3(x, 0f, -0.004f);

            // Il pomello cresce un po' con lo spessore, ma con un tetto: non deve
            // diventare gigante sul pannello.
            float radius = StrokeSettings.FixedRadius;
            knob.localScale = Vector3.one * Mathf.Clamp(radius * 1.4f, 0.009f, 0.016f);
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }
    }
}