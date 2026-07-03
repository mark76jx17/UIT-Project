using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Il "foglio a quadretti" 3D: un piano verticale con celle da 5 cm e righe maggiori
    /// ogni 4 (come un foglio da disegno quadrettato), posizionabile nella stanza con lo
    /// stesso grab della palette (striscia bianca sul bordo + grip). Da solo è un puro
    /// riferimento visivo; con Line attiva (StrokeSettings.SnapAxis) diventa piano di
    /// disegno: BrushController proietta gli estremi della linea sul foglio via
    /// ReferenceGrid.TryProject, e un marker mostra la "punta della matita sulla carta".
    /// Creato/distrutto da ReferenceGrid (toggle Grid della palette).
    /// </summary>
    public class GridSheet : MonoBehaviour
    {
        // Foglio: dimensioni e quadretti.
        const float SheetW = 1.0f, SheetH = 0.7f;
        const float Cell = 0.05f;            // quadretto da 5 cm
        const int MajorEvery = 4;            // riga maggiore ogni 4 celle (20 cm)
        const float MinorThick = 0.0015f, MajorThick = 0.0025f;
        static readonly Color MinorColor = new(0.55f, 0.45f, 0.95f, 0.22f);
        static readonly Color MajorColor = new(0.55f, 0.45f, 0.95f, 0.45f);
        const float SpawnDistance = 0.6f;    // davanti alla testa all'attivazione

        // Grab: stessi parametri e stessa affordance della palette.
        const float HighlightRange = 0.22f, GrabRange = 0.09f;
        const float GripPress = 0.55f, GripRelease = 0.35f;
        const float HlThick = 0.012f, HlWindow = 0.16f;
        const int HlWindowSegs = 28;
        const float SheetCorner = 0.02f;     // raggio degli angoli per la striscia

        // Proiezione (piano di disegno con Grid + Line).
        const float ProjectRange = 0.10f;    // distanza dal piano entro cui il tratto si appoggia
        const float ProjectMargin = 0.02f;   // tolleranza oltre il bordo del foglio
        const float ProjectOffset = 0.002f;  // davanti alle righe, contro lo z-fighting

        BrushController brush;
        bool grabbing;
        bool brushNear;
        Vector3 grabLocalPos;                // posa del foglio nello spazio del controller all'aggancio
        Quaternion grabLocalRot;
        float hapticTimer;

        Renderer hlRibbon;
        Material hlMat;
        Mesh hlMesh;
        GrabRibbon hlGeom;
        Renderer marker;

        /// <summary>Crea il foglio davanti alla testa (verticale, ruotato verso l'utente).</summary>
        public static GridSheet Create(Transform reference)
        {
            var go = new GameObject("GridSheet");
            var fwd = reference != null ? reference.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
                fwd = Vector3.forward;
            fwd.Normalize();
            var origin = reference != null ? reference.position : Vector3.zero;
            go.transform.SetPositionAndRotation(
                origin + fwd * SpawnDistance + Vector3.up * -0.05f,
                Quaternion.LookRotation(fwd, Vector3.up));
            return go.AddComponent<GridSheet>();
        }

        void Awake()
        {
            BuildLines("MinorLines", major: false);
            BuildLines("MajorLines", major: true);
            BuildHighlight();
            BuildMarker();
            brush = FindFirstObjectByType<BrushController>();
        }

        void OnDestroy()
        {
            // Non lasciare la presa tratti/il disegno soppressi se il foglio sparisce.
            ReferenceGrid.SuppressBrushGrab = false;
            ReferenceGrid.IsGrabbing = false;
        }

        // ---- quadretti ----

        // Le righe sono QUAD sottili in una mesh (le MeshTopology.Lines sono hairline da
        // 1 px, quasi invisibili sul visore). Bordi del foglio sempre nel set "maggiori".
        void BuildLines(string name, bool major)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            float hw = SheetW * 0.5f, hh = SheetH * 0.5f;
            float t = (major ? MajorThick : MinorThick) * 0.5f;

            int cols = Mathf.RoundToInt(SheetW / Cell);
            int rows = Mathf.RoundToInt(SheetH / Cell);
            bool IsMajor(int i, int last) => i % MajorEvery == 0 || i == last;

            for (int i = 0; i <= cols; i++)
            {
                if (IsMajor(i, cols) != major) continue;
                float x = -hw + i * Cell;
                AddQuad(verts, tris, new Vector2(x - t, -hh), new Vector2(x + t, hh));
            }
            for (int j = 0; j <= rows; j++)
            {
                if (IsMajor(j, rows) != major) continue;
                float y = -hh + j * Cell;
                AddQuad(verts, tris, new Vector2(-hw, y - t), new Vector2(hw, y + t));
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().mesh = mesh;
            var mat = BrushMaterials.CreateUnlit(major ? MajorColor : MinorColor);
            mat.SetFloat("_Cull", 0f); // visibile da entrambi i lati (ci si gira attorno)
            go.AddComponent<MeshRenderer>().material = mat;
        }

        static void AddQuad(System.Collections.Generic.List<Vector3> verts,
            System.Collections.Generic.List<int> tris, Vector2 min, Vector2 max)
        {
            int b = verts.Count;
            verts.Add(new Vector3(min.x, min.y, 0f));
            verts.Add(new Vector3(min.x, max.y, 0f));
            verts.Add(new Vector3(max.x, max.y, 0f));
            verts.Add(new Vector3(max.x, min.y, 0f));
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        // ---- grab (stessa affordance della palette, stato semplice: fisso ↔ trascinato) ----

        void BuildHighlight()
        {
            hlGeom = new GrabRibbon(new Vector2(SheetW, SheetH), SheetCorner, HlThick, HlWindow, HlWindowSegs);
            var go = new GameObject("GrabHighlight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.003f);
            hlMesh = new Mesh { name = "GridGrabRibbon" };
            hlMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().mesh = hlMesh;
            hlMat = BrushMaterials.CreateUnlit(Color.white); // alpha pilotata per frame da _BaseColor
            hlMat.SetFloat("_Cull", 0f);
            hlRibbon = go.AddComponent<MeshRenderer>();
            hlRibbon.material = hlMat;
            hlRibbon.enabled = false;
        }

        void BuildMarker()
        {
            var go = new GameObject("PenMarker");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedDisc(0.012f, 24);
            var mat = BrushMaterials.CreateUnlit(new Color(1f, 1f, 1f, 0.9f));
            mat.SetFloat("_Cull", 0f);
            marker = go.AddComponent<MeshRenderer>();
            marker.material = mat;
            marker.enabled = false;
        }

        void Update()
        {
            if (hapticTimer > 0f)
            {
                OVRInput.SetControllerVibration(0.25f, 0.4f, StrokeSettings.BrushHand);
                hapticTimer -= Time.deltaTime;
                if (hapticTimer <= 0f)
                    OVRInput.SetControllerVibration(0f, 0f, StrokeSettings.BrushHand);
            }

            UpdateMarker();

            // Il grab gira solo su device (in editor/simulatore il foglio resta dove nasce).
            if (brush == null || !UnityEngine.XR.XRSettings.isDeviceActive)
            {
                ReferenceGrid.SuppressBrushGrab = false;
                HideHighlight();
                return;
            }

            // Priorità alla palette: se è a portata di grip (o già afferrata), il foglio
            // non aggancia e spegne la sua affordance — un solo bersaglio alla volta.
            if (PaletteController.SuppressBrushGrab && !grabbing)
            {
                ReferenceGrid.SuppressBrushGrab = false;
                HideHighlight();
                return;
            }

            var brushPos = brush.transform.position;
            float dist = DistanceToSheet(brushPos);
            bool inGrabRange = dist <= GrabRange;

            // Impulso leggero quando ENTRI nel raggio di presa (affordance "agganciabile").
            if (inGrabRange && !brushNear && !grabbing)
                Pulse(0.02f);
            brushNear = inGrabRange;

            float t = grabbing ? 1f : Mathf.InverseLerp(HighlightRange, GrabRange, dist);
            UpdateHighlight(brushPos, t, inGrabRange || grabbing);

            // Vicino o mentre trascini: la mano-pennello non afferra i tratti (GrabController).
            ReferenceGrid.SuppressBrushGrab = inGrabRange || grabbing;

            float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, StrokeSettings.BrushHand);
            if (grabbing)
            {
                if (grip <= GripRelease)
                {
                    grabbing = false;
                    ReferenceGrid.IsGrabbing = false;
                    Pulse(0.03f);
                }
            }
            else if (inGrabRange && grip >= GripPress)
            {
                grabbing = true;
                ReferenceGrid.IsGrabbing = true;
                var bt = brush.transform;
                grabLocalPos = bt.InverseTransformPoint(transform.position);
                grabLocalRot = Quaternion.Inverse(bt.rotation) * transform.rotation;
                Pulse(0.04f);
            }
        }

        void LateUpdate()
        {
            // Trascinato: segue la posa del controller-pennello (presa a una mano), dopo
            // che il tracking ha aggiornato le mani in Update.
            if (!grabbing || brush == null)
                return;
            var bt = brush.transform;
            transform.SetPositionAndRotation(
                bt.TransformPoint(grabLocalPos), bt.rotation * grabLocalRot);
        }

        // Distanza dal CONTORNO del foglio (la cornice), non dal rettangolo pieno: il grab
        // scatta solo al bordo esterno. Dentro il foglio la distanza cresce verso il centro,
        // così sopra la struttura disegnata il grip resta libero di afferrare i tratti.
        float DistanceToSheet(Vector3 worldPos)
        {
            float hw = SheetW * 0.5f, hh = SheetH * 0.5f;
            var lp = transform.InverseTransformPoint(worldPos);
            Vector3 onBorder;
            if (Mathf.Abs(lp.x) <= hw && Mathf.Abs(lp.y) <= hh)
            {
                // Dentro il rettangolo: punto più vicino sul lato meno distante.
                float toVertical = hw - Mathf.Abs(lp.x);   // distanza dal lato sinistro/destro
                float toHorizontal = hh - Mathf.Abs(lp.y); // distanza dal lato alto/basso
                onBorder = toVertical < toHorizontal
                    ? new Vector3(Mathf.Sign(lp.x) * hw, lp.y, 0f)
                    : new Vector3(lp.x, Mathf.Sign(lp.y) * hh, 0f);
            }
            else
            {
                // Fuori: il clamp cade già sul contorno.
                onBorder = new Vector3(Mathf.Clamp(lp.x, -hw, hw), Mathf.Clamp(lp.y, -hh, hh), 0f);
            }
            return Vector3.Distance(transform.TransformPoint(onBorder), worldPos);
        }

        void UpdateHighlight(Vector3 brushPos, float t, bool grabbable)
        {
            float a = Mathf.Clamp01(t) * (grabbable ? 1f : 0.65f);
            if (a <= 0.01f)
            {
                HideHighlight();
                return;
            }
            if (!hlRibbon.enabled)
                hlRibbon.enabled = true;
            var col = Color.white;
            col.a = a;
            hlMat.SetColor(Shader.PropertyToID("_BaseColor"), col);
            var lp = transform.InverseTransformPoint(brushPos);
            hlGeom.RebuildAt(new Vector2(lp.x, lp.y), hlMesh);
        }

        void HideHighlight()
        {
            if (hlRibbon != null && hlRibbon.enabled)
                hlRibbon.enabled = false;
        }

        void Pulse(float duration) => hapticTimer = Mathf.Max(hapticTimer, duration);

        // ---- piano di disegno (Grid + Line) ----

        /// <summary>
        /// Proietta un punto sul piano del foglio se è entro ProjectRange dal piano e dentro
        /// il rettangolo (con margine). Il risultato sta ProjectOffset davanti alle righe,
        /// dal lato in cui si trova la punta (il foglio si usa da entrambi i lati).
        /// </summary>
        public bool TryProject(Vector3 worldPos, out Vector3 projected)
        {
            projected = worldPos;
            var lp = transform.InverseTransformPoint(worldPos);
            if (Mathf.Abs(lp.z) > ProjectRange)
                return false;
            float hw = SheetW * 0.5f + ProjectMargin, hh = SheetH * 0.5f + ProjectMargin;
            if (Mathf.Abs(lp.x) > hw || Mathf.Abs(lp.y) > hh)
                return false;
            projected = ProjectClamped(worldPos);
            return true;
        }

        /// <summary>
        /// Proiezione SENZA vincolo di distanza, sempre clampata dentro il rettangolo:
        /// serve al latch del tratto (iniziato sulla carta, ci resta anche se la punta
        /// esce dal range a metà tratto).
        /// </summary>
        public Vector3 ProjectClamped(Vector3 worldPos)
        {
            var lp = transform.InverseTransformPoint(worldPos);
            float side = lp.z >= 0f ? 1f : -1f;
            return transform.TransformPoint(new Vector3(
                Mathf.Clamp(lp.x, -SheetW * 0.5f, SheetW * 0.5f),
                Mathf.Clamp(lp.y, -SheetH * 0.5f, SheetH * 0.5f),
                side * ProjectOffset));
        }

        // Marker "punta della matita sulla carta": visibile quando Line è attiva e la punta
        // è in range di proiezione — anche prima di premere il trigger (anteprima).
        void UpdateMarker()
        {
            bool show = false;
            if (StrokeSettings.SnapAxis && brush != null && brush.Tip != null
                && TryProject(brush.Tip.position, out var p))
            {
                marker.transform.position = p;
                marker.transform.rotation = transform.rotation;
                show = true;
            }
            if (marker.enabled != show)
                marker.enabled = show;
        }
    }
}
