using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Toggle "Grid" della palette: crea/distrugge il foglio a quadretti 3D (GridSheet),
    /// il riferimento visivo da disegno posizionabile nella stanza — ha sostituito la
    /// vecchia griglia a pavimento, inutile in passthrough (il pavimento reale c'è già).
    /// Facciata statica con la stessa API di sempre (Toggle/Enable/Disable/Enabled), così
    /// palette, flick dello stick e simulatore desktop non cambiano. Con Line attiva il
    /// foglio fa da piano di disegno: BrushController proietta via TryProject.
    /// </summary>
    public static class ReferenceGrid
    {
        public static bool Enabled => sheet != null;

        // Scritti da GridSheet, letti da GrabController/PaletteRay: come i gemelli di
        // PaletteController, sospendono presa tratti e disegno mentre il foglio è
        // vicino al grip / trascinato.
        public static bool SuppressBrushGrab;
        public static bool IsGrabbing;

        static GridSheet sheet;

        public static void Toggle(Transform reference)
        {
            if (Enabled)
                Disable();
            else
                Enable(reference);
        }

        public static void Enable(Transform reference) => sheet = GridSheet.Create(reference);

        public static void Disable()
        {
            if (sheet != null)
                Object.Destroy(sheet.gameObject); // OnDestroy del foglio resetta i flag
            sheet = null;
        }

        /// <summary>Proiezione sul foglio (vedi GridSheet.TryProject); false se la grid è spenta.</summary>
        public static bool TryProject(Vector3 worldPos, out Vector3 projected)
        {
            if (sheet == null)
            {
                projected = worldPos;
                return false;
            }
            return sheet.TryProject(worldPos, out projected);
        }

        /// <summary>Proiezione senza vincolo di distanza (latch del tratto sulla carta);
        /// identità se la grid è spenta (es. spenta a metà tratto).</summary>
        public static Vector3 ProjectClamped(Vector3 worldPos)
            => sheet != null ? sheet.ProjectClamped(worldPos) : worldPos;
    }
}
