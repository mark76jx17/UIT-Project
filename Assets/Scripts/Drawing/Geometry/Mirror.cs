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

        public static Vector3 Reflect(Vector3 point) =>
            point - 2f * Vector3.Dot(point - planePoint, planeNormal) * planeNormal;

        static GameObject BuildVisual()
        {
            var go = new GameObject("MirrorPlane");
            go.transform.position = planePoint;
            go.transform.rotation = Quaternion.LookRotation(planeNormal);

            var tint = new Color(0.55f, 0.45f, 0.95f, 0.10f);
            for (int side = 0; side < 2; side++)
            {
                var face = new GameObject("Face");
                face.transform.SetParent(go.transform, false);
                face.transform.localRotation = Quaternion.Euler(0f, side * 180f, 0f);
                face.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(0.8f, 1.0f, 0.05f);
                face.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(tint);
            }
            return go;
        }
    }
}
