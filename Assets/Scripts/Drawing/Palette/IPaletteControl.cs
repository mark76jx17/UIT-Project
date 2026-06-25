using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Controllo "a trascinamento" della palette (ruota colori, slider): si aziona
    /// passando il punto colpito dal ray o dal tocco. Unifica il dispatch in
    /// PaletteRay e nel simulatore: aggiungere un controllo ora richiede solo di
    /// implementare questa interfaccia, non di toccare 3 file (è così che erano
    /// sopravvissuti i controlli morti ColorSquare/HueBar).
    /// I pulsanti (PaletteButton) restano a parte: scattano una volta, non trascinano.
    /// </summary>
    public interface IPaletteControl
    {
        void PressAt(Vector3 worldPoint);
    }
}
