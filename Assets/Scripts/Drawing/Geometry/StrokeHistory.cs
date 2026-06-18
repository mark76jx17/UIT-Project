using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Cronologia dei tratti per undo/redo. L'undo disattiva il GameObject invece
    /// di distruggerlo, così il redo è una semplice riattivazione; lo stack di redo
    /// viene svuotato (e i tratti distrutti) al primo nuovo tratto.
    /// </summary>
    public static class StrokeHistory
    {
        static readonly List<GameObject> strokes = new();
        static readonly List<GameObject> redoStack = new();

        public static void Push(GameObject stroke)
        {
            foreach (var s in redoStack)
                Object.Destroy(s);
            redoStack.Clear();
            strokes.Add(stroke);
        }

        public static void Undo()
        {
            // Le voci possono essere state distrutte nel frattempo (gomma,
            // caricamento di un disegno): si saltano.
            while (strokes.Count > 0)
            {
                var last = strokes[^1];
                strokes.RemoveAt(strokes.Count - 1);
                if (last == null)
                    continue;
                last.SetActive(false);
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
                if (last == null)
                    continue;
                last.SetActive(true);
                strokes.Add(last);
                return;
            }
        }

        public static void Clear()
        {
            foreach (var s in strokes)
                Object.Destroy(s);
            foreach (var s in redoStack)
                Object.Destroy(s);
            strokes.Clear();
            redoStack.Clear();
        }
    }
}
