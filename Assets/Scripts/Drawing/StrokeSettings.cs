using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    public enum BrushType
    {
        Round,   // tubo (default)
        Ribbon,  // nastro piatto
        Glow,    // emissivo (brilla col bloom)
        Dashed,  // tratteggiato (texture lungo la V del tubo)
    }

    public enum ToolMode
    {
        Pen,    // disegna tratti
        Fill,   // disegna un contorno che si chiude e si riempie
        Eraser, // cancella l'oggetto toccato
    }

    public enum SizeMode
    {
        FixedPen,       // spessore scelto dalla palette
        PressureBrush   // spessore dipende dalla pressione
    }


    /// <summary>
    /// Impostazioni correnti del pennello. Scritte dalla palette (mano sinistra),
    /// lette dal pennello (mano destra) al momento del trigger-press: le proprietà
    /// si "fotografano" all'inizio del tratto e valgono per tutto il tratto.
    /// </summary>
    public static class StrokeSettings
    {
        public static Color BaseColor = Color.white;
        public static float Alpha = 1f;

        /// <summary>Strumento corrente: penna, riempimento o gomma.</summary>
        public static ToolMode Tool = ToolMode.Pen;

        // Mano del pennello e mano della palette: impostate dal DrawingRig
        // (spunta "Left Handed" nell'Inspector per i mancini).
        public static OVRInput.Controller BrushHand = OVRInput.Controller.RTouch;
        public static OVRInput.Controller PaletteHand = OVRInput.Controller.LTouch;

        public static bool EraserMode => Tool == ToolMode.Eraser;
        public static bool FillMode => Tool == ToolMode.Fill;

        /// <summary>Tipo di pennello corrente (vedi la riga pennelli della palette).</summary>
        public static BrushType Type = BrushType.Round;

        /// <summary>Snap ad assi: il tratto viene vincolato all'asse del mondo dominante
        /// rispetto al punto di partenza (linee dritte X/Y/Z). Toggle "Snap" in palette.</summary>
        public static bool SnapAxis;

        //modalità dimensione
        public static SizeMode SizeMode = SizeMode.FixedPen;

        // Valore dello slider: 0 = minimo, 1 = massimo
        public static float Size01 = 0.5f;

        //range di dimensioni per la modalità FixedPen
        public static float MinFixedRadius = 0.003f;
        public static float MaxFixedRadius = 0.020f;

        public static float FixedRadius =>
            Mathf.Lerp(MinFixedRadius, MaxFixedRadius, Size01);


        // Stato HSV per la ruota dei colori e lo slider di luminosità.
        public static float Hue;
        public static float Sat;
        public static float Val = 1f;

        public static void SetHSV(float hue, float sat, float val)
        {
            Hue = hue;
            Sat = sat;
            Val = val;
            BaseColor = Color.HSVToRGB(hue, sat, val);
        }

        public static void SetColor(Color color)
        {
            Color.RGBToHSV(color, out var h, out var s, out var v);
            SetHSV(h, s, v);
        }

        public static Color Color
        {
            get
            {
                var c = BaseColor;
                c.a = Alpha;
                return c;
            }
        }

        // ---- Colori recenti (max 5, persistono tra le sessioni) ----

        const int MaxRecentColors = 5;
        const string RecentColorsKey = "drawing.recentColors";
        static readonly List<Color> recentColors = new();

        /// <summary>Notifica la palette quando l'elenco dei recenti cambia.</summary>
        public static System.Action RecentColorsChanged;
        public static IReadOnlyList<Color> RecentColors => recentColors;

        /// <summary>Registra un colore tra i recenti (in testa, senza duplicati).</summary>
        public static void PushRecentColor(Color color)
        {
            color.a = 1f; // i recenti memorizzano la tinta, non la trasparenza
            recentColors.RemoveAll(c => ApproximatelyEqual(c, color));
            recentColors.Insert(0, color);
            while (recentColors.Count > MaxRecentColors)
                recentColors.RemoveAt(recentColors.Count - 1);
            SaveRecentColors();
            RecentColorsChanged?.Invoke();
        }

        /// <summary>Carica i recenti salvati (chiamato all'avvio da DrawingRig).</summary>
        public static void LoadRecentColors()
        {
            recentColors.Clear();
            foreach (var token in PlayerPrefs.GetString(RecentColorsKey, "").Split(';'))
                if (!string.IsNullOrEmpty(token) &&
                    ColorUtility.TryParseHtmlString("#" + token, out var c))
                    recentColors.Add(c);
            RecentColorsChanged?.Invoke();
        }

        static void SaveRecentColors()
        {
            var parts = new List<string>();
            foreach (var c in recentColors)
                parts.Add(ColorUtility.ToHtmlStringRGB(c));
            PlayerPrefs.SetString(RecentColorsKey, string.Join(";", parts));
            PlayerPrefs.Save();
        }

        static bool ApproximatelyEqual(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.02f &&
            Mathf.Abs(a.g - b.g) < 0.02f &&
            Mathf.Abs(a.b - b.b) < 0.02f;
    }
}
