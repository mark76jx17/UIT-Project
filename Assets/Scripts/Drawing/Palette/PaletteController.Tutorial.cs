using TMPro;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    // Accessori additivi per il tutorial guidato. Il file principale (di fla52) resta intatto:
    // questa partial legge/forza lo stato della palette per le precondizioni degli step, così
    // il tutorial può garantire uno stato coerente all'ingresso di ogni step.
    public partial class PaletteController
    {
        /// <summary>Istanza corrente (o null se non ancora inizializzata).</summary>
        public static PaletteController Instance => instance;

        /// <summary>True se la palette è aperta.</summary>
        public static bool IsOpen => instance != null && instance.isOpen;

        /// <summary>Apre/chiude la palette in modo esplicito (senza toggle "a indovinare").
        /// Usato dalle precondizioni degli step del tutorial per garantire lo stato atteso.</summary>
        public void SetOpen(bool open)
        {
            if (isOpen == open)
                return;
            isOpen = open;
            if (!open)
                visibility = 0f; // chiusura immediata: niente flash della palette all'avvio del tutorial
            UiFeedback.Instance?.PanelToggle(isOpen);
        }

        /// <summary>Riaggancia la palette alla mano (da Placed a Docked), esposto per il tutorial.
        /// Riusa la logica privata di re-dock; no-op se non è fissata.</summary>
        public void RedockPublic()
        {
            if (placeMode == PlaceMode.Placed)
                Redock();
        }

        /// <summary>Trova il transform di un controllo della palette per nome del GameObject
        /// (es. "EraseButton", "lineToggle", "Ribbon", "Dashed"), cercando tra i pulsanti attivi.
        /// Usato dal tutorial per evidenziare il tasto giusto. Null se non trovato/non attivo.</summary>
        public static Transform FindControl(string name)
        {
            var list = PaletteButton.Instances;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].gameObject.name == name)
                    return list[i].transform;
            return null;
        }

        // Voce "Tutorial" in fondo al menu Options (chiamata da BuildOptionsPanel, così viene
        // ricreata ad ogni rebuild). Se il tutorial è in pausa riprende, altrimenti (ri)parte da
        // capo. Usa gli helper privati di costruzione: sta nella partial, il file di fla52 ha solo
        // la riga di hook.
        void BuildTutorialOptionsRow(Transform parent, Vector2 size)
        {
            var cell = new Vector2(size.x - 0.05f, 0.045f);
            float y = -size.y * 0.5f + 0.045f; // riga in fondo al pannello
            var btn = MakeRoundedButton(parent, "TutorialEntry",
                new Vector3(0f, y, -0.004f), cell, Mathf.Min(0.014f, cell.y * 0.4f), ButtonColor,
                () =>
                {
                    var t = TutorialController.Instance;
                    if (t == null)
                        return;
                    if (TutorialProgress.State == TutorialState.Paused)
                        t.Resume();
                    else
                        t.StartFromBeginning();
                });
            MakeLabel(btn.transform, Localization.Get("tutorial.menu.start"),
                new Vector3(0f, 0f, -0.004f), cell, SliderLabelFont * 1.1f,
                TextAlignmentOptions.Center, autoFit: true);
        }
    }
}
