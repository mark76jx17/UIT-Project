using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Traccia una spline nello spazio 3D seguendo il cursore del mouse.
/// Aggiunge knot ogni volta che il mouse si muove oltre la soglia minima.
/// </summary>
[RequireComponent(typeof(SplineContainer))]
public class SplineDrawer : MonoBehaviour
{
    [Header("Drawing Settings")]
    [Tooltip("Distanza minima tra due knot consecutivi (world units)")]
    public float minKnotDistance = 0.1f;

    [Tooltip("Distanza dal piano di disegno rispetto alla camera")]
    public float drawDepth = 5f;

    [Tooltip("Lisciatura della spline (Catmull-Rom = auto-smooth)")]
    public bool autoSmooth = true;

    // Riferimenti interni
    private SplineContainer _splineContainer;
    private Spline _spline;
    private Camera _camera;
    private Vector3 _lastKnotPosition;
    private bool _isDrawing = false;
    private List<float3> _points = new List<float3>();

    public bool IsDrawing => _isDrawing;
    public int KnotCount => _spline?.Count ?? 0;

    void Awake()
    {
        _splineContainer = GetComponent<SplineContainer>();
        _camera = Camera.main;
    }

    /// <summary>Inizia una nuova spline dal punto fornito.</summary>
    public void BeginSpline(Vector3 worldPosition)
    {
        _points.Clear();

        // Ricrea la spline pulita
        _spline = new Spline();
        _splineContainer.Spline = _spline;

        AddKnot(worldPosition);
        _lastKnotPosition = worldPosition;
        _isDrawing = true;
    }

    /// <summary>Aggiunge un knot se la distanza minima è rispettata.</summary>
    public void ContinueSpline(Vector3 worldPosition)
    {
        if (!_isDrawing) return;

        float dist = Vector3.Distance(worldPosition, _lastKnotPosition);
        if (dist >= minKnotDistance)
        {
            AddKnot(worldPosition);
            _lastKnotPosition = worldPosition;
        }
    }

    /// <summary>Termina il tracciamento.</summary>
    public void EndSpline(Vector3 worldPosition)
    {
        if (!_isDrawing) return;
        ContinueSpline(worldPosition); // aggiungi l'ultimo punto
        _isDrawing = false;
    }

    private void AddKnot(Vector3 position)
    {
        float3 pos = new float3(position.x, position.y, position.z);
        _points.Add(pos);

        BezierKnot knot = new BezierKnot(pos);
        _spline.Add(knot, autoSmooth ? TangentMode.AutoSmooth : TangentMode.Broken);
    }

    /// <summary>Restituisce la posizione nel mondo dal mouse usando un piano virtuale.</summary>
    public Vector3 GetWorldPositionFromMouse()
    {
        Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
        // Piano virtuale perpendicolare alla camera a distanza drawDepth
        Plane drawPlane = new Plane(-_camera.transform.forward, 
                                     _camera.transform.position + _camera.transform.forward * drawDepth);

        if (drawPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }
        return Vector3.zero;
    }

    /// <summary>Cancella la spline corrente.</summary>
    public void ClearSpline()
    {
        _spline?.Clear();
        _points.Clear();
        _isDrawing = false;
    }
}