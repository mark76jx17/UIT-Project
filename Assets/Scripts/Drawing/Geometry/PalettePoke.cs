using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Marcatore per una sonda di "poke" della palette diversa dalla punta del pennello
    /// (es. la mano che tiene la palette, per poterla toccare quando è fissata nella stanza).
    /// I pulsanti della palette lo cercano in OnTriggerEnter, oltre a <see cref="BrushTip"/>,
    /// così il poke funziona da entrambi i controller. Vedi DrawingRig.
    /// </summary>
    public class PalettePoke : MonoBehaviour
    {
    }
}
