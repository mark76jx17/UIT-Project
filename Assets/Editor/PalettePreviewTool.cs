#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MixedRealityProject.Drawing;

namespace MixedRealityProject.DrawingEditor
{
    /// <summary>
    /// Strumento di sviluppo: costruisce un'anteprima statica della palette
    /// procedurale in edit mode (senza Play), così la si può ispezionare e
    /// fotografare. Disabilita temporaneamente la vecchia palette uGUI (che a
    /// runtime spegne DrawingRig ma in edit mode è attiva e sporcherebbe la vista),
    /// ripristinandola su Clear. È solo un aiuto al design; non finisce in build.
    /// </summary>
    public static class PalettePreviewTool
    {
        const string PreviewName = "PalettePreview";
        static readonly List<GameObject> disabledLegacy = new();

        [MenuItem("Tools/Palette Preview/Build")]
        public static void Build()
        {
            Clear();
            HideLegacy();

            var go = new GameObject(PreviewName);
            go.transform.position = new Vector3(20f, 1.2f, 0f);
            go.transform.rotation = Quaternion.identity;
            var pc = go.AddComponent<PaletteController>();
            var m = typeof(PaletteController).GetMethod("BuildPanel",
                BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(pc, null);
            Selection.activeGameObject = go;
        }

        [MenuItem("Tools/Palette Preview/Clear")]
        public static void Clear()
        {
            var existing = GameObject.Find(PreviewName);
            if (existing != null)
                Object.DestroyImmediate(existing);
            RestoreLegacy();
        }

        static void HideLegacy()
        {
            disabledLegacy.Clear();
            foreach (var name in new[] { "PalettePanel", "EventSystem" })
            {
                var o = GameObject.Find(name);
                if (o != null && o.activeSelf)
                {
                    o.SetActive(false);
                    disabledLegacy.Add(o);
                }
            }
        }

        static void RestoreLegacy()
        {
            foreach (var o in disabledLegacy)
                if (o != null)
                    o.SetActive(true);
            disabledLegacy.Clear();
        }
    }
}
#endif
