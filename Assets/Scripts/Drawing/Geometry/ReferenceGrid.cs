using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Griglia di riferimento a pavimento (stile Gravity Sketch): dà profondità e senso
    /// della scala quando si disegna in aria. Mesh a linee sul piano XZ, centrata sotto
    /// l'utente all'attivazione, semitrasparente. Si accende/spegne dal toggle "Grid".
    /// Posta sull'origine di tracking (y≈0); se il tracking non è a livello pavimento
    /// può comparire più in alto — regolabile da GridHeight.
    /// </summary>
    public static class ReferenceGrid
    {
        public static bool Enabled { get; private set; }

        const float HalfSize = 2.0f;   // griglia 4×4 m
        const float Spacing = 0.25f;   // celle da 25 cm
        const float GridHeight = 0.001f;

        static GameObject visual;

        public static void Toggle(Transform reference)
        {
            if (Enabled)
                Disable();
            else
                Enable(reference);
        }

        public static void Enable(Transform reference)
        {
            visual = Build(reference);
            Enabled = true;
        }

        public static void Disable()
        {
            if (visual != null)
                Object.Destroy(visual);
            Enabled = false;
        }

        static GameObject Build(Transform reference)
        {
            var go = new GameObject("ReferenceGrid");
            float cx = reference != null ? reference.position.x : 0f;
            float cz = reference != null ? reference.position.z : 0f;
            // Aggancia il centro alla cella più vicina sotto l'utente.
            go.transform.position = new Vector3(
                Mathf.Round(cx / Spacing) * Spacing, GridHeight, Mathf.Round(cz / Spacing) * Spacing);

            var verts = new List<Vector3>();
            var indices = new List<int>();
            int lines = Mathf.RoundToInt(HalfSize / Spacing);
            for (int i = -lines; i <= lines; i++)
            {
                float p = i * Spacing;
                AddLine(verts, indices, new Vector3(p, 0f, -HalfSize), new Vector3(p, 0f, HalfSize));
                AddLine(verts, indices, new Vector3(-HalfSize, 0f, p), new Vector3(HalfSize, 0f, p));
            }

            var mesh = new Mesh { name = "ReferenceGrid" };
            mesh.SetVertices(verts);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material =
                BrushMaterials.CreateUnlit(new Color(0.55f, 0.45f, 0.95f, 0.30f));
            return go;
        }

        static void AddLine(List<Vector3> verts, List<int> indices, Vector3 a, Vector3 b)
        {
            indices.Add(verts.Count); verts.Add(a);
            indices.Add(verts.Count); verts.Add(b);
        }
    }
}
