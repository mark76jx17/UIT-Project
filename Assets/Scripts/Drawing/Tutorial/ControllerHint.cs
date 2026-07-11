using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Immagini reali dei controller (baked in Resources/Controllers) e posizioni normalizzate
    /// (x→destra, y→giù, 0..1) dei tasti su ciascuna immagine, per piazzarci sopra l'anello di
    /// evidenziazione. Le ancore sono tarate a preview (render con griglia).
    /// </summary>
    public static class ControllerHint
    {
        public static Texture2D Image(bool physicalLeft) =>
            Resources.Load<Texture2D>(physicalLeft ? "Controllers/touch-left" : "Controllers/touch-right");

        // Grilletto (indice): tarato sulla griglia (feedback utente).
        public static Vector2 Trigger(bool physicalLeft) =>
            physicalLeft ? new Vector2(0.42f, 0.20f) : new Vector2(0.58f, 0.20f);

        // Grip/grab: tab centrale (tarato sulla griglia).
        public static Vector2 Grip(bool physicalLeft) =>
            physicalLeft ? new Vector2(0.47f, 0.52f) : new Vector2(0.53f, 0.52f);
    }
}
