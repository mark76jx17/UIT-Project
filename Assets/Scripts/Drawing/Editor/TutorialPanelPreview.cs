#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MixedRealityProject.Drawing.EditorTools
{
    /// <summary>
    /// Costruisce i pannelli del tutorial (welcome, card) in EDIT mode (niente Play) e li
    /// renderizza a PNG, così si può iterare il layout senza visore. Riusa camera/inquadratura
    /// di PanelPreviews.CapturePanel. Menu: Tools/Drawing/Preview Tutorial Panels.
    /// </summary>
    public static class TutorialPanelPreview
    {
        const string OutDir = PanelPreviews.OutDir;

        [MenuItem("Tools/Drawing/Preview Tutorial Panels")]
        public static void Preview()
        {
            Directory.CreateDirectory(OutDir);
            string savedLang = Localization.Current;
            try
            {
                CaptureWelcome($"{OutDir}/tutorial-welcome.png");
                // Step con immagine controller (destrimano: pennello = destro, palette = sinistro).
                var right = ControllerHint.Image(false);
                var left = ControllerHint.Image(true);
                CaptureStep($"{OutDir}/tutorial-step-draw.png", "tutorial.step.draw",
                    right, ControllerHint.Trigger(false), true,
                    Localization.Get("tutorial.ctrl.right") + "\n" + Localization.Get("tutorial.btn.trigger"));
                CaptureStep($"{OutDir}/tutorial-step-move.png", "tutorial.step.move",
                    right, ControllerHint.Grip(false), true,
                    Localization.Get("tutorial.ctrl.right") + "\n" + Localization.Get("tutorial.btn.grip"));
                CaptureStep($"{OutDir}/tutorial-step-color.png", "tutorial.step.color",
                    null, Vector2.zero, false, null);
                CaptureControllerGrid($"{OutDir}/controller-grid-right.png", right, false);
                CaptureControllerGrid($"{OutDir}/controller-grid-left.png", left, true);
                Debug.Log("[TutorialPanelPreview] Anteprime salvate in " + OutDir);
            }
            finally
            {
                Localization.Current = savedLang;
                AssetDatabase.Refresh();
            }
        }

        static void CaptureWelcome(string outFile)
        {
            var root = new GameObject("TutWelcomePreview");
            try
            {
                var w = root.AddComponent<TutorialWelcomePanel>();
                w.Build(() => { }, () => { });
                root.SetActive(true);
                ForceText(root);
                PanelPreviews.CapturePanel(root.transform, root, outFile, 1150, 800);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void CaptureStep(string outFile, string titleKey, Texture2D controller,
            Vector2 anchor, bool showImage, string buttonName)
        {
            var root = new GameObject("TutCardPreview");
            try
            {
                var c = root.AddComponent<TutorialCard>();
                c.Build();
                c.SetStep(Localization.Get(titleKey), controller, anchor, showImage, buttonName);
                root.SetActive(true);
                ForceText(root);
                // Inquadro sul banner attivo (grande con immagine, piccolo di solo testo).
                var bg = root.transform.Find(showImage ? "CardBgLarge" : "CardBgSmall");
                PanelPreviews.CapturePanel(root.transform, bg != null ? bg.gameObject : root, outFile, 820, 980);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Controller con griglia di coordinate 0..1 (x→destra, y→giù) per tarare le ancore dei
        // tasti. Linee ogni 0.1; assi (0.5) in ciano. Etichette agli assi.
        static void CaptureControllerGrid(string outFile, Texture2D controller, bool physicalLeft)
        {
            const float S = 0.24f;
            var root = new GameObject("CtrlGridPreview");
            try
            {
                // Sfondo grigio per vedere lo sfondo trasparente del PNG.
                Quad(root.transform, "Bg", new Vector3(0, 0, 0.002f), new Vector2(S * 1.05f, S * 1.05f),
                    new Color(0.35f, 0.35f, 0.38f), null);
                // Controller.
                var c = Quad(root.transform, "Ctrl", new Vector3(0, 0, -0.004f), new Vector2(S, S),
                    Color.white, controller);
                c.GetComponent<Renderer>().material.SetTexture("_BaseMap", controller);
                // Griglia.
                for (int i = 0; i <= 10; i++)
                {
                    float f = i / 10f;
                    bool axis = i == 5;
                    var col = axis ? new Color(0f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.6f);
                    float t = axis ? 0.0016f : 0.0008f;
                    float wx = (f - 0.5f) * S, wy = (0.5f - f) * S;
                    Quad(root.transform, "V", new Vector3(wx, 0, -0.006f), new Vector2(t, S), col, null);
                    Quad(root.transform, "H", new Vector3(0, wy, -0.006f), new Vector2(S, t), col, null);
                }
                // Anelli attuali: trigger (ambra) e grip (magenta) alle posizioni di ControllerHint.
                RingAt(root.transform, ControllerHint.Trigger(physicalLeft), S, new Color(1f, 0.82f, 0.2f));
                RingAt(root.transform, ControllerHint.Grip(physicalLeft), S, new Color(1f, 0.2f, 0.9f));
                TutorialUi.SetLayer(root, PaletteController.PaletteLayer);
                root.SetActive(true);
                PanelPreviews.CapturePanel(root.transform, root, outFile, 900, 900);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void RingAt(Transform parent, Vector2 anchor01, float S, Color color)
        {
            var pos = new Vector3((anchor01.x - 0.5f) * S, (0.5f - anchor01.y) * S, -0.008f);
            TutorialUi.Ring(parent, "AnchorRing", pos, 0.020f, 0.014f, color);
        }

        static GameObject Quad(Transform parent, string name, Vector3 pos, Vector2 size, Color color, Texture2D tex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var mat = BrushMaterials.CreateUnlit(color, opaque: tex == null);
            if (tex != null) { mat.mainTexture = tex; BrushMaterials.PreserveDestAlpha(mat); }
            go.AddComponent<MeshRenderer>().material = mat;
            return go;
        }

        // In edit mode il TMP può non aver ancora generato la mesh del testo: forzo l'update
        // prima del render, altrimenti il testo esce vuoto/non impaginato.
        static void ForceText(GameObject root)
        {
            foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
                tmp.ForceMeshUpdate();
        }
    }
}
#endif
