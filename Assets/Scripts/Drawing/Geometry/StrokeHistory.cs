using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Cronologia dei tratti per undo/redo. Ogni voce è un GRUPPO di oggetti che si
    /// annullano/ripristinano insieme con un solo undo: un tratto normale è un gruppo
    /// di uno, un tratto specchiato è un gruppo (originale + gemello), così la
    /// simmetria si annulla in un colpo solo. L'undo disattiva i GameObject invece di
    /// distruggerli, così il redo è una semplice riattivazione; lo stack di redo viene
    /// svuotato (e i suoi oggetti distrutti) al primo nuovo tratto.
    /// </summary>
    public static class StrokeHistory
    {
        // Tetto della cronologia: oltre questo i gruppi più vecchi escono dall'undo
        // (gli oggetti restano in scena, sono il disegno; si perde solo la possibilità
        // di annullarli così indietro). Tiene la lista limitata senza toccare l'arte.
        const int MaxUndo = 200;

        static readonly List<GameObject[]> groups = new();
        static readonly List<GameObject[]> redoStack = new();

        /// <summary>Aggiunge un singolo oggetto come nuovo passo di cronologia.</summary>
        public static void Push(GameObject stroke) => PushGroup(stroke);

        /// <summary>
        /// Aggiunge più oggetti come UN solo passo di cronologia: un undo li nasconde
        /// tutti, un redo li riporta tutti (usato dalla simmetria a specchio).
        /// </summary>
        public static void PushGroup(params GameObject[] strokes)
        {
            foreach (var group in redoStack)
                foreach (var s in group)
                    Object.Destroy(s);
            redoStack.Clear();

            groups.Add(strokes);
            if (groups.Count > MaxUndo)
                groups.RemoveAt(0); // il più vecchio esce dall'undo, ma resta in scena
        }

        public static void Undo()
        {
            // Le voci possono essere state distrutte nel frattempo (gomma,
            // caricamento di un disegno): si saltano.
            while (groups.Count > 0)
            {
                var last = groups[^1];
                groups.RemoveAt(groups.Count - 1);
                bool any = false;
                foreach (var s in last)
                    if (s != null)
                    {
                        s.SetActive(false);
                        any = true;
                    }
                if (!any)
                    continue;
                redoStack.Add(last);
                return;
            }
        }

        public static void Redo()
        {
            while (redoStack.Count > 0)
            {
                var last = redoStack[^1];
                redoStack.RemoveAt(redoStack.Count - 1);
                bool any = false;
                foreach (var s in last)
                    if (s != null)
                    {
                        s.SetActive(true);
                        any = true;
                    }
                if (!any)
                    continue;
                groups.Add(last);
                return;
            }
        }

        public static void Clear()
        {
            foreach (var group in groups)
                foreach (var s in group)
                    Object.Destroy(s);
            foreach (var group in redoStack)
                foreach (var s in group)
                    Object.Destroy(s);
            groups.Clear();
            redoStack.Clear();
        }
    }
}
