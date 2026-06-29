using System;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Scorciatoie da controller: attivano le funzioni della palette SENZA aprirla,
    /// leggendo direttamente i pulsanti dei due Touch via OVRInput. Aggiunto a runtime
    /// dal <see cref="DrawingRig"/> sul GameObject del pennello.
    ///
    /// Non duplica logica: ogni azione richiama le stesse API statiche usate dai bottoni
    /// della palette (StrokeSettings/Mirror/ReferenceGrid/StrokeHistory/DrawingStore).
    /// L'elenco <see cref="All"/> è l'unica fonte di verità delle mappature ed è anche
    /// ciò che il pannello "View Shortcuts" mostra (vedi PaletteController.BuildShortcutsPanel).
    ///
    /// Convenzioni vs scelte di progetto:
    ///  - CONVENZIONE: Undo/Redo sulla mano non dominante (palette), come in Open Brush;
    ///    "Delete all" richiede una pressione prolungata (hold-to-confirm) per sicurezza.
    ///  - DI PROGETTO: tutte le direzioni/click dello stick e i face button per
    ///    Options/Save/Load. Non esiste una convenzione consolidata che leghi questi
    ///    comandi-palette a pulsanti del controller (Gravity Sketch/Open Brush usano menu
    ///    in-world), quindi sono mappature scelte qui per usabilità/coerenza.
    /// </summary>
    public class ControllerShortcuts : MonoBehaviour
    {
        // Iniettati dal DrawingRig.
        public PaletteController Palette;
        public Transform Head;

        const float FlickOn = 0.7f;     // soglia per riconoscere una "spinta" dello stick
        const float FlickOff = 0.3f;    // sotto questa lo stick è "riarmato" per la prossima
        const float GripThreshold = 0.5f;
        const float DeleteHoldSeconds = 1.5f;

        // Tipi di pennello selezionabili dalla scorciatoia: Glow è disattivato in palette
        // (vedi PaletteController.BuildBrushStrip) quindi il ciclo lo salta.
        static readonly BrushType[] TypeCycle = { BrushType.Round, BrushType.Ribbon, BrushType.Dashed };

        enum Flick { None, Up, Down, Left, Right }

        bool brushArmed = true, paletteArmed = true;
        float deleteHold;
        bool deleteFired;

        void Update()
        {
            // Senza runtime XR (desktop/editor) i controller non esistono: il
            // DesktopBrushSimulator gestisce la tastiera, qui non facciamo nulla.
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
                return;

            var brush = StrokeSettings.BrushHand;
            var palette = StrokeSettings.PaletteHand;

            HandlePaletteHand(palette);
            HandleBrushHand(brush);

            // Menu (☰): il tasto menu "classico" esiste fisicamente solo sul Touch
            // sinistro (Button.Start), quindi è indipendente da quale mano regge la
            // palette/il pennello. Apre/chiude lo stesso pannello Options del bottone "...".
            if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch) && Palette != null)
                Palette.ToggleOptions();
        }

        // --- Mano della palette (mano "comandi"): strumenti, tipo pennello, undo/redo, menu, save ---
        void HandlePaletteHand(OVRInput.Controller c)
        {
            switch (ReadFlick(c, ref paletteArmed))
            {
                case Flick.Left: CycleType(-1); break;
                case Flick.Right: CycleType(+1); break;
                case Flick.Up: StrokeHistory.Redo(); Toast.Show("Redo"); break;
                case Flick.Down: StrokeHistory.Undo(); Toast.Show("Undo"); break;
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, c))
                CycleTool();

            // B/Y = salva (DrawingStore.Save mostra già il proprio toast).
            if (OVRInput.GetDown(OVRInput.Button.Two, c))
                DrawingStore.Save();
        }

        // --- Mano del pennello: toggle Pressure/Grid/Mirror/Snap, Load, Delete all ---
        void HandleBrushHand(OVRInput.Controller c)
        {
            switch (ReadFlick(c, ref brushArmed))
            {
                case Flick.Up: ReferenceGrid.Toggle(Reference()); Toast.Show(ReferenceGrid.Enabled ? "Grid on" : "Grid off"); break;
                case Flick.Down: Mirror.Toggle(Reference()); Toast.Show(Mirror.Enabled ? "Mirror on" : "Mirror off"); break;
                case Flick.Left: StrokeSettings.SnapAxis = !StrokeSettings.SnapAxis; Toast.Show(StrokeSettings.SnapAxis ? "Snap on" : "Snap off"); break;
                case Flick.Right: DrawingStore.Load(); break; // self-toast
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, c))
            {
                StrokeSettings.SizeMode = StrokeSettings.SizeMode == SizeMode.PressureBrush
                    ? SizeMode.FixedPen : SizeMode.PressureBrush;
                Toast.Show(StrokeSettings.SizeMode == SizeMode.PressureBrush ? "Pressure on" : "Pressure off");
            }

            // Delete all = B/Y tenuto premuto ~1.5s e SOLO se non si sta afferrando: il
            // tap di B/Y mentre afferri è già "duplica" (GrabController), e l'hold evita
            // cancellazioni accidentali di tutta la scena.
            bool gripping = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, c) >= GripThreshold;
            if (!gripping && OVRInput.Get(OVRInput.Button.Two, c))
            {
                deleteHold += Time.deltaTime;
                if (!deleteFired && deleteHold >= DeleteHoldSeconds)
                {
                    DrawingStore.NewScene();
                    Toast.Show("Cleared all");
                    deleteFired = true;
                }
            }
            else
            {
                deleteHold = 0f;
                deleteFired = false;
            }
        }

        // Stick come 4 direzioni discrete con debounce: scatta una volta quando supera
        // FlickOn, si riarma solo dopo essere tornato sotto FlickOff.
        Flick ReadFlick(OVRInput.Controller c, ref bool armed)
        {
            Vector2 s = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c);
            float mag = s.magnitude;
            if (!armed)
            {
                if (mag < FlickOff) armed = true;
                return Flick.None;
            }
            if (mag < FlickOn)
                return Flick.None;
            armed = false;
            if (Mathf.Abs(s.x) > Mathf.Abs(s.y))
                return s.x > 0f ? Flick.Right : Flick.Left;
            return s.y > 0f ? Flick.Up : Flick.Down;
        }

        void CycleTool()
        {
            StrokeSettings.Tool = (ToolMode)(((int)StrokeSettings.Tool + 1) % 3);
            Toast.Show("Tool: " + StrokeSettings.Tool); // Pen / Fill / Eraser
        }

        void CycleType(int dir)
        {
            int idx = Array.IndexOf(TypeCycle, StrokeSettings.Type);
            if (idx < 0) idx = 0; // partendo da un tipo non in ciclo (Glow)
            idx = (idx + dir + TypeCycle.Length) % TypeCycle.Length;
            StrokeSettings.Type = TypeCycle[idx];
            Toast.Show("Brush: " + StrokeSettings.Type); // Round / Ribbon / Dashed
        }

        // Riferimento per Mirror/Grid (piano davanti allo sguardo), come fa la palette.
        Transform Reference()
        {
            if (Head != null) return Head;
            return Camera.main != null ? Camera.main.transform : transform;
        }

        // ---------------------------------------------------------------------------
        // Unica fonte di verità delle scorciatoie, mostrata dal pannello "View Shortcuts".
        // Hand = ruolo (mano-palette / mano-pennello); Button = tasto fisico (così il
        // pannello sa DOVE puntare la linea-guida sul controller); Action = testo breve.
        // ---------------------------------------------------------------------------

        // Tasti fisici a cui può essere agganciata una scorciatoia. Lo stick è un solo
        // controllo ma con 5 azioni distinte (4 direzioni + click): qui sono voci separate
        // perché ognuna ha la sua etichetta/posizione nel diagramma.
        public enum Btn { StickClick, StickUp, StickDown, StickLeft, StickRight, FaceA, FaceB, Menu }

        public readonly struct ShortcutBinding
        {
            public readonly string Hand;   // ruolo: PaletteHandName / BrushHandName
            public readonly Btn Button;
            public readonly string Action; // chiave di localizzazione (vedi Localization)
            public ShortcutBinding(string hand, Btn button, string action)
            {
                Hand = hand; Button = button; Action = action;
            }
        }

        // Identificatori di ruolo (NON mostrati: servono solo per accoppiare le voci al
        // controller giusto nel diagramma). Il testo visibile è localizzato altrove.
        public const string PaletteHandName = "Palette-hand controller";
        public const string BrushHandName = "Brush-hand controller";

        // Action = chiave di localizzazione: il pannello "View Shortcuts" la traduce nella
        // lingua corrente (vedi PaletteController.ActionFor).
        public static readonly ShortcutBinding[] All =
        {
            // Mano della palette
            new(PaletteHandName, Btn.StickClick, "sc.tool"),
            new(PaletteHandName, Btn.StickLeft,  "sc.brushPrev"),
            new(PaletteHandName, Btn.StickRight, "sc.brushNext"),
            new(PaletteHandName, Btn.StickUp,    "sc.redo"),
            new(PaletteHandName, Btn.StickDown,  "sc.undo"),
            new(PaletteHandName, Btn.FaceB,      "sc.save"),
            new(PaletteHandName, Btn.Menu,       "options"), // ☰: fisicamente sul Touch sinistro

            // Mano del pennello
            new(BrushHandName, Btn.StickClick, "sc.pressure"),
            new(BrushHandName, Btn.StickUp,    "sc.grid"),
            new(BrushHandName, Btn.StickDown,  "sc.mirror"),
            new(BrushHandName, Btn.StickLeft,  "sc.snap"),
            new(BrushHandName, Btn.StickRight, "sc.load"),
            new(BrushHandName, Btn.FaceB,      "sc.clearAll"),
        };
    }
}
