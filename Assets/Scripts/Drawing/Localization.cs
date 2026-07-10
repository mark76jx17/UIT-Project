using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Localizzazione della UI (palette + menu). Stessa impostazione di
    /// <see cref="StrokeSettings"/>: classe statica, preferenza persistita su PlayerPrefs,
    /// evento per aggiornare la UI a runtime (la palette si ricostruisce nella nuova lingua).
    ///
    /// UNICA FONTE DI VERITÀ delle lingue disponibili = le chiavi di <see cref="Tables"/>.
    /// Per aggiungere una lingua basta aggiungere UN solo set di traduzioni a quel dizionario:
    /// la dropdown nel menu Options e tutta la UI la mostrano in automatico, senza toccare
    /// né la dropdown né la logica dei componenti (vedi PaletteController.BuildLanguageDropdown).
    /// </summary>
    public static class Localization
    {
        // Chiave riservata: nome della lingua mostrato nella dropdown, nella sua stessa lingua.
        const string NameKey = "language.name";
        const string PrefKey = "drawing.language";
        // Default = inglese: conserva il testo storico della UI finché l'utente non sceglie.
        const string DefaultLanguage = "en";

        // ===================================================================
        // AGGIUNGI UNA LINGUA QUI: un nuovo blocco ["xx"] = new() { ... } con le
        // stesse chiavi. Nient'altro da modificare in tutto il progetto.
        // ===================================================================
        static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
        {
            ["en"] = new Dictionary<string, string>
            {
                [NameKey]            = "English",
                // -- palette: colore / slider --
                ["brightness"]       = "Brightness",
                ["opacity"]          = "Opacity",
                ["size"]             = "Size",
                // -- palette: toggle --
                ["pressure"]         = "Pressure",
                ["mirror"]           = "Mirror",
                ["grid"]             = "Grid",
                ["line"]             = "Line",
                // -- palette: strumenti --
                ["draw"]             = "Draw",
                ["fill"]             = "Fill",
                ["erase"]            = "Erase",
                ["delete"]      = "Delete",
                // -- palette: tipi di pennello --
                ["brush.stroke"]     = "stroke",
                ["brush.ribbon"]     = "ribbon",
                ["brush.dashed"]     = "dashed",
                ["brush.glow"]       = "glow",
                // -- palette: striscia azioni --
                ["action.undo"]      = "undo",
                ["action.redo"]      = "redo",
                ["action.save"]      = "save",
                ["action.load"]      = "load",
                ["action.clearAll"]  = "delete all",
                // -- conferma "delete all" --
                ["confirm.clearAll"] = "Are you sure to delete all?",
                ["confirm.yes"]      = "Yes",
                ["confirm.no"]       = "No",
                // -- menu Options --
                ["options"]          = "Options",
                ["viewShortcuts"]    = "View Shortcuts",
                ["language"]         = "Language",
                // -- pannello Shortcuts --
                ["shortcuts"]        = "Shortcuts",
                ["shortcuts.hint"]   = "Works with the palette closed.",
                ["stick"]            = "Thumbstick",
                ["stick.click"]      = "Click",
                ["dir.up"]           = "Up",
                ["dir.down"]         = "Down",
                ["dir.left"]         = "Left",
                ["dir.right"]        = "Right",
                ["sc.tool"]          = "Tool",
                ["sc.brushPrev"]     = "Brush −",
                ["sc.brushNext"]     = "Brush +",
                ["sc.redo"]          = "Redo",
                ["sc.undo"]          = "Undo",
                ["sc.save"]          = "Save",
                ["sc.pressure"]      = "Pressure",
                ["sc.grid"]          = "Grid",
                ["sc.mirror"]        = "Mirror",
                ["sc.snap"]          = "Snap",
                ["sc.load"]          = "Load",
                ["sc.clearAll"]      = "Delete all (hold)",
            },
            ["it"] = new Dictionary<string, string>
            {
                [NameKey]            = "Italiano",
                ["brightness"]       = "Luminosità",
                ["opacity"]          = "Opacità",
                ["size"]             = "Spessore",
                ["pressure"]         = "Pressione",
                ["mirror"]           = "Specchio",
                ["grid"]             = "Griglia",
                ["line"]             = "Linea",
                ["draw"]             = "Disegna",
                ["fill"]             = "Riempi",
                ["erase"]            = "Cancella",
                ["delete"]           = "Elimina",
                ["brush.stroke"]     = "tratto",
                ["brush.ribbon"]     = "nastro",
                ["brush.dashed"]     = "trattegg.",
                ["brush.glow"]       = "bagliore",
                ["action.undo"]      = "annulla",
                ["action.redo"]      = "ripeti",
                ["action.save"]      = "salva",
                ["action.load"]      = "carica",
                ["action.clearAll"]  = "canc. tutto",
                ["confirm.clearAll"] = "Sei sicuro di voler cancellare tutto?",
                ["confirm.yes"]      = "Sì",
                ["confirm.no"]       = "No",
                ["options"]          = "Opzioni",
                ["viewShortcuts"]    = "Scorciatoie",
                ["language"]         = "Lingua",
                ["shortcuts"]        = "Scorciatoie",
                ["shortcuts.hint"]   = "Funziona a palette chiusa.",
                ["stick"]            = "Levetta",
                ["stick.click"]      = "Premi",
                ["dir.up"]           = "Su",
                ["dir.down"]         = "Giù",
                ["dir.left"]         = "Sinistra",
                ["dir.right"]        = "Destra",
                ["sc.tool"]          = "Strumento",
                ["sc.brushPrev"]     = "Pennello −",
                ["sc.brushNext"]     = "Pennello +",
                ["sc.redo"]          = "Ripeti",
                ["sc.undo"]          = "Annulla",
                ["sc.save"]          = "Salva",
                ["sc.pressure"]      = "Pressione",
                ["sc.grid"]          = "Griglia",
                ["sc.mirror"]        = "Specchio",
                ["sc.snap"]          = "Aggancia",
                ["sc.load"]          = "Carica",
                ["sc.clearAll"]      = "Cancella tutto (tieni)",
            },
        };

        // L'elenco delle lingue deriva dalle chiavi della tabella: aggiungere un blocco qui
        // sopra lo fa comparire ovunque (dropdown inclusa) senza altre modifiche.
        static readonly List<string> codes = new(Tables.Keys);

        /// <summary>Codici lingua disponibili, nell'ordine di dichiarazione in <see cref="Tables"/>.</summary>
        public static IReadOnlyList<string> Languages => codes;

        /// <summary>Notifica chi deve riaggiornare il testo quando cambia la lingua
        /// (la palette si ricostruisce nella nuova lingua).</summary>
        public static System.Action LanguageChanged;

        static string current = DefaultLanguage;

        /// <summary>Lingua corrente. Scrivendo qui si salva la preferenza e si notifica la UI.</summary>
        public static string Current
        {
            get => current;
            set
            {
                if (current == value || !Tables.ContainsKey(value))
                    return;
                current = value;
                PlayerPrefs.SetString(PrefKey, value);
                PlayerPrefs.Save();
                LanguageChanged?.Invoke();
            }
        }

        /// <summary>Carica la lingua salvata (chiamato all'avvio dal DrawingRig).</summary>
        public static void Load()
        {
            var saved = PlayerPrefs.GetString(PrefKey, DefaultLanguage);
            current = Tables.ContainsKey(saved) ? saved : DefaultLanguage;
        }

        /// <summary>Testo nella lingua corrente. Fallback: lingua di default, poi la chiave
        /// stessa (così una chiave mancante è visibile invece di sparire).</summary>
        public static string Get(string key)
        {
            if (Tables.TryGetValue(current, out var t) && t.TryGetValue(key, out var s))
                return s;
            if (Tables.TryGetValue(DefaultLanguage, out var d) && d.TryGetValue(key, out var ds))
                return ds;
            return key;
        }

        /// <summary>Nome di una lingua nella sua stessa lingua (per la dropdown).</summary>
        public static string LanguageName(string code)
            => Tables.TryGetValue(code, out var t) && t.TryGetValue(NameKey, out var n) ? n : code;
    }
}
