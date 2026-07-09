using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Afferra e sposta gli oggetti disegnati col grip (pulsante laterale) del
    /// controller. Una mano: l'oggetto segue posizione e rotazione della mano.
    /// Due mani sullo stesso oggetto: lo si sposta, ruota e SCALA allargando o
    /// stringendo le mani (trasformazione di similitudine attorno al punto
    /// medio, vedi GrabSession). Hover e presa sono evidenziati schiarendo il
    /// colore dell'oggetto.
    /// </summary>
    public class GrabController : MonoBehaviour
    {
        [SerializeField] OVRInput.Controller controller = OVRInput.Controller.RTouch;
        [SerializeField] float grabRadius = 0.04f;
        [Tooltip("Unione magnete: tenendo un oggetto, un altro oggetto disegnato entro " +
                 "questo raggio dalla mano si evidenzia come candidato; rilasciando si fondono.")]
        [SerializeField] float mergeRadius = 0.06f;
        [Tooltip("Raggio della sonda sulla punta del pennello: più piccolo dell'area del " +
                 "controller, per selezioni precise puntando un tratto sottile.")]
        [SerializeField] float tipProbeRadius = 0.018f;
        [SerializeField] float pressThreshold = 0.55f;
        [SerializeField] float releaseThreshold = 0.35f;

        // Punta del pennello (impostata dal DrawingRig solo sulla mano del pennello):
        // sonda di selezione aggiuntiva, più precisa dell'area del controller. Null
        // sull'altra mano → solo l'area del controller, come prima.
        Transform tipProbe;
        public Transform TipProbe { set => tipProbe = value; }

        Transform holding;
        Transform hovered;
        Transform mergeTarget; // candidato all'unione mentre tieni un oggetto (magnete post-disegno)

        // Pennello sulla stessa mano (null sulla mano-palette): mentre disegna, hover e presa
        // vanno sospesi — la sonda sulla punta aggancerebbe a intermittenza i collider del
        // tratto IN CORSO, facendone sfarfallare il colore (highlight 1.2× on/off).
        BrushController brush;

        void Awake() => brush = GetComponent<BrushController>();

        // --- Sovrapposizione oggetto-oggetto (magnete di precisione) ---
        // Oltre alla sonda mano/punta, mentre tieni un oggetto si rileva anche la
        // SOVRAPPOSIZIONE dell'oggetto tenuto con un altro disegno: i suoi collider di
        // presa vengono esaminati a lotti (round-robin → costo costante per frame anche su
        // disegni ricchi) cercando contatti con i collider di altri DrawnItem. Basta che le
        // strutture si tocchino appena perché il bersaglio si evidenzi (viola); il rilascio
        // conferma l'unione. L'aggancio è immediato; lo sgancio avviene solo a fine ciclo
        // completo di scansione (isteresi anti-sfarfallio dell'evidenziazione).
        readonly List<Collider> heldColliders = new();
        int scanIndex;
        Transform scanFound;     // bersaglio visto nel ciclo di scansione corrente
        Transform overlapTarget; // esito dell'ultimo ciclo completo
        const int ScanPerFrame = 24;        // collider dell'oggetto tenuto esaminati per frame
        const float OverlapMargin = 0.004f; // tolleranza extra: la "piccola sovrapposizione"

        // Fotografa i collider dell'oggetto appena afferrato (non cambiano durante la presa).
        void BeginOverlapScan()
        {
            heldColliders.Clear();
            if (holding != null)
                holding.GetComponentsInChildren(false, heldColliders);
            scanIndex = 0;
            scanFound = null;
            overlapTarget = null;
        }

        void EndOverlapScan()
        {
            heldColliders.Clear();
            scanFound = null;
            overlapTarget = null;
        }

        // Avanza la scansione di un lotto e ritorna il disegno attualmente sovrapposto
        // all'oggetto tenuto (o null). Il primo contatto aggancia subito; il bersaglio si
        // sgancia solo quando un intero ciclo di scansione non trova più contatti.
        Transform ScanHeldOverlap()
        {
            int count = heldColliders.Count;
            if (count == 0)
                return null;
            int checks = Mathf.Min(ScanPerFrame, count);
            for (int n = 0; n < checks; n++)
            {
                var col = heldColliders[scanIndex];
                bool cycleEnd = ++scanIndex >= count;
                if (cycleEnd)
                    scanIndex = 0;

                if (col != null && col.enabled && col.gameObject.activeInHierarchy)
                {
                    var b = col.bounds;
                    float r = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) + OverlapMargin;
                    int hits = Physics.OverlapSphereNonAlloc(b.center, r, overlapBuffer,
                        Physics.AllLayers, QueryTriggerInteraction.Collide);
                    for (int i = 0; i < hits; i++)
                    {
                        var root = GrabRoot(overlapBuffer[i]);
                        if (root == null || root.GetComponent<DrawnItem>() == null || !IsMergeable(root))
                            continue;
                        scanFound = root;
                        overlapTarget = root; // aggancio immediato
                        break;
                    }
                }

                if (cycleEnd)
                {
                    overlapTarget = scanFound; // sgancio solo a ciclo completo senza contatti
                    scanFound = null;
                }
            }
            return overlapTarget;
        }

        // Haptic state
        float hapticTimer;
        float hapticFreq;
        float hapticAmp;

        public OVRInput.Controller Controller
        {
            set => controller = value;
        }

        void HapticPulse(float duration, float frequency = 0.5f, float amplitude = 0.6f)
        {
            hapticFreq = frequency;
            hapticAmp = amplitude;
            hapticTimer = duration;
        }

        void Update()
        {
            // Haptic timer
            if (hapticTimer > 0f)
            {
                OVRInput.SetControllerVibration(hapticFreq, hapticAmp, controller);
                hapticTimer -= Time.deltaTime;
                if (hapticTimer <= 0f)
                    OVRInput.SetControllerVibration(0f, 0f, controller);
            }

            // Vicino alla palette o al foglio a quadretti (o mentre li si trascina), o MENTRE
            // IL PENNELLO STA DISEGNANDO, la mano-pennello NON fa hover né afferra i tratti:
            // il grip è riservato allo spostamento del pannello, e durante il tratto l'hover
            // aggancerebbe a intermittenza il tratto in corso facendolo sfarfallare. Non
            // interrompe una presa già in corso (holding != null).
            if (controller == StrokeSettings.BrushHand
                && (PaletteController.SuppressBrushGrab || ReferenceGrid.SuppressBrushGrab
                    || (brush != null && brush.IsDrawing))
                && holding == null)
            {
                SetHover(null);
                return;
            }

            float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller);

            if (holding == null)
            {
                UpdateHover();
                if (grip >= pressThreshold && hovered != null)
                {
                    holding = hovered;
                    SetHover(null);
                    StrokeHighlight.Set(holding, 1.45f);
                    GrabSession.Add(holding, this);
                    BeginOverlapScan(); // magnete di precisione: scandisce le sovrapposizioni
                    // Impulso di conferma presa: breve e deciso
                    HapticPulse(0.05f, frequency: 0.6f, amplitude: 0.7f);
                }
            }
            else if (grip <= releaseThreshold || holding == null /* distrutto (gomma) */)
            {
                ReleaseHolding();
            }
            else if (controller == StrokeSettings.BrushHand
                     && OVRInput.GetDown(OVRInput.Button.Two, controller))
            {
                // B (mano del pennello) mentre afferri = duplica, stile
                // Gravity Sketch. Solo su quella mano: sull'altra il tasto
                // "Two" è già Save (vedi ControllerShortcuts).
                var copy = DrawingStore.Duplicate(holding);
                if (copy != null)
                {
                    copy.transform.position += transform.up * 0.03f;
                    StrokeHistory.Push(copy);
                }
            }
            else
            {
                // Tenendo l'oggetto: cerca un altro oggetto disegnato vicino e lo evidenzia
                // come candidato all'unione (magnete). Al rilascio i due si fondono.
                UpdateMergeCandidate();
            }
        }

        // Rilascio dell'oggetto tenuto. Se un candidato all'unione è evidenziato e l'oggetto
        // non è più tenuto da nessun'altra mano, i due diventano UN gruppo (l'oggetto tenuto
        // entra nella gerarchia del bersaglio, mantenendo la posa nel mondo). Annullabile.
        void ReleaseHolding()
        {
            GrabSession.Remove(this);

            bool merged = false;
            if (mergeTarget != null && holding != null
                && !GrabSession.IsHeld(holding)
                && mergeTarget.gameObject.activeInHierarchy
                && holding != mergeTarget
                && !mergeTarget.IsChildOf(holding) && !holding.IsChildOf(mergeTarget))
            {
                var child = holding;
                var oldParent = child.parent;
                child.SetParent(mergeTarget, true); // mantiene la posa nel mondo
                var item = child.GetComponent<DrawnItem>();
                if (item != null)
                    Destroy(item); // il "vero" oggetto ora è la radice del gruppo
                StrokeHistory.PushMerge(child, oldParent, mergeTarget);
                merged = true;
            }

            if (mergeTarget != null)
                StrokeHighlight.Clear(mergeTarget); // copre anche l'oggetto appena unito (ora figlio)
            if (holding != null)
                StrokeHighlight.Clear(holding);
            mergeTarget = null;
            holding = null;
            EndOverlapScan();

            // Unione: impulso deciso (conferma); rilascio semplice: impulso morbido.
            if (merged)
                HapticPulse(0.08f, frequency: 0.8f, amplitude: 0.8f);
            else
                HapticPulse(0.03f, frequency: 0.2f, amplitude: 0.3f);
        }

        // Mentre tieni un oggetto: trova un altro oggetto disegnato vicino alla mano O
        // sovrapposto (anche di poco) all'oggetto tenuto, e lo evidenzia col colore
        // "magnete". Si aggiorna solo al cambio di candidato; il rilascio conferma l'unione.
        void UpdateMergeCandidate()
        {
            Transform found = FindMergeCandidate();
            if (found == null)
                found = ScanHeldOverlap(); // magnete di precisione: sovrapposizione strutture
            if (found == mergeTarget)
                return;
            if (mergeTarget != null && !GrabSession.IsHeld(mergeTarget))
                StrokeHighlight.Clear(mergeTarget);
            mergeTarget = found;
            if (mergeTarget != null)
            {
                StrokeHighlight.SetMergeHover(mergeTarget);
                HapticPulse(0.02f, frequency: 0.5f, amplitude: 0.4f); // "qui si aggancia"
            }
        }

        // Oggetto disegnato (DrawnItem) su cui fondere ciò che si tiene, diverso da quello
        // tenuto e non imparentato con esso. Prima cerca per contatto sui collider dei tratti
        // (il contorno); se non trova nulla, ripiega sull'AREA RIEMPITA (vedi FillOwnerAt).
        // Lo specchio (MirrorHandle) è escluso: non si unisce.
        Transform FindMergeCandidate()
        {
            if (holding == null)
                return null;
            Vector3 probe = tipProbe != null ? tipProbe.position : transform.position;
            int count = Physics.OverlapSphereNonAlloc(probe, mergeRadius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var root = GrabRoot(overlapBuffer[i]);
                if (root != null && root.GetComponent<DrawnItem>() != null && IsMergeable(root))
                    return root; // solo oggetti disegnati (no specchio)
            }
            // Il riempimento non ha collider (vedi FillSurface), quindi l'OverlapSphere sopra
            // non lo trova: se la punta è dentro un'area colorata ci si aggancia comunque.
            return FillOwnerAt(probe);
        }

        // Disegno (DrawnItem) proprietario di un riempimento la cui area colorata contiene la
        // punta ed è entro mergeRadius dalla superficie; null altrimenti. Così si può fondere
        // portando un disegno sopra la parte fillata di un altro, non solo sul contorno.
        Transform FillOwnerAt(Vector3 probe)
        {
            foreach (var fill in FillRegion.CoveringFills(probe, holding.gameObject))
            {
                var renderer = fill.GetComponent<Renderer>();
                if (renderer != null && renderer.bounds.SqrDistance(probe) > mergeRadius * mergeRadius)
                    continue; // fill lontano (complanare): non agganciare
                var item = fill.GetComponentInParent<DrawnItem>();
                if (item != null && IsMergeable(item.transform))
                    return item.transform;
            }
            return null;
        }

        // Bersaglio valido per l'unione: non è l'oggetto tenuto né imparentato con esso.
        bool IsMergeable(Transform root) =>
            root != holding && !root.IsChildOf(holding) && !holding.IsChildOf(root);

        // L'oggetto afferrato viene mosso in LateUpdate, dopo che il tracking
        // ha aggiornato la posa delle mani in Update.
        void LateUpdate() => GrabSession.ApplyIfLeader(this);

        // Buffer riusato per l'hover: la variante NonAlloc evita l'allocazione di un
        // array a ogni frame su entrambe le mani.
        static readonly Collider[] overlapBuffer = new Collider[32];

        void UpdateHover()
        {
            // Prima la punta del pennello (sonda piccola, precisa): così puoi selezionare
            // un tratto sottile puntandolo. Se la punta non aggancia nulla (o non c'è, es.
            // mano palette), ripiega sull'area larga del controller, come da sempre.
            Transform found = null;
            if (tipProbe != null)
                found = ProbeAt(tipProbe.position, tipProbeRadius);
            if (found == null)
                found = ProbeAt(transform.position, grabRadius);
            SetHover(found);
        }

        Transform ProbeAt(Vector3 position, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(position, radius,
                overlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var t = GrabRoot(overlapBuffer[i]);
                if (t != null)
                    return t;
            }
            return null;
        }

        /// <summary>Radice afferrabile di un collider: un oggetto disegnato o il piano
        /// specchio (MirrorHandle). Gomma/magnete cercano solo DrawnItem, quindi
        /// ignorano lo specchio.</summary>
        public static Transform GrabRoot(Collider col)
        {
            var item = col.GetComponentInParent<DrawnItem>();
            if (item != null)
                return item.transform;
            var handle = col.GetComponentInParent<MirrorHandle>();
            return handle != null ? handle.transform : null;
        }

        void SetHover(Transform target)
        {
            if (hovered == target)
                return;
            // Non spegnere l'evidenziazione di un oggetto tenuto dall'altra mano.
            if (hovered != null && !GrabSession.IsHeld(hovered))
                StrokeHighlight.Clear(hovered);
            hovered = target;
            if (hovered != null && !GrabSession.IsHeld(hovered))
                StrokeHighlight.Set(hovered, 1.2f);
        }
    }

    /// <summary>
    /// Coordina le prese: una sessione per oggetto, con la lista delle mani che
    /// lo tengono. Non si fa reparenting (niente effetti collaterali su undo e
    /// gerarchia del magnete): la posa viene applicata ogni frame.
    ///
    /// Due mani: trasformazione di similitudine esatta attorno al punto medio.
    /// Con stato iniziale (a0, b0, posa0) e mani correnti (a, b):
    ///   s  = |b-a| / |b0-a0|             (scala)
    ///   R  = FromToRotation(b0-a0, b-a)  (rotazione)
    ///   pos = mid + R * (pos0 - mid0) * s
    /// così ogni punto dell'oggetto segue p' = mid + R*s*(p - mid0).
    /// </summary>
    static class GrabSession
    {
        class Session
        {
            public Transform target;
            public readonly List<GrabController> hands = new();

            // presa a una mano: posa dell'oggetto nello spazio della mano
            Vector3 localPos;
            Quaternion localRot;

            // presa a due mani: stato iniziale
            Vector3 a0, b0, pos0, scale0;
            Quaternion rot0;
            Quaternion frame0; // orientamento iniziale delle mani (per il roll)

            // Posa (mondo) all'inizio della presa: catturata quando la sessione nasce
            // (prima mano), per registrare l'intero spostamento nella history al rilascio
            // finale. Non si aggiorna nei passaggi due→una mano (Recapture), così l'undo
            // torna alla posa PRE-presa.
            Vector3 grabStartPos;
            Quaternion grabStartRot;
            Vector3 grabStartScale;

            public void CaptureGrabStart()
            {
                if (target == null)
                    return;
                grabStartPos = target.position;
                grabStartRot = target.rotation;
                grabStartScale = target.localScale;
            }

            // Registra lo spostamento nella history se la posa è cambiata rispetto
            // all'inizio della presa (oltre una piccola soglia anti-jitter). Un semplice
            // afferra-e-rilascia senza spostamento non consuma un passo di undo.
            public void PushTransformIfMoved()
            {
                if (target == null)
                    return;
                const float posEps = 1e-4f;   // 0.1 mm
                const float scaleEps = 1e-4f;
                bool moved = (target.position - grabStartPos).sqrMagnitude > posEps * posEps
                          || Quaternion.Angle(target.rotation, grabStartRot) > 0.1f
                          || (target.localScale - grabStartScale).sqrMagnitude > scaleEps * scaleEps;
                if (moved)
                    StrokeHistory.PushTransform(target, grabStartPos, grabStartRot, grabStartScale);
            }

            public void Recapture()
            {
                if (target == null)
                    return;
                if (hands.Count == 1)
                {
                    var hand = hands[0].transform;
                    localPos = hand.InverseTransformPoint(target.position);
                    localRot = Quaternion.Inverse(hand.rotation) * target.rotation;
                }
                else if (hands.Count >= 2)
                {
                    a0 = hands[0].transform.position;
                    b0 = hands[1].transform.position;
                    pos0 = target.position;
                    rot0 = target.rotation;
                    scale0 = target.localScale;
                    frame0 = HandFrame(a0, b0,
                        hands[0].transform.rotation, hands[1].transform.rotation);
                }
            }

            public void Apply()
            {
                if (target == null)
                    return;
                if (hands.Count == 1)
                {
                    var hand = hands[0].transform;
                    target.SetPositionAndRotation(
                        hand.TransformPoint(localPos), hand.rotation * localRot);
                }
                else if (hands.Count >= 2)
                {
                    var a = hands[0].transform.position;
                    var b = hands[1].transform.position;
                    float d0 = Vector3.Distance(a0, b0);
                    float scale = d0 > 1e-4f ? Vector3.Distance(a, b) / d0 : 1f;
                    scale = Mathf.Clamp(scale, 0.1f, 10f); // niente collasso a zero o esplosione
                    var mid0 = (a0 + b0) * 0.5f;
                    var mid = (a + b) * 0.5f;
                    // Rotazione completa (incluso il roll): differenza tra il frame
                    // corrente delle mani e quello iniziale. FromToRotation catturava
                    // solo lo swing, quindi torcendo i polsi l'oggetto non rollava.
                    var frame = HandFrame(a, b,
                        hands[0].transform.rotation, hands[1].transform.rotation);
                    var rotation = frame * Quaternion.Inverse(frame0);

                    target.localScale = scale0 * scale;
                    target.rotation = rotation * rot0;
                    target.position = mid + rotation * (pos0 - mid0) * scale;
                }
            }

            // Frame di orientamento definito dalle due mani: l'asse X è il vettore tra
            // le mani; il "su" è la media dell'alto dei due controller, così ruotando
            // entrambi i polsi attorno all'asse (roll) il frame ruota con loro.
            static Quaternion HandFrame(Vector3 a, Vector3 b, Quaternion rotA, Quaternion rotB)
            {
                Vector3 axis = b - a;
                axis = axis.sqrMagnitude < 1e-8f ? Vector3.right : axis.normalized;
                Vector3 up = (rotA * Vector3.up) + (rotB * Vector3.up);
                Vector3 upPerp = Vector3.ProjectOnPlane(up, axis);
                if (upPerp.sqrMagnitude < 1e-6f)
                {
                    upPerp = Vector3.ProjectOnPlane(Vector3.up, axis);
                    if (upPerp.sqrMagnitude < 1e-6f)
                        upPerp = Vector3.ProjectOnPlane(Vector3.forward, axis);
                }
                return Quaternion.LookRotation(axis, upPerp.normalized);
            }
        }

        static readonly List<Session> sessions = new();

        public static void Add(Transform target, GrabController hand)
        {
            var session = sessions.Find(s => s.target == target);
            if (session == null)
            {
                session = new Session { target = target };
                sessions.Add(session);
                session.CaptureGrabStart(); // posa PRE-presa, per l'undo dello spostamento
            }
            if (!session.hands.Contains(hand))
                session.hands.Add(hand);
            session.Recapture();
        }

        public static void Remove(GrabController hand)
        {
            var session = sessions.Find(s => s.hands.Contains(hand));
            if (session == null)
                return;
            session.hands.Remove(hand);
            if (session.hands.Count == 0 || session.target == null)
            {
                sessions.Remove(session);
                session.PushTransformIfMoved(); // rilascio finale: registra lo spostamento
            }
            else
                session.Recapture(); // da due mani a una: ri-aggancia
        }

        public static bool IsHeld(Transform target) =>
            sessions.Exists(s => s.target == target);

        public static void ApplyIfLeader(GrabController hand)
        {
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var session = sessions[i];
                if (session.target == null)
                {
                    sessions.RemoveAt(i); // oggetto distrutto sotto le mani
                    continue;
                }
                if (session.hands[0] == hand)
                    session.Apply();
            }
        }
    }
}
