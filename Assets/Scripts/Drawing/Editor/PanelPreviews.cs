#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MixedRealityProject.Drawing.EditorTools
{
    /// <summary>
    /// Renderizza a PNG i pannelli della UI (palette principale, Options, Shortcuts) in TUTTE le
    /// lingue, in EDIT mode (niente Play) e senza lasciare nulla in scena. Per ogni lingua di
    /// Localization.Languages costruisce ciascun pannello con un PaletteController usa-e-getta,
    /// lo inquadra automaticamente sui suoi bounds e salva l'immagine in Assets/Drawing/Previews.
    /// Menu: Tools/Drawing/Preview All Panels.
    /// </summary>
    public static class PanelPreviews
    {
        public const string OutDir = "Assets/Drawing/Previews";
        const int Width = 1150;
        const int Height = 980;

        [MenuItem("Tools/Drawing/Preview All Panels")]
        public static void PreviewAll()
        {
            Directory.CreateDirectory(OutDir);

            // Settare Localization.Current scrive in PlayerPrefs: salvo la lingua scelta dall'utente
            // e la ripristino nel finally, così il tool non gliela cambia.
            string savedLang = Localization.Current;
            try
            {
                PaletteController.DebugAnchorGrid = false;
                foreach (var code in Localization.Languages)
                {
                    Localization.Current = code;
                    Capture("palette",   code, pc => pc.EditorBuildMainPanel());
                    Capture("options",   code, pc => pc.EditorBuildOptionsPanel());
                    Capture("shortcuts", code, pc => pc.EditorBuildShortcutsPanel());
                }
                Debug.Log($"[PanelPreviews] Anteprime salvate in {OutDir} " +
                          $"({Localization.Languages.Count} lingue × 3 pannelli).");
            }
            finally
            {
                Localization.Current = savedLang;
                PaletteController.DebugAnchorGrid = false;
                AssetDatabase.Refresh();
            }
        }

        // Costruisce UN pannello con un PaletteController usa-e-getta su un root temporaneo, lo
        // renderizza e distrugge tutto (camera inclusa, figlia di root → niente orfani in scena).
        static void Capture(string name, string langCode, System.Func<PaletteController, GameObject> build)
        {
            var root = new GameObject("PanelPreviewRoot");
            try
            {
                var pc = root.AddComponent<PaletteController>();
                var panel = build(pc);
                if (panel == null)
                {
                    Debug.LogError($"[PanelPreviews] Pannello '{name}' non costruito.");
                    return;
                }
                CapturePanel(root.transform, panel, $"{OutDir}/{name}-{langCode}.png", Width, Height);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Render offscreen URP di un pannello già costruito. La camera ortografica è figlia di
        /// <paramref name="root"/> (così il cleanup del root la elimina e non resta un orfano col
        /// background grigio) e viene inquadrata automaticamente sui Renderer.bounds del pannello,
        /// con un po' di padding: così palette (con strisce laterali), Options e Shortcuts sono
        /// inquadrati ciascuno alla sua misura, senza tarature manuali. Salva un PNG.
        /// </summary>
        public static void CapturePanel(Transform root, GameObject panel, string outFile, int width, int height)
        {
            var renderers = panel.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogError("[PanelPreviews] Nessun Renderer da inquadrare: " + outFile);
                return;
            }

            // Bounds combinati (world space; il pannello è costruito attorno all'origine).
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            const float pad = 1.08f; // ~8% d'aria attorno al pannello
            float aspect = (float)width / height;
            float orthoSize = Mathf.Max(bounds.extents.y * pad, bounds.extents.x * pad / aspect);

            // Il pannello guarda verso -Z: camera davanti (lato -Z) che guarda +Z.
            var camGO = new GameObject("PreviewCam");
            camGO.transform.SetParent(root, false);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.30f, 0.33f, 1f); // grigio neutro per il pannello scuro
            cam.cullingMask = 1 << PaletteController.PaletteLayer;
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 1f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 5f;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
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
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            File.WriteAllBytes(outFile, tex.EncodeToPNG());

            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
#endif
