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

        /// <summary>
        /// True = questo oggetto È una superficie di riempimento di GRUPPO (creata
        /// unendo più tratti, vedi Stroke.FillGroup): in <see cref="points"/> c'è il
        /// contorno già assemblato, nello spazio locale del genitore. Si ricostruisce
        /// al caricamento con Stroke.RebuildFill, senza ripassare dai singoli tratti.
        /// (I riempimenti del SINGOLO tratto usano invece <see cref="filled"/>.)
        /// </summary>
        public bool isFill;

        /// <summary>
        /// Buchi (contorni interni da lasciare vuoti) di un riempimento di regione, es. la
        /// ciambella: <see cref="points"/> è il contorno esterno, questi sono i fori.
        /// Nello stesso spazio di points. NonSerialized: la persistenza passa da DrawingStore
        /// (JsonUtility non gestisce le liste annidate); qui serve solo a runtime.
        /// </summary>
        [System.NonSerialized] public List<List<Vector3>> holes = new();
    }
}
