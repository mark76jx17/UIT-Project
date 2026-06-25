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
        }

        static readonly List<Action> undo = new();
        static readonly List<Action> redo = new();

        public static void Push(GameObject stroke) => PushGroup(stroke);

        public static void PushGroup(params GameObject[] strokes)
            => Add(new Action { added = strokes });

        /// <summary>Cancellazione: il chiamante ha già nascosto gli oggetti.</summary>
        public static void PushErase(params GameObject[] strokes)
            => Add(new Action { removed = strokes });

        /// <summary>
        /// Sostituzione (cancellazione parziale): <paramref name="removed"/> già nascosti,
        /// <paramref name="added"/> i pezzi rimasti — un solo passo di undo.
        /// </summary>
        public static void PushReplace(GameObject[] removed, GameObject[] added)
            => Add(new Action { removed = removed, added = added });

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

        // Forward (esegui/redo): mostra gli aggiunti, nasconde i rimossi.
        static bool ApplyForward(Action a) => SetActive(a.added, true) | SetActive(a.removed, false);

        // Reverse (undo): mostra i rimossi, nasconde gli aggiunti.
        static bool ApplyReverse(Action a) => SetActive(a.removed, true) | SetActive(a.added, false);

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
