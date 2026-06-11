using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manager principale della demo Gravity Sketch.
/// - Click sinistro: disegna
/// - Click destro: cancella l'ultimo tracciato
/// - Tasto C: cancella tutto
/// - Tasto Z: undo ultimo stroke
/// - Rotella mouse: cambia profondità di disegno
/// </summary>
public class GravitySketchManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Prefab che contiene SplineDrawer + SplineExtruder + MeshRenderer")]
    public GameObject splineStrokePrefab;

    [Header("Drawing")]
    public float drawDepth = 5f;
    public float depthScrollSpeed = 0.5f;

    [Header("Appearance")]
    public Material strokeMaterial;
    [Range(0.01f, 0.3f)]
    public float strokeRadius = 0.04f;

    // Stato
    private SplineDrawer _activeDrawer;
    private SplineExtruder _activeExtruder;
    private List<GameObject> _strokes = new List<GameObject>();

    void Update()
    {
        HandleDepthScroll();
        HandleDrawInput();
        HandleUndoAndClear();
    }

    // -----------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------
    private void HandleDrawInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            StartNewStroke();
        }
        else if (Input.GetMouseButton(0) && _activeDrawer != null)
        {
            Vector3 wp = _activeDrawer.GetWorldPositionFromMouse();
            _activeDrawer.ContinueSpline(wp);
        }
        else if (Input.GetMouseButtonUp(0) && _activeDrawer != null)
        {
            Vector3 wp = _activeDrawer.GetWorldPositionFromMouse();
            _activeDrawer.EndSpline(wp);

            // Se la spline ha troppo pochi knot, distrugge lo stroke
            if (_activeDrawer.KnotCount < 2)
            {
                Destroy(_strokes[_strokes.Count - 1]);
                _strokes.RemoveAt(_strokes.Count - 1);
            }

            _activeDrawer    = null;
            _activeExtruder  = null;
        }
    }

    private void HandleUndoAndClear()
    {
        // Undo: tasto Z
        if (Input.GetKeyDown(KeyCode.Z) || Input.GetMouseButtonDown(1))
        {
            UndoLastStroke();
        }

        // Clear all: tasto C
        if (Input.GetKeyDown(KeyCode.C))
        {
            ClearAll();
        }
    }

    private void HandleDepthScroll()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            drawDepth = Mathf.Clamp(drawDepth + scroll * depthScrollSpeed * 10f, 1f, 30f);

            if (_activeDrawer != null)
                _activeDrawer.drawDepth = drawDepth;
        }
    }

    // -----------------------------------------------------------------
    // Stroke management
    // -----------------------------------------------------------------
    private void StartNewStroke()
    {
        if (splineStrokePrefab == null)
        {
            Debug.LogError("[GravitySketch] splineStrokePrefab non assegnato!");
            return;
        }

        GameObject strokeObj = Instantiate(splineStrokePrefab);
        strokeObj.name = $"Stroke_{_strokes.Count}";
        _strokes.Add(strokeObj);

        _activeDrawer   = strokeObj.GetComponent<SplineDrawer>();
        _activeExtruder = strokeObj.GetComponent<SplineExtruder>();

        if (_activeDrawer == null || _activeExtruder == null)
        {
            Debug.LogError("[GravitySketch] Il prefab deve avere SplineDrawer e SplineExtruder!");
            return;
        }

        // Applica impostazioni runtime
        _activeDrawer.drawDepth = drawDepth;
        _activeExtruder.tubeRadius = strokeRadius;

        if (strokeMaterial != null)
            strokeObj.GetComponent<MeshRenderer>().material = strokeMaterial;

        Vector3 startPos = _activeDrawer.GetWorldPositionFromMouse();
        _activeDrawer.BeginSpline(startPos);
    }

    private void UndoLastStroke()
    {
        if (_strokes.Count == 0) return;

        GameObject last = _strokes[_strokes.Count - 1];
        _strokes.RemoveAt(_strokes.Count - 1);
        Destroy(last);
    }

    private void ClearAll()
    {
        foreach (var s in _strokes)
            Destroy(s);
        _strokes.Clear();
        _activeDrawer   = null;
        _activeExtruder = null;
    }
}