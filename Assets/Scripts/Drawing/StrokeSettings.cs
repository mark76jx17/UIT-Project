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
        Eraser,  // gomma: cancella/parzializza il tratto toccato
        Delete,  // X: cancella l'intero oggetto toccato
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
        public static float Alpha = 1f;

        /// <summary>Strumento corrente: penna, riempimento o gomma.</summary>
        public static ToolMode Tool = ToolMode.Pen;

        // Mano del pennello e mano della palette: impostate dal DrawingRig
        // (spunta "Left Handed" nell'Inspector per i mancini).
        public static OVRInput.Controller BrushHand = OVRInput.Controller.RTouch;
        public static OVRInput.Controller PaletteHand = OVRInput.Controller.LTouch;

        public static bool EraserMode => Tool == ToolMode.Eraser;
        public static bool FillMode => Tool == ToolMode.Fill;

        public static bool DeleteMode => Tool == ToolMode.Delete;

        /// <summary>Grigio "gomma" (semi-trasparente): unica fonte usata sia dall'anteprima
        /// colore (checker) sia dallo slider Size in modalità Cancella/Elimina, così il
        /// linguaggio visivo è coerente.</summary>
        public static readonly Color EraserSwatchColor = new(0.62f, 0.62f, 0.68f, 0.40f);

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


        // Stato della ruota dei colori. La "luminosità" (Val) è uno slider a 3 fermate:
        // 0 = nero, 0.5 = colore pieno (quello scelto sulla ruota), 1 = bianco.
        public static float Hue;
        public static float Sat;
        public static float Val = 0.5f; // 0 nero · 0.5 colore pieno · 1 bianco

        /// <summary>Colore "puro" scelto dalla ruota (tinta + saturazione a piena luminosità).</summary>
        public static Color PureColor => Color.HSVToRGB(Hue, Sat, 1f);

        public static void SetHSV(float hue, float sat, float val)
        {
            Hue = hue;
            Sat = sat;
            Val = val;
            ReactivatePen();
        }

        public static void SetColor(Color color)
        {
            Color.RGBToHSV(color, out var h, out var s, out _);
            Hue = h;
            Sat = s;
            Val = 0.5f; // i recenti sono colori pieni → luminosità neutra
            ReactivatePen();
        }

        // Scegliere un colore (ruota, luminosità, recenti, contagocce) mentre si è in
        // Gomma/Elimina riattiva il pennello: la zona colore resta sempre attiva in palette
        // (niente overlay di disattivazione, confondeva gli utenti) e toccarla esprime
        // l'intenzione di disegnare. La UI segue da sola (pulsante Draw, icona sulla punta,
        // controlli draw-only), quindi il passaggio è fluido. In Fill il colore serve al
        // secchiello: non si cambia strumento.
        static void ReactivatePen()
        {
            if (Tool == ToolMode.Eraser || Tool == ToolMode.Delete)
                Tool = ToolMode.Pen;
        }

        /// <summary>
        /// Colore base = colore puro modulato dalla luminosità: nero (Val 0) ↔ colore
        /// pieno (Val 0.5) ↔ bianco (Val 1).
        /// </summary>
        public static Color BaseColor
        {
            get
            {
                var pure = PureColor;
                return Val <= 0.5f
                    ? Color.Lerp(Color.black, pure, Val * 2f)
                    : Color.Lerp(pure, Color.white, (Val - 0.5f) * 2f);
            }
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

        // ---- Mano dominante (mancino/destro, persiste tra le sessioni) ----
        // Stesso schema dei colori recenti: PlayerPrefs + evento per aggiornare la UI.
        // Unica fonte di verità per BrushHand/PaletteHand: il setter li riassegna.

        const string LeftHandedKey = "drawing.leftHanded";
        static bool leftHanded;

        /// <summary>Notifica chi deve riconfigurarsi quando cambia la mano dominante
        /// (DrawingRig riaggancia pennello/palette, la palette rifà il layout).</summary>
        public static System.Action LeftHandedChanged;

        /// <summary>Mancino: pennello a sinistra, palette a destra. Scrivendo qui si
        /// applicano subito le mani, si salva la preferenza e si notifica la UI.</summary>
        public static bool LeftHanded
        {
            get => leftHanded;
            set
            {
                if (leftHanded == value)
                    return;
                leftHanded = value;
                ApplyHands();
                PlayerPrefs.SetInt(LeftHandedKey, value ? 1 : 0);
                PlayerPrefs.Save();
                LeftHandedChanged?.Invoke();
            }
        }

        /// <summary>Assegna BrushHand/PaletteHand in base a <see cref="LeftHanded"/>.</summary>
        static void ApplyHands()
        {
            BrushHand = leftHanded ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            PaletteHand = leftHanded ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        }

        /// <summary>Carica la preferenza salvata (chiamato all'avvio da DrawingRig).
        /// <paramref name="defaultValue"/> = valore di prima esecuzione (campo Inspector).</summary>
        public static void LoadLeftHanded(bool defaultValue)
        {
            leftHanded = PlayerPrefs.GetInt(LeftHandedKey, defaultValue ? 1 : 0) == 1;
            ApplyHands();
        }

        static bool ApproximatelyEqual(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.02f &&
            Mathf.Abs(a.g - b.g) < 0.02f &&
            Mathf.Abs(a.b - b.b) < 0.02f;
    }
}
