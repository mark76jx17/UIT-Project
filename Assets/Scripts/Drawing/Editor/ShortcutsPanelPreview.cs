#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MixedRealityProject.Drawing.EditorTools
{
    /// <summary>
    /// Costruisce il pannello scorciatoie in EDIT mode (niente Play) e lo renderizza a PNG,
    /// così si può vedere e iterare il layout senza indossare il visore. Usa l'entry-point
    /// editor PaletteController.EditorBuildShortcutsPanel e un render offscreen URP.
    /// Menu: Tools/Drawing/Preview Shortcuts Panel.
    /// </summary>
    public static class ShortcutsPanelPreview
    {
        const string OutDir = "Assets/Drawing/Previews";
        const string OutFile = OutDir + "/shortcuts.png";
        const int Width = 1150;
        const int Height = 980;
        const float OrthoSize = 0.30f;

        [MenuItem("Tools/Drawing/Preview Shortcuts Panel")]
        public static void Preview()
        {
            Directory.CreateDirectory(OutDir);

            var root = new GameObject("ShortcutsPreviewRoot");
            try
            {
                PaletteController.DebugAnchorGrid = true; // metti false per il render finale
                var pc = root.AddComponent<PaletteController>();
                var panel = pc.EditorBuildShortcutsPanel();
                if (panel == null)
                {
                    Debug.LogError("[ShortcutsPanelPreview] Pannello non costruito.");
                    return;
                }

                // Il pannello è centrato sull'origine e rivolto verso -Z: camera dal lato -Z.
                var camGO = new GameObject("PreviewCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.30f, 0.30f, 0.33f, 1f); // grigio neutro per leggere il pannello scuro
                cam.cullingMask = 1 << PaletteController.PaletteLayer;
                cam.orthographic = true;
                cam.orthographicSize = OrthoSize;
                cam.transform.position = new Vector3(0f, 0f, -1f);
                cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 5f;

                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    RenderPipeline.SubmitRenderRequest(cam, request);
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                File.WriteAllBytes(OutFile, tex.EncodeToPNG());

                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
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
