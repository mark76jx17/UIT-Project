using System.IO;
using System.Text;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Esporta tutte le stroke disegnate nella scena come file OBJ standard.
    ///
    /// L'OBJ è un formato testuale ampiamente supportato (Blender, Maya, Cinema 4D,
    /// importatori Unity/Unreal): permette di portare le scene disegnate in SketchXR
    /// in qualsiasi pipeline 3D. Ogni DrawnItem viene esportato come oggetto OBJ
    /// separato con vertici, normali e triangoli. Le coordinate sono in world space.
    ///
    /// Utilizzo:
    ///   - In editor/simulatore: tasto O (vedi DesktopBrushSimulator).
    ///   - Da codice: DrawingExporter.ExportToFile(path).
    ///   - Il percorso di default è [persistentDataPath]/drawing.obj.
    /// </summary>
    public static class DrawingExporter
    {
        public static string DefaultPath =>
            Path.Combine(Application.persistentDataPath, "drawing.obj");

        /// <summary>
        /// Esporta tutte le stroke attive come stringa OBJ.
        /// I vertici sono in world space; i normali sono trasformati di conseguenza.
        /// </summary>
        public static string ExportToOBJ()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SketchXR — OBJ export");
            sb.AppendLine($"# Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            int vertexOffset = 1; // OBJ usa indici 1-based

            var items = Object.FindObjectsByType<DrawnItem>(FindObjectsSortMode.None);
            foreach (var item in items)
            {
                // Ogni DrawnItem può avere più MeshFilter (cap sferici, fill surface, ecc.)
                var filters = item.GetComponentsInChildren<MeshFilter>();
                foreach (var mf in filters)
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0) continue;

                    var t = mf.transform;
                    sb.AppendLine($"o {item.gameObject.name}_{mf.gameObject.name}");

                    // Vertici in world space
                    foreach (var v in mesh.vertices)
                    {
                        var w = t.TransformPoint(v);
                        sb.AppendLine($"v {-w.x:F6} {w.y:F6} {w.z:F6}"); // flip X per handedness OBJ
                    }

                    // Normali in world space
                    foreach (var n in mesh.normals)
                    {
                        var wn = t.TransformDirection(n).normalized;
                        sb.AppendLine($"vn {-wn.x:F6} {wn.y:F6} {wn.z:F6}");
                    }

                    // Facce (triangoli, con normale per vertice)
                    sb.AppendLine("g stroke");
                    var tris = mesh.triangles;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        // Flip dell'ordine dei vertici per compensare il flip X (winding order)
                        int a = tris[i]     + vertexOffset;
                        int b = tris[i + 2] + vertexOffset; // b e c invertiti
                        int c = tris[i + 1] + vertexOffset;
                        sb.AppendLine($"f {a}//{a} {b}//{b} {c}//{c}");
                    }
                    sb.AppendLine();

                    vertexOffset += mesh.vertexCount;
                }
            }

            return sb.ToString();
        }

        /// <summary>Salva il file OBJ sul percorso specificato.</summary>
        public static void ExportToFile(string path = null)
        {
            path ??= DefaultPath;
            var content = ExportToOBJ();
            File.WriteAllText(path, content, Encoding.UTF8);
            Debug.Log($"[DrawingExporter] OBJ salvato in: {path}");
        }
    }
}
