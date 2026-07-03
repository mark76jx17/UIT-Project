using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Simmetria speculare, stile Gravity Sketch: quando è attiva, ogni tratto
    /// disegnato genera in tempo reale il gemello riflesso rispetto a un piano
    /// verticale. Il piano viene piazzato al momento dell'attivazione, mezzo
    /// metro davanti a chi guarda, ed è visualizzato come lastra
    /// semitrasparente (due facce).
    /// </summary>
    public static class Mirror
    {
        public static bool Enabled { get; private set; }

        static Vector3 planePoint;
        static Vector3 planeNormal;
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
            // Piano verticale davanti all'osservatore, perpendicolare al suo
            // sguardo proiettato sull'orizzontale.
            var forward = reference.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
            planePoint = reference.position + forward * 0.5f;
            planeNormal = Vector3.Cross(Vector3.up, forward).normalized;

            visual = BuildVisual();
            Enabled = true;
        }

        public static void Disable()
        {
            if (visual != null)
                Object.Destroy(visual);
            Enabled = false;
        }

        // Riflette rispetto alla posa CORRENTE del piano: se lo sposti/ruoti col grip
        // (MirrorHandle), la simmetria segue. Fallback ai valori statici se il piano
        // non è ancora stato creato.
        public static Vector3 Reflect(Vector3 point)
        {
            Vector3 p = visual != null ? visual.transform.position : planePoint;
            Vector3 n = visual != null ? visual.transform.forward : planeNormal;
            return point - 2f * Vector3.Dot(point - p, n) * n;
        }

        public static Vector3 Project(Vector3 point)
        {
            Vector3 p = visual != null ? visual.transform.position : planePoint;
            Vector3 n = visual != null ? visual.transform.forward : planeNormal;
            return point - Vector3.Dot(point - p, n) * n;
        }

        public static bool TryProject(Vector3 point, float maxDistance, out Vector3 projected)
        {
            Vector3 p = visual != null ? visual.transform.position : planePoint;
            Vector3 n = visual != null ? visual.transform.forward : planeNormal;

            float d = Vector3.Dot(point - p, n);
            projected = point - d * n;
            return Mathf.Abs(d) <= maxDistance;
        }

        static GameObject BuildVisual()
        {
            var go = new GameObject("MirrorPlane");
            go.transform.position = planePoint;
            go.transform.rotation = Quaternion.LookRotation(planeNormal);

            // Afferrabile col grip ma non cancellabile/salvabile (vedi MirrorHandle).
            go.AddComponent<MirrorHandle>();
            var grab = go.AddComponent<BoxCollider>();
            grab.isTrigger = true;
            grab.size = new Vector3(0.8f, 1.0f, 0.03f);

            var glass = new Color(0.55f, 0.45f, 0.95f, 0.18f);
            var border = new Color(0.70f, 0.60f, 1f, 0.6f);
            for (int side = 0; side < 2; side++)
            {
                var face = new GameObject("Face");
                face.transform.SetParent(go.transform, false);
                face.transform.localRotation = Quaternion.Euler(0f, side * 180f, 0f);

                // Cornice luminosa leggermente più grande, dietro la lastra: fa "telaio".
                var frame = new GameObject("Frame");
                frame.transform.SetParent(face.transform, false);
                frame.transform.localPosition = new Vector3(0f, 0f, 0.001f);
                frame.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(0.84f, 1.04f, 0.06f);
                frame.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(border);

                // Lastra semitrasparente.
                var pane = new GameObject("Glass");
                pane.transform.SetParent(face.transform, false);
                pane.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(0.8f, 1.0f, 0.05f);
                pane.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(glass);
            }
            return go;
        }
    }
}
