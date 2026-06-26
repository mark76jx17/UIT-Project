#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MixedRealityProject.Drawing.EditorTools
{
    /// <summary>
    /// Renderizza i modelli 3D reali dei controller Meta Quest Touch Pro (FBX del pacchetto
    /// com.meta.xr.sdk.core) in PNG con sfondo trasparente, da usare come "immagini reali"
    /// nel pannello scorciatoie. Gira in EDIT mode (niente Play): usa SubmitRenderRequest di
    /// URP per un render offscreen.
    ///
    /// I PNG finiscono in una cartella Resources così il runtime li carica con Resources.Load.
    /// I parametri di posa/inquadratura sono campi statici: per iterare basta cambiarli e
    /// rilanciare il menu "Tools/Drawing/Bake Controller Images".
    /// </summary>
    public static class ShortcutControllerBaker
    {
        const string CorePkg = "Packages/com.meta.xr.sdk.core/Meshes/MetaQuestTouchPro/";
        const string OutDir = "Assets/Drawing/Resources/Controllers";
        const int Size = 1024;
        const int BakeLayer = 31;

        // Posa/inquadratura (iterabili). Vista dall'alto-davanti per mostrare bene la faccia
        // superiore (stick + tasti); per la mano sinistra la X della direzione viene specchiata
        // così i due controller appaiono come una coppia simmetrica.
        static readonly Vector3 ModelEuler = new(0f, 0f, 0f);
        static readonly Vector3 ViewDir = new(0f, 0.72f, -0.7f); // dall'alto sulla faccia: tasti ben visibili
        const float FillPadding = 0.85f; // più basso = controller più grande nel frame (meno aria)

        [MenuItem("Tools/Drawing/Bake Controller Images")]
        public static void Bake()
        {
            Directory.CreateDirectory(OutDir);
            BakeOne(CorePkg + "MetaQuestTouchPro_Left.fbx", "touch-left", mirror: true);
            BakeOne(CorePkg + "MetaQuestTouchPro_Right.fbx", "touch-right", mirror: false);
            AssetDatabase.Refresh();
            Debug.Log("[ShortcutControllerBaker] Render salvati in " + OutDir);
        }

        static void BakeOne(string fbxPath, string outName, bool mirror)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (prefab == null)
            {
                Debug.LogError("[ShortcutControllerBaker] FBX non trovato: " + fbxPath);
                return;
            }

            var root = new GameObject("BakeRoot");
            try
            {
                var model = Object.Instantiate(prefab, root.transform);
                model.transform.localRotation = Quaternion.Euler(ModelEuler);
                SetLayer(root, BakeLayer);

                // Set di 3 luci (key + fill + rim) per dare forma alla plastica nera e uno
                // stacco luminoso sul bordo: la silhouette piatta diventa un "product shot".
                AddLight(root, "Key", new Vector3(40f, -20f, 0f), 1.6f, new Color(1f, 0.98f, 0.95f));
                AddLight(root, "Fill", new Vector3(15f, 150f, 0f), 0.6f, new Color(0.8f, 0.85f, 1f));
                AddLight(root, "Rim", new Vector3(-35f, 200f, 0f), 2.0f, new Color(0.85f, 0.9f, 1f));
                var prevAmbient = RenderSettings.ambientLight;
                var prevAmbientMode = RenderSettings.ambientMode;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.48f);

                // Inquadratura: bounds combinati dei renderer del modello.
                if (!TryGetBounds(model, out var bounds))
                {
                    Debug.LogError("[ShortcutControllerBaker] Nessun renderer in " + fbxPath);
                    return;
                }

                var camGO = new GameObject("BakeCam");
                camGO.transform.SetParent(root.transform);
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << BakeLayer;
                // Ortografica: look "product" pulito, niente prospettiva che gonfia l'impugnatura.
                cam.orthographic = true;

                float radius = bounds.extents.magnitude * FillPadding;
                cam.orthographicSize = radius;
                Vector3 dir = ViewDir;
                if (mirror) dir.x = -dir.x;
                dir = dir.normalized;
                float dist = radius * 3f;
                cam.transform.position = bounds.center - dir * dist;
                cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = dist + radius * 3f;

                var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 8
                };

                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    RenderPipeline.SubmitRenderRequest(cam, request);
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                File.WriteAllBytes(OutDir + "/" + outName + ".png", tex.EncodeToPNG());

                RenderSettings.ambientLight = prevAmbient;
                RenderSettings.ambientMode = prevAmbientMode;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void AddLight(GameObject parent, string name, Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject("BakeLight_" + name);
            go.transform.SetParent(parent.transform);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(euler);
        }

        static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            bounds = default;
            if (rends.Length == 0)
                return false;
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                bounds.Encapsulate(rends[i].bounds);
            return true;
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform)
                SetLayer(c.gameObject, layer);
        }
    }
}
#endif
