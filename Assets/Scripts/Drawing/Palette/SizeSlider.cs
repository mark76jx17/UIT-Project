using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Slider orizzontale dello spessore (StrokeSettings.Size01). È un cuneo opaco —
    /// sottile a sinistra, spesso a destra — tinto col colore corrente in tempo reale:
    /// si capisce a colpo d'occhio che regola lo spessore del tratto. In Cancella/Elimina
    /// prende invece il grigio "gomma" (lo stesso del checker): lì regola la dimensione
    /// della gomma, non un colore. Il pomello indica il valore scelto. Tocco col pennello
    /// (visore) o trascinamento (simulatore).
    /// </summary>
    public class SizeSlider : MonoBehaviour, IPaletteControl
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        float width;
        Transform knob;
        Material material;

        bool built;

        void Awake()
        {
            enabled = false;
        }

        public void Build(Vector2 size)
        {
            width = size.x;

            gameObject.AddComponent<MeshFilter>().mesh = WedgeMesh(size.x, size.y);
            material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            gameObject.AddComponent<MeshRenderer>().material = material;

            var collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y * 2f, 0.012f);

            var knobGO = new GameObject("SizeKnob");
            knobGO.transform.SetParent(transform, false);
            knobGO.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(0.006f, size.y * 1.3f, 0.002f);
            knobGO.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            knob = knobGO.transform;

            built = true;
            enabled = true;
        }

        public void PressAt(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            StrokeSettings.Size01 = Mathf.Clamp01(local.x / width + 0.5f);
        }

        void Update()
        {
            if (!built || knob == null || material == null)
                return;

            knob.localPosition = new Vector3((StrokeSettings.Size01 - 0.5f) * width, 0f, -0.004f);

            // In gomma (Cancella/Elimina) lo slider regola la dimensione della gomma, non un
            // colore: mostra il grigio "gomma" del checker invece dell'ultimo colore scelto.
            var c = StrokeSettings.EraserMode || StrokeSettings.DeleteMode
                ? StrokeSettings.EraserSwatchColor
                : StrokeSettings.BaseColor;
            c.a = 1f;
            material.SetColor(BaseColorId, c);
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() != null)
                PressAt(other.transform.position);
        }

        // Cuneo nel piano XY: sottile a sinistra (minH), spesso a destra (maxH).
        // Doppia faccia (triangoli in entrambi i sensi) per non dipendere dal culling.
        static Mesh WedgeMesh(float w, float h)
        {
            float minH = h * 0.14f, maxH = h;
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-w * 0.5f, -minH * 0.5f, 0f),
                    new Vector3(-w * 0.5f,  minH * 0.5f, 0f),
                    new Vector3( w * 0.5f,  maxH * 0.5f, 0f),
                    new Vector3( w * 0.5f, -maxH * 0.5f, 0f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
