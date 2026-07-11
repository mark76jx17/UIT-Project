using System;
using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    public enum CoachHand { None, Brush, Palette }
    public enum CoachButton { None, Trigger, Grip }

    /// <summary>
    /// Uno step del tutorial: istruzione, quale controller/tasto evidenziare (o None per gli step
    /// di poke sulla palette), bersaglio da evidenziare con l'anello e predicato di completamento
    /// (legge solo stato pubblico esistente).
    /// </summary>
    public class TutorialStep
    {
        public string TitleKey;
        public Func<Transform> Target;      // oggetto su cui va l'anello (step senza immagine)
        public Action Enter;                // precondizione + eventuale snapshot
        public Func<bool> IsComplete;
        public CoachHand Hand = CoachHand.None;   // quale controller mostrare nella card
        public CoachButton Button = CoachButton.None; // quale tasto evidenziare sull'immagine
        public bool HighlightRect;                // true = cornice rettangolare (slider) invece dell'anello

        /// <summary>I 5 step del nucleo, nell'ordine confermato in spec.</summary>
        public static List<TutorialStep> CoreSteps(BrushController brush, PaletteController palette)
        {
            int strokeSnap = 0;
            Color colorSnap = default;
            float sizeSnap = 0f;
            Transform colorTarget = null, sizeTarget = null;
            bool brushRibbon = false, brushDashed = false;
            bool lineOn = false, lineDrew = false; int lineDrawAt = 0;
            bool eraseSel = false, eraseDid = false; int eraseAt = 0;

            return new List<TutorialStep>
            {
                // 1) Disegna un tratto — grilletto della mano che disegna, palette chiusa.
                new TutorialStep
                {
                    TitleKey = "tutorial.step.draw",
                    Hand = CoachHand.Brush,
                    Button = CoachButton.Trigger,
                    Enter = () => { palette.SetOpen(false); strokeSnap = StrokeHistory.DrawCount; },
                    IsComplete = () => StrokeHistory.DrawCount > strokeSnap,
                },
                // 2) Apri la palette — grilletto della mano-palette, precondizione: chiusa.
                new TutorialStep
                {
                    TitleKey = "tutorial.step.open",
                    Hand = CoachHand.Palette,
                    Button = CoachButton.Trigger,
                    Enter = () => palette.SetOpen(false),
                    IsComplete = () => PaletteController.IsOpen,
                },
                // 3) Cambia colore — poke sulla ruota colori, aperta, docked.
                new TutorialStep
                {
                    TitleKey = "tutorial.step.color",
                    Target = () => colorTarget,
                    Enter = () =>
                    {
                        palette.RedockPublic();
                        palette.SetOpen(true);
                        colorSnap = StrokeSettings.BaseColor;
                        var w = UnityEngine.Object.FindAnyObjectByType<ColorWheel>();
                        colorTarget = w != null ? w.transform : palette.transform;
                    },
                    IsComplete = () => StrokeSettings.BaseColor != colorSnap,
                },
                // 4) Cambia spessore — poke sullo slider Size, aperta, docked.
                new TutorialStep
                {
                    TitleKey = "tutorial.step.size",
                    Target = () => sizeTarget,
                    HighlightRect = true, // lo slider è allungato: cornice rettangolare, non anello
                    Enter = () =>
                    {
                        palette.RedockPublic();
                        palette.SetOpen(true);
                        sizeSnap = StrokeSettings.FixedRadius;
                        var s = UnityEngine.Object.FindAnyObjectByType<SizeSlider>();
                        sizeTarget = s != null ? s.transform : palette.transform;
                    },
                    IsComplete = () => !Mathf.Approximately(StrokeSettings.FixedRadius, sizeSnap),
                },
                // 5) Prova i pennelli Nastro e Tratteggiato — poke sui tasti pennello (Tool = Pen).
                new TutorialStep
                {
                    TitleKey = "tutorial.step.brush",
                    HighlightRect = true,
                    Target = () => PaletteController.FindControl(BrushType.Ribbon.ToString()),
                    Enter = () =>
                    {
                        palette.RedockPublic();
                        palette.SetOpen(true);
                        StrokeSettings.Tool = ToolMode.Pen;
                        brushRibbon = brushDashed = false;
                    },
                    IsComplete = () =>
                    {
                        if (StrokeSettings.Type == BrushType.Ribbon) brushRibbon = true;
                        if (StrokeSettings.Type == BrushType.Dashed) brushDashed = true;
                        return brushRibbon && brushDashed;
                    },
                },
                // 6) Line: attiva, disegna una linea dritta, disattiva (tasto "lineToggle" = SnapAxis).
                new TutorialStep
                {
                    TitleKey = "tutorial.step.line",
                    HighlightRect = true,
                    Target = () => PaletteController.FindControl("lineToggle"),
                    Enter = () =>
                    {
                        palette.RedockPublic();
                        palette.SetOpen(true);
                        StrokeSettings.Tool = ToolMode.Pen;
                        StrokeSettings.SnapAxis = false;
                        lineOn = lineDrew = false;
                    },
                    IsComplete = () =>
                    {
                        if (StrokeSettings.SnapAxis && !lineOn) { lineOn = true; lineDrawAt = StrokeHistory.DrawCount; }
                        if (lineOn && StrokeHistory.DrawCount > lineDrawAt) lineDrew = true;
                        return lineOn && lineDrew && !StrokeSettings.SnapAxis;
                    },
                },
                // 7) Cancella: seleziona, cancella un tratto, torna a Disegna (tasto "EraseButton").
                new TutorialStep
                {
                    TitleKey = "tutorial.step.erase",
                    HighlightRect = true,
                    Target = () => PaletteController.FindControl("EraseButton"),
                    Enter = () =>
                    {
                        palette.RedockPublic();
                        palette.SetOpen(true);
                        StrokeSettings.Tool = ToolMode.Pen;
                        eraseSel = eraseDid = false;
                        eraseAt = StrokeHistory.EraseCount;
                    },
                    IsComplete = () =>
                    {
                        if (StrokeSettings.Tool == ToolMode.Eraser) eraseSel = true;
                        if (eraseSel && StrokeHistory.EraseCount > eraseAt) eraseDid = true;
                        return eraseSel && eraseDid && StrokeSettings.Tool == ToolMode.Pen;
                    },
                },
                // 8) Sposta la palette — grip della MANO CHE DISEGNA (è lei che raggiunge il bordo
                // e stringe il grip: vedi PaletteController, grip letto su StrokeSettings.BrushHand).
                new TutorialStep
                {
                    TitleKey = "tutorial.step.move",
                    Hand = CoachHand.Brush,
                    Button = CoachButton.Grip,
                    Enter = () =>
                    {
                        palette.RedockPublic();
                        palette.SetOpen(true);
                    },
                    IsComplete = () => PaletteController.Placed,
                },
            };
        }
    }
}
