using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Dati sorgente di un oggetto disegnato (polilinea + raggi + colore):
    /// la mesh è derivabile, quindi per salvare/caricare basta questo.
    /// Compilato da Stroke a fine tratto e letto da DrawingStore.
    /// </summary>
    public class StrokeRecord : MonoBehaviour
    {
        public List<Vector3> points = new();
        public List<float> radii = new();
        public Color color;
        public BrushType brushType;
        public bool isPoint;
        public bool filled;
        public Color fillColor = Color.white; // colore del riempimento (può differire dal contorno)
    }
}
