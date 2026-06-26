#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MixedRealityProject.Drawing.EditorTools
{
    /// <summary>
    /// Converte lo schema line-art dei controller (PNG nero-su-bianco nella root del progetto)
    /// in linee CHIARE su sfondo TRASPARENTE, così si legge bene sul pannello scuro. L'alpha
    /// diventa (1 - luminanza): il nero del tratto → opaco, il bianco dello sfondo → trasparente.
    /// Output in Resources, caricato a runtime dal pannello scorciatoie.
    /// Menu: Tools/Drawing/Import Controller Schematic.
    /// </summary>
    public static class SchematicImporter
    {
        const string SrcName = "668fb221-f3ac-469d-a305-f0ee61451adc-cover.png";
        const string OutPath = "Assets/Drawing/Resources/Controllers/schematic.png";

        [MenuItem("Tools/Drawing/Import Controller Schematic")]
        public static void Import()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string src = Path.Combine(root, SrcName);
            if (!File.Exists(src))
            {
                Debug.LogError("[SchematicImporter] Sorgente non trovata: " + src);
                return;
            }

            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(src));
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                float lum = (px[i].r + px[i].g + px[i].b) / 3f;
                // "Inchiostro" = pixel scuro E presente (alpha sorgente). Funziona sia con sfondo
                // bianco opaco (alpha=1, lum=1→0) sia con sfondo trasparente (alphaSrc=0→0).
                float a = px[i].a * (1f - lum);
                px[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(a)); // tratto → bianco, resto → trasparente
            }
            var outTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            outTex.SetPixels(px);
            outTex.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllBytes(OutPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(outTex);
            AssetDatabase.Refresh();
            Debug.Log($"[SchematicImporter] Salvato {OutPath} ({w}x{h})");
        }
    }
}
#endif
