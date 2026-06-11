using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Genera e aggiorna una mesh cilindrica estrusa lungo la spline in tempo reale.
/// Richiede SplineContainer e MeshFilter/MeshRenderer sullo stesso oggetto.
/// </summary>
[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SplineExtruder : MonoBehaviour
{
    [Header("Tube Settings")]
    [Tooltip("Raggio del tubo estruso")]
    [Range(0.01f, 0.5f)]
    public float tubeRadius = 0.05f;

    [Tooltip("Numero di segmenti circolari del tubo (più alto = più morbido)")]
    [Range(4, 24)]
    public int tubeSides = 8;

    [Tooltip("Numero di campionamenti lungo la spline")]
    [Range(4, 128)]
    public int resolution = 32;

    [Tooltip("Aggiorna la mesh anche durante il tracciamento")]
    public bool liveUpdate = true;

    // Riferimenti interni
    private SplineContainer _splineContainer;
    private MeshFilter _meshFilter;
    private Mesh _mesh;

    void Awake()
    {
        _splineContainer = GetComponent<SplineContainer>();
        _meshFilter = GetComponent<MeshFilter>();

        _mesh = new Mesh();
        _mesh.name = "SplineTube";
        _meshFilter.mesh = _mesh;
    }

    void Update()
    {
        if (liveUpdate && _splineContainer.Spline != null && _splineContainer.Spline.Count >= 2)
        {
            GenerateTubeMesh();
        }
    }

    /// <summary>Genera la mesh tubo lungo la spline.</summary>
    public void GenerateTubeMesh()
    {
        Spline spline = _splineContainer.Spline;
        if (spline == null || spline.Count < 2) return;

        int rings = resolution + 1;
        int sides = tubeSides;

        Vector3[] vertices = new Vector3[rings * sides];
        Vector3[] normals  = new Vector3[rings * sides];
        Vector2[] uvs      = new Vector2[rings * sides];
        int[]     tris     = new int[resolution * sides * 6];

        // --- Campionamento della spline ---
        for (int i = 0; i < rings; i++)
        {
            float t = (float)i / resolution;

            // Posizione e tangente sulla spline (spazio locale)
            spline.Evaluate(t, out float3 pos, out float3 tangent, out float3 upVec);

            // Costruisce un frame di Frenet-Serret per orientare l'anello
            Vector3 forward = math.normalizesafe(tangent);
            if (forward == Vector3.zero) forward = Vector3.forward;

            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f)
                up = Vector3.right;

            Vector3 right = Vector3.Cross(forward, up).normalized;
            up = Vector3.Cross(right, forward).normalized;

            // --- Anello di vertici ---
            for (int s = 0; s < sides; s++)
            {
                float angle = (float)s / sides * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                Vector3 offset = (right * cos + up * sin) * tubeRadius;
                int idx = i * sides + s;

                vertices[idx] = (Vector3)(float3)pos + offset;
                normals[idx]  = offset.normalized;
                uvs[idx]      = new Vector2((float)s / sides, t);
            }
        }

        // --- Triangoli ---
        int triIdx = 0;
        for (int i = 0; i < resolution; i++)
        {
            for (int s = 0; s < sides; s++)
            {
                int curr = i * sides + s;
                int next = i * sides + (s + 1) % sides;
                int currNext = (i + 1) * sides + s;
                int nextNext = (i + 1) * sides + (s + 1) % sides;

                tris[triIdx++] = curr;
                tris[triIdx++] = currNext;
                tris[triIdx++] = next;

                tris[triIdx++] = next;
                tris[triIdx++] = currNext;
                tris[triIdx++] = nextNext;
            }
        }

        // --- Applica alla mesh ---
        _mesh.Clear();
        _mesh.vertices  = vertices;
        _mesh.normals   = normals;
        _mesh.uv        = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();
    }
}