using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Cronologia undo/redo come stack di AZIONI, ognuna con un insieme di oggetti
    /// "aggiunti" e uno di "rimossi" (nascosti). Disegnare = aggiunge; cancellare =
    /// rimuove; la cancellazione PARZIALE rimuove l'originale e aggiunge i pezzi
    /// rimasti, in un solo passo (Replace). Undo riattiva i rimossi e nasconde gli
    /// aggiunti; redo fa l'opposto. Gli oggetti non si distruggono finché restano
    /// annullabili, così il redo è una semplice riattivazione.
    /// </summary>
    public static class StrokeHistory
    {
        const int MaxUndo = 200;
        static readonly GameObject[] None = new GameObject[0];

        class Action
        {
            public GameObject[] added = None;   // mostrati dall'azione (disegno / pezzi)
            public GameObject[] removed = None; // nascosti dall'azione (gomma)

            // Unione (magnete post-disegno): reparenting di un oggetto sotto un altro.
            // Forward (esegui/redo): mergeChild → mergeNewParent e si TOGLIE il suo
            // DrawnItem (il gruppo si afferra come un unico oggetto). Reverse (undo):
            // mergeChild → mergeOldParent e si RIPRISTINA il DrawnItem.
            public Transform mergeChild;
            public Transform mergeOldParent;
            public Transform mergeNewParent;

            // Spostamento/rotazione/scala di un oggetto afferrato (la posa è già stata
            // applicata dal chiamante). Posizione e rotazione in spazio MONDO (robuste a
            // un'eventuale unione magnete successiva, annullata prima di questa), scala
            // locale. Reverse (undo): posa "prima"; forward (redo): posa "dopo".
            public Transform poseTarget;
            public Vector3 posBefore, posAfter;
            public Quaternion rotBefore, rotAfter;
            public Vector3 scaleBefore, scaleAfter;
        }

        static readonly List<Action> undo = new();
        static readonly List<Action> redo = new();

        /// <summary>Numero di "disegni" registrati da inizio sessione (tratti/tap/fill che
        /// AGGIUNGONO oggetti). Monotono crescente, non decrementa con l'undo: serve solo come
        /// segnale "l'utente ha disegnato qualcosa" (usato dal tutorial guidato).</summary>
        public static int DrawCount { get; private set; }

        /// <summary>Numero di "cancellazioni" registrate da inizio sessione (gomma piena o
        /// parziale). Monotono crescente: segnale "l'utente ha cancellato qualcosa" (tutorial).</summary>
        public static int EraseCount { get; private set; }

        public static void Push(GameObject stroke) => PushGroup(stroke);

        public static void PushGroup(params GameObject[] strokes)
        {
            DrawCount++;
            Add(new Action { added = strokes });
        }

        /// <summary>Cancellazione: il chiamante ha già nascosto gli oggetti.</summary>
        public static void PushErase(params GameObject[] strokes)
        {
            EraseCount++;
            Add(new Action { removed = strokes });
        }

        /// <summary>
        /// Sostituzione (cancellazione parziale): <paramref name="removed"/> già nascosti,
        /// <paramref name="added"/> i pezzi rimasti — un solo passo di undo.
        /// </summary>
        public static void PushReplace(GameObject[] removed, GameObject[] added)
        {
            EraseCount++;
            Add(new Action { removed = removed, added = added });
        }

        /// <summary>
        /// Unione magnete di un oggetto già disegnato sotto un altro (l'unione è già
        /// stata applicata dal chiamante). Undo la separa, redo la riunisce.
        /// </summary>
        public static void PushMerge(Transform child, Transform oldParent, Transform newParent)
            => Add(new Action { mergeChild = child, mergeOldParent = oldParent, mergeNewParent = newParent });

        /// <summary>
        /// Spostamento/rotazione/scala di un oggetto afferrato (la posa "dopo" è quella
        /// corrente del target; il chiamante passa la posa "prima" catturata all'inizio
        /// della presa). Undo ripristina la posa "prima", redo quella "dopo".
        /// </summary>
        public static void PushTransform(Transform target,
            Vector3 posBefore, Quaternion rotBefore, Vector3 scaleBefore)
            => Add(new Action
            {
                poseTarget = target,
                posBefore = posBefore, rotBefore = rotBefore, scaleBefore = scaleBefore,
                posAfter = target.position, rotAfter = target.rotation, scaleAfter = target.localScale,
            });

        static void Add(Action action)
        {
            ClearRedo();
            undo.Add(action);
            if (undo.Count > MaxUndo)
            {
                var old = undo[0];
                undo.RemoveAt(0);
                // Oltre il tetto: i "rimossi" sono nascosti e non più ripristinabili → liberali.
                foreach (var o in old.removed)
                    Object.Destroy(o);
            }
        }

        public static void Undo()
        {
            while (undo.Count > 0)
            {
                var a = undo[^1];
                undo.RemoveAt(undo.Count - 1);
                if (!ApplyReverse(a))
                    continue;
                redo.Add(a);
                return;
            }
        }

        public static void Redo()
        {
            while (redo.Count > 0)
            {
                var a = redo[^1];
                redo.RemoveAt(redo.Count - 1);
                if (!ApplyForward(a))
                    continue;
                undo.Add(a);
                return;
            }
        }

        // Forward (esegui/redo): mostra gli aggiunti, nasconde i rimossi, riunisce, riposa.
        static bool ApplyForward(Action a)
            => SetActive(a.added, true) | SetActive(a.removed, false) | ApplyMerge(a, true) | ApplyPose(a, true);

        // Reverse (undo): mostra i rimossi, nasconde gli aggiunti, separa, riposa.
        static bool ApplyReverse(Action a)
            => SetActive(a.removed, true) | SetActive(a.added, false) | ApplyMerge(a, false) | ApplyPose(a, false);

        // Riapplica/annulla uno spostamento. forward = posa "dopo"; reverse = posa "prima".
        // Ritorna true se ha agito (così l'azione non viene "saltata"); false se il target
        // è stato distrutto.
        static bool ApplyPose(Action a, bool forward)
        {
            if (a.poseTarget == null)
                return false;
            a.poseTarget.SetPositionAndRotation(
                forward ? a.posAfter : a.posBefore,
                forward ? a.rotAfter : a.rotBefore);
            a.poseTarget.localScale = forward ? a.scaleAfter : a.scaleBefore;
            return true;
        }

        // Riapplica/annulla un'unione magnete. forward = unisci (child sotto newParent,
        // via il suo DrawnItem); reverse = separa (child torna a oldParent, DrawnItem
        // ripristinato). Ritorna true se ha agito (così l'azione non viene "saltata").
        static bool ApplyMerge(Action a, bool forward)
        {
            if (a.mergeChild == null)
                return false;
            var parent = forward ? a.mergeNewParent : a.mergeOldParent;
            if (forward && parent == null)
                return false; // bersaglio dell'unione distrutto: non si può rifare
            a.mergeChild.SetParent(parent, true); // mantiene la posa nel mondo
            var item = a.mergeChild.GetComponent<DrawnItem>();
            if (forward)
            {
                if (item != null) Object.Destroy(item);
            }
            else if (item == null)
            {
                a.mergeChild.gameObject.AddComponent<DrawnItem>();
            }
            return true;
        }

        static bool SetActive(GameObject[] objs, bool active)
        {
            bool any = false;
            foreach (var o in objs)
                if (o != null)
                {
                    o.SetActive(active);
                    any = true;
                }
            return any;
        }

        static void ClearRedo()
        {
            // I redo annullati hanno gli "added" nascosti (vanno distrutti) e i "removed"
            // di nuovo visibili (NON toccarli: sono arte in scena).
            foreach (var a in redo)
                foreach (var o in a.added)
                    Object.Destroy(o);
            redo.Clear();
        }

        public static void Clear()
        {
            foreach (var a in undo) { Destroy(a.added); Destroy(a.removed); }
            foreach (var a in redo) { Destroy(a.added); Destroy(a.removed); }
            undo.Clear();
            redo.Clear();
        }

        static void Destroy(GameObject[] objs)
        {
            foreach (var o in objs)
                Object.Destroy(o);
        }
    }
}
