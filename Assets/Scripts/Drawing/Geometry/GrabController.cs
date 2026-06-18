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
        [SerializeField] float pressThreshold = 0.55f;
        [SerializeField] float releaseThreshold = 0.35f;

        Transform holding;
        Transform hovered;

        public OVRInput.Controller Controller
        {
            set => controller = value;
        }

        void Update()
        {
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
                }
            }
            else if (grip <= releaseThreshold || holding == null /* distrutto (gomma) */)
            {
                GrabSession.Remove(this);
                StrokeHighlight.Clear(holding);
                holding = null;
            }
            else if (controller == StrokeSettings.BrushHand
                     && OVRInput.GetDown(OVRInput.Button.Two, controller))
            {
                // B (mano del pennello) mentre afferri = duplica, stile
                // Gravity Sketch. Solo su quella mano: sull'altra il tasto
                // "Two" è già il redo.
                var copy = DrawingStore.Duplicate(holding);
                if (copy != null)
                {
                    copy.transform.position += transform.up * 0.03f;
                    StrokeHistory.Push(copy);
                }
            }
        }

        // L'oggetto afferrato viene mosso in LateUpdate, dopo che il tracking
        // ha aggiornato la posa delle mani in Update.
        void LateUpdate() => GrabSession.ApplyIfLeader(this);

        void UpdateHover()
        {
            Transform found = null;
            var hits = Physics.OverlapSphere(transform.position, grabRadius,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            foreach (var hit in hits)
            {
                var item = hit.GetComponentInParent<DrawnItem>();
                if (item == null)
                    continue;
                found = item.transform;
                break;
            }
            SetHover(found);
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
                    var mid0 = (a0 + b0) * 0.5f;
                    var mid = (a + b) * 0.5f;
                    var rotation = Quaternion.FromToRotation((b0 - a0).normalized, (b - a).normalized);

                    target.localScale = scale0 * scale;
                    target.rotation = rotation * rot0;
                    target.position = mid + rotation * (pos0 - mid0) * scale;
                }
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
                sessions.Remove(session);
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
