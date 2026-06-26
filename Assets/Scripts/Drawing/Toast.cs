using TMPro;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Messaggio breve a schermo (toast) per dare feedback visibile sul VISORE alle
    /// azioni che altrimenti scrivono solo Debug.Log (invisibile sul device): Salva,
    /// Carica, Svuota, Export, Undo/Redo. API statica: Toast.Show("...").
    /// </summary>
    public static class Toast
    {
        public static void Show(string message) => ToastController.ShowMessage(message);
    }

    /// <summary>
    /// Pannellino scuro + testo che fluttua davanti allo sguardo, compare quando arriva
    /// un messaggio e svanisce dopo qualche secondo. Creato dal DrawingRig all'avvio.
    /// </summary>
    public class ToastController : MonoBehaviour
    {
        static ToastController instance;

        const float HoldTime = 1.5f;
        const float FadeTime = 0.45f;
        const int QueuePanel = 3000, QueueText = 3004;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color PanelColor = new(0.07f, 0.07f, 0.10f, 0.92f);

        TextMeshPro label;
        Material panelMat;
        MeshRenderer panelRenderer;
        Transform head;
        float timer;

        public static void ShowMessage(string message)
        {
            if (instance != null)
                instance.Display(message);
        }

        void Awake()
        {
            instance = this;
            Build();
            SetVisible(false); // niente messaggio = niente quad renderizzato (vedi sotto)
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void Build()
        {
            var panel = new GameObject("ToastPanel");
            panel.transform.SetParent(transform, false);
            panel.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(0.30f, 0.07f, 0.02f);
            panelMat = BrushMaterials.CreateUnlit(PanelColor); // trasparente: può sfumare
            panelMat.renderQueue = QueuePanel;
            panelRenderer = panel.AddComponent<MeshRenderer>();
            panelRenderer.material = panelMat;

            var textGO = new GameObject("ToastText");
            textGO.transform.SetParent(transform, false);
            textGO.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            label = textGO.AddComponent<TextMeshPro>();
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = false;
            label.fontSize = 0.3f; // come le label della palette (point-size in unità locali)
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.color = Color.white;
            label.rectTransform.sizeDelta = new Vector2(0.29f, 0.07f);
            if (label.font == null)
                label.font = TMP_Settings.defaultFontAsset;
            if (label.fontSharedMaterial != null)
                label.fontMaterial.renderQueue = QueueText;
        }

        void Display(string message)
        {
            label.text = message;
            timer = HoldTime + FadeTime;
            SetVisible(true);
        }

        void LateUpdate()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;

            // Fluttua davanti allo sguardo, leggermente sotto il centro.
            if (head != null)
            {
                transform.position = head.position + head.forward * 0.6f - head.up * 0.18f;
                var dir = transform.position - head.position;
                if (dir.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            if (timer > 0f)
            {
                timer -= Time.deltaTime;
                SetAlpha(timer >= FadeTime ? 1f : Mathf.Clamp01(timer / FadeTime));
                // Finito il messaggio SPEGNI i renderer: non basta azzerare l'alpha del colore,
                // perché un quad trasparente comunque renderizzato resta un rettangolo che
                // fluttua davanti allo sguardo e "buca" l'occlusione del passthrough (see-through)
                // quando passa sopra le altre interfacce. Niente messaggio = niente render.
                if (timer <= 0f)
                    SetVisible(false);
            }
        }

        // Accende/spegne il render del toast (pannello + testo): a riposo è completamente
        // assente dal frame, così non lascia un rettangolo fantasma sul passthrough.
        void SetVisible(bool visible)
        {
            if (panelRenderer != null)
                panelRenderer.enabled = visible;
            if (label != null)
                label.enabled = visible;
        }

        void SetAlpha(float a)
        {
            if (panelMat != null)
            {
                var c = PanelColor;
                c.a *= a;
                panelMat.SetColor(BaseColorId, c);
            }
            if (label != null)
            {
                var c = label.color;
                c.a = a;
                label.color = c;
            }
        }
    }
}
