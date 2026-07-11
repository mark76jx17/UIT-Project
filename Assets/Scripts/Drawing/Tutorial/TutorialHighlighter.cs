using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Evidenziatore pulsante per un controllo della palette negli step senza tasto. Due forme:
    /// - ANELLO sottile che incornicia da FUORI un controllo tondo (ruota colori), senza coprirlo;
    /// - CORNICE rettangolare pulsante attorno a un controllo allungato (slider).
    /// Giace sul controllo (stessa orientazione), dimensionato sui suoi bounds.
    /// </summary>
    public class TutorialHighlighter : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color HiColor = new(1f, 0.82f, 0.20f); // ambra "guarda qui"

        Transform target;
        bool useRect;
        Transform ring;
        Material ringMat;
        Transform frame;
        Material frameMat;
        Transform[] bars = new Transform[4];

        void Awake()
        {
            // Anello sottile.
            var r = new GameObject("Ring");
            r.transform.SetParent(transform, false);
            r.AddComponent<MeshFilter>().mesh = TutorialMeshes.Ring(0.5f, 0.45f);
            ringMat = BrushMaterials.CreateUnlit(HiColor, opaque: true);
            r.AddComponent<MeshRenderer>().material = ringMat;
            ring = r.transform;
            ring.localPosition = new Vector3(0f, 0f, -0.008f);

            // Cornice rettangolare (4 barre).
            frame = new GameObject("Frame").transform;
            frame.SetParent(transform, false);
            frame.localPosition = new Vector3(0f, 0f, -0.008f);
            frameMat = BrushMaterials.CreateUnlit(HiColor, opaque: true);
            for (int i = 0; i < 4; i++)
            {
                var b = new GameObject("Bar" + i);
                b.transform.SetParent(frame, false);
                b.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(1f, 1f);
                b.AddComponent<MeshRenderer>().material = frameMat;
                bars[i] = b.transform;
            }

            gameObject.SetActive(false);
        }

        public void Show(Transform t, bool rect)
        {
            target = t;
            useRect = rect;

            float ex = 0.04f, ey = 0.04f;
            var rend = t != null ? t.GetComponentInChildren<Renderer>() : null;
            if (rend != null)
            {
                ex = rend.bounds.extents.x;
                ey = rend.bounds.extents.y;
            }

            ring.gameObject.SetActive(!rect);
            frame.gameObject.SetActive(rect);

            if (rect)
            {
                const float th = 0.006f;               // spessore barre
                float hw = ex * 1.12f, hh = ey * 1.35f; // margine attorno allo slider
                bars[0].localPosition = new Vector3(0f, hh, 0f);   // alto
                bars[0].localScale = new Vector3(2f * hw + th, th, 1f);
                bars[1].localPosition = new Vector3(0f, -hh, 0f);  // basso
                bars[1].localScale = new Vector3(2f * hw + th, th, 1f);
                bars[2].localPosition = new Vector3(-hw, 0f, 0f);  // sinistra
                bars[2].localScale = new Vector3(th, 2f * hh + th, 1f);
                bars[3].localPosition = new Vector3(hw, 0f, 0f);   // destra
                bars[3].localScale = new Vector3(th, 2f * hh + th, 1f);
            }
            else
            {
                // Anello appena FUORI dal controllo tondo (non lo copre): mesh raggio interno 0.45,
                // esterno 0.5 → con scala così il bordo interno cade sul raggio della ruota.
                float outer = Mathf.Max(ex, ey) * 1.12f;
                ring.localScale = Vector3.one * (2f * outer);
            }

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            target = null;
            gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (target == null)
                return;
            transform.SetPositionAndRotation(target.position, target.rotation);
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 5f);
            var c = HiColor * pulse; c.a = 1f;
            (useRect ? frameMat : ringMat).SetColor(BaseColorId, c);
        }
    }
}
