#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MixedRealityProject.Drawing.EditorTools
{
    /// <summary>
    /// Costruisce il pannello scorciatoie in EDIT mode (niente Play) e lo renderizza a PNG nella
    /// lingua corrente, così si può iterare il layout senza visore. Render e inquadratura sono
    /// condivisi con PanelPreviews.CapturePanel (vedi Tools/Drawing/Preview All Panels per
    /// generare TUTTI i pannelli in TUTTE le lingue in un colpo solo).
    /// Menu: Tools/Drawing/Preview Shortcuts Panel.
    /// </summary>
    public static class ShortcutsPanelPreview
    {
        const string OutDir = PanelPreviews.OutDir;
        const string OutFile = OutDir + "/shortcuts.png";
        const int Width = 1150;
        const int Height = 980;

        [MenuItem("Tools/Drawing/Preview Shortcuts Panel")]
        public static void Preview()
        {
            Directory.CreateDirectory(OutDir);

            var root = new GameObject("ShortcutsPreviewRoot");
            try
            {
                PaletteController.DebugAnchorGrid = false; // true per ritarare gli anchor
                var pc = root.AddComponent<PaletteController>();
                var panel = pc.EditorBuildShortcutsPanel();
                if (panel == null)
                {
                    Debug.LogError("[ShortcutsPanelPreview] Pannello non costruito.");
                    return;
                }

                PanelPreviews.CapturePanel(root.transform, panel, OutFile, Width, Height);
                AssetDatabase.Refresh();
                Debug.Log("[ShortcutsPanelPreview] Anteprima salvata in " + OutFile);
            }
            finally
            {
                PaletteController.DebugAnchorGrid = false;
                Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
