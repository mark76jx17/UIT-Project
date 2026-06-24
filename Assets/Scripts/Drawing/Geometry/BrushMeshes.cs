using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Mesh condivise e a basso conteggio di vertici, generate una sola volta e
    /// riusate da tutti i tratti. La sfera primitiva di Unity (CreatePrimitive)
    /// ha ~515 vertici: usata per i due cap di ogni tratto e per ogni "punto"
    /// raddoppia il costo geometrico del disegno. Qui generiamo una UV-sphere a
    /// basso poly (~60 vertici) condivisa da tutti i cap/punti: 8-10× di vertici
    /// in meno e una sola mesh in memoria invece di una per oggetto.
    /// Raggio 0.5 (diametro 1), come la primitiva Unity, così la matematica di
    /// scala esistente (localScale = raggio × k) resta valida.
    /// </summary>
    public static class BrushMeshes
    {
        static Mesh sphere;

        /// <summary>Sfera low-poly condivisa, raggio 0.5 (diametro 1). Non modificarla.</summary>
        public static Mesh Sphere()
        {
            // Il null-check copre il domain reload dell'editor (la mesh statica
            // può essere distrutta tra una sessione di Play e l'altra).
            if (sphere == null)
                sphere = BuildUVSphere(segments: 8, rings: 6, radius: 0.5f);
            return sphere;
        }

        static Mesh BuildUVSphere(int segments, int rings, float radius)
        {
            // rings = anelli orizzontali tra i due poli; segments = spicchi.
            int vertCount = (rings + 1) * (segments + 1);
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];

            int v = 0;
            for (int y = 0; y <= rings; y++)
            {
                float phi = Mathf.PI * y / rings;          // 0..π (polo a polo)
                float sinPhi = Mathf.Sin(phi), cosPhi = Mathf.Cos(phi);
                for (int x = 0; x <= segments; x++)
                {
                    float theta = 2f * Mathf.PI * x / segments;
                    var dir = new Vector3(sinPhi * Mathf.Cos(theta), cosPhi, sinPhi * Mathf.Sin(theta));
                    vertices[v] = dir * radius;
                    normals[v] = dir;
                    v++;
                }
            }

            var triangles = new int[rings * segments * 6];
            int t = 0;
            for (int y = 0; y < rings; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i0 = y * (segments + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + (segments + 1);
                    int i3 = i2 + 1;
                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            var mesh = new Mesh { name = "BrushSphereLowPoly" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
