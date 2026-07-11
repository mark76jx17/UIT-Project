using UnityEngine;

namespace MixedRealityProject.Drawing
{
    public enum TutorialState { NotStarted, InProgress, Paused, Completed }

    /// <summary>
    /// Stato persistente del tutorial guidato (PlayerPrefs), stesso pattern di
    /// Localization/StrokeSettings. Nessuna UI: solo lettura/scrittura dei flag.
    /// </summary>
    public static class TutorialProgress
    {
        const string ProposedKey = "tutorial.proposed";
        const string StateKey = "tutorial.state";
        const string StepKey = "tutorial.step";

        /// <summary>La schermata di benvenuto è già stata mostrata almeno una volta
        /// (all'avvio non si ripropone se true).</summary>
        public static bool Proposed
        {
            get => PlayerPrefs.GetInt(ProposedKey, 0) != 0;
            set { PlayerPrefs.SetInt(ProposedKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Stato del percorso guidato.</summary>
        public static TutorialState State
        {
            get => (TutorialState)PlayerPrefs.GetInt(StateKey, (int)TutorialState.NotStarted);
            set { PlayerPrefs.SetInt(StateKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Indice dello step corrente/salvato.</summary>
        public static int Step
        {
            get => PlayerPrefs.GetInt(StepKey, 0);
            set { PlayerPrefs.SetInt(StepKey, value); PlayerPrefs.Save(); }
        }

        /// <summary>Riporta il percorso all'inizio (per "Ricomincia tutorial").</summary>
        public static void Reset()
        {
            State = TutorialState.NotStarted;
            Step = 0;
        }
    }
}
