# Tutorial guidato — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aggiungere un tutorial guidato "impara facendo" (~5 step del nucleo) con schermata di benvenuto (lingua + Sì/No) al primo avvio, interrompibile/riprendibile/ricominciabile dal menu Options.

**Architecture:** Un `TutorialController` (MonoBehaviour, istanziato da `DrawingRig`) guida una lista dichiarativa di `TutorialStep`; il completamento di ogni step è rilevato via **polling dello stato pubblico esistente** (`StrokeHistory`, statici di `PaletteController`, `StrokeSettings`). UI (welcome, card, highlighter, bandiere) costruita in modo **isolato** con gli helper pubblici `RoundedMesh` + `BrushMaterials` + `PaletteButton`. La coerenza degli stati palette è garantita da precondizioni eseguite all'ingresso di ogni step, tramite una **piccola API pubblica additiva** su `PaletteController` in una `partial class` separata (file principale di fla52 intatto).

**Tech Stack:** Unity 6 (6000.4.6f1), URP, Meta XR SDK (OVR), C#, TextMeshPro, PlayerPrefs.

## Global Constraints

- **Workflow a piccoli passi:** una task alla volta, spiegata prima, **verificata in visore dall'utente** prima della successiva.
- **Play mode NON si avvia via MCP** (fa crashare Unity): il test in Play lo fa l'utente. Verifica automatica = compilazione **0 errori** via `refresh_unity` + `read_console` (types: [error]).
- **Commit:** li fa l'utente. Ogni "Commit" nel piano è un checkpoint da proporre all'utente, non un `git commit` eseguito da me.
- **Non modificare `PaletteController.cs`** (territorio di fla52): tutto il nuovo codice palette-side va in `PaletteController.Tutorial.cs` (partial).
- **Passthrough MR:** i pannelli/indicatori opachi devono scrivere alpha = 1 (usare `BrushMaterials.CreateUnlit(color, opaque:true)` / `MakeOpaque`), altrimenti "bucano" l'occlusione (see-through).
- **Documentazione:** a feature completata, una pagina in `documentazione/` (convenzione di progetto).
- **Localizzazione:** ogni stringa UI passa da `Localization.Get(key)`; nuove chiavi in `it` ed `en`.

---

### Task 1: Persistenza e stato del tutorial (`TutorialProgress`)

Classe statica su PlayerPrefs (stesso pattern di `Localization`/`StrokeSettings`). Nessuna UI, nessuna dipendenza: fondamenta testabili da sole.

**Files:**
- Create: `Assets/Scripts/Drawing/Tutorial/TutorialProgress.cs`

**Interfaces:**
- Produces:
  - `enum TutorialState { NotStarted, InProgress, Paused, Completed }`
  - `static bool TutorialProgress.Proposed { get; set; }`
  - `static TutorialState TutorialProgress.State { get; set; }`
  - `static int TutorialProgress.Step { get; set; }`
  - `static void TutorialProgress.Reset()` (State=NotStarted, Step=0)

- [ ] **Step 1: Creare il file**

```csharp
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

        public static bool Proposed
        {
            get => PlayerPrefs.GetInt(ProposedKey, 0) != 0;
            set { PlayerPrefs.SetInt(ProposedKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static TutorialState State
        {
            get => (TutorialState)PlayerPrefs.GetInt(StateKey, (int)TutorialState.NotStarted);
            set { PlayerPrefs.SetInt(StateKey, (int)value); PlayerPrefs.Save(); }
        }

        public static int Step
        {
            get => PlayerPrefs.GetInt(StepKey, 0);
            set { PlayerPrefs.SetInt(StepKey, value); PlayerPrefs.Save(); }
        }

        public static void Reset()
        {
            State = TutorialState.NotStarted;
            Step = 0;
        }
    }
}
```

- [ ] **Step 2: Compilazione** — `refresh_unity(compile:request, force)` poi `read_console(types:[error])`. Atteso: **0 errori**.

- [ ] **Step 3: Commit (checkpoint utente)** — messaggio suggerito: `feat(tutorial): stato persistente TutorialProgress`.

---

### Task 2: API pubblica additiva su `PaletteController` (partial)

Accessori per leggere/forzare lo stato palette dalle precondizioni degli step. Solo additivo, file principale intatto.

**Files:**
- Create: `Assets/Scripts/Drawing/Palette/PaletteController.Tutorial.cs`

**Interfaces:**
- Consumes: campi privati esistenti `isOpen` (bool), `instance` (static PaletteController), `placeMode` (PlaceMode), metodo privato `Redock()`, `UiFeedback.Instance`.
- Produces:
  - `static bool PaletteController.IsOpen`
  - `void PaletteController.SetOpen(bool open)`
  - `void PaletteController.RedockPublic()`
  - `static PaletteController PaletteController.Instance`

- [ ] **Step 1: Creare la partial**

```csharp
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    // Accessori additivi per il tutorial guidato. Il file principale (di fla52) resta intatto:
    // questa partial legge/forza lo stato palette per le precondizioni degli step.
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
            UiFeedback.Instance?.PanelToggle(isOpen);
        }

        /// <summary>Riaggancia la palette alla mano (da Placed a Docked), esposto per il tutorial.
        /// Riusa la logica privata di re-dock.</summary>
        public void RedockPublic()
        {
            if (placeMode == PlaceMode.Placed)
                Redock();
        }
    }
}
```

**Nota:** verificare che nel file principale la dichiarazione sia `public partial class PaletteController` (se è `public class`, l'unica modifica ammessa al file di fla52 è aggiungere la parola `partial` alla firma della classe — una parola sola, additiva). Questo si accerta al Passo 2.

- [ ] **Step 2: Verificare/abilitare `partial` nel file principale**
  - Leggere la firma della classe in `PaletteController.cs`. Se è `public class PaletteController`, cambiarla in `public partial class PaletteController` (unica modifica, additiva).
  - Compilazione `refresh_unity` + `read_console`. Atteso: **0 errori**.

- [ ] **Step 3: Commit (checkpoint utente)** — `feat(tutorial): accessori palette in partial class`.

---

### Task 3: Chiavi di localizzazione

Aggiungere le stringhe del tutorial ai blocchi `it` ed `en` di `Localization.Tables`.

**Files:**
- Modify: `Assets/Scripts/Drawing/Localization.cs` (dentro i dizionari `["en"]` e `["it"]`)

**Interfaces:**
- Produces (chiavi leggibili con `Localization.Get`): `tutorial.welcome.title`, `tutorial.step.draw`, `tutorial.step.open`, `tutorial.step.color`, `tutorial.step.size`, `tutorial.step.move`, `tutorial.skip`, `tutorial.exit`, `tutorial.menu.start`, `tutorial.menu.resume`, `tutorial.menu.restart`, `tutorial.done`.

- [ ] **Step 1: Aggiungere al blocco `["en"]`** (prima della chiusura `},` del blocco en)

```csharp
                // -- tutorial guidato --
                ["tutorial.welcome.title"] = "Do you want the tutorial?",
                ["tutorial.step.draw"]     = "Draw a stroke",
                ["tutorial.step.open"]     = "Open the palette (palette-hand trigger)",
                ["tutorial.step.color"]    = "Pick a color from the wheel",
                ["tutorial.step.size"]     = "Adjust the size",
                ["tutorial.step.move"]     = "Reach the edge and hold grip to move the palette",
                ["tutorial.skip"]          = "Skip step",
                ["tutorial.exit"]          = "Exit",
                ["tutorial.menu.start"]    = "Tutorial",
                ["tutorial.menu.resume"]   = "Resume tutorial",
                ["tutorial.menu.restart"]  = "Restart tutorial",
                ["tutorial.done"]          = "Done! Happy drawing",
```

- [ ] **Step 2: Aggiungere al blocco `["it"]`** (prima della chiusura `},` del blocco it)

```csharp
                // -- tutorial guidato --
                ["tutorial.welcome.title"] = "Vuoi fare il tutorial?",
                ["tutorial.step.draw"]     = "Disegna un tratto",
                ["tutorial.step.open"]     = "Apri la palette (grilletto mano-palette)",
                ["tutorial.step.color"]    = "Scegli un colore dalla ruota",
                ["tutorial.step.size"]     = "Regola lo spessore",
                ["tutorial.step.move"]     = "Avvicina la mano al bordo e col grip sposta la palette",
                ["tutorial.skip"]          = "Salta step",
                ["tutorial.exit"]          = "Esci",
                ["tutorial.menu.start"]    = "Tutorial",
                ["tutorial.menu.resume"]   = "Riprendi tutorial",
                ["tutorial.menu.restart"]  = "Ricomincia tutorial",
                ["tutorial.done"]          = "Fatto! Buon disegno",
```

- [ ] **Step 3: Compilazione** — 0 errori. **Commit** — `feat(tutorial): chiavi localizzazione it/en`.

---

### Task 4: Bandiere procedurali (`FlagTextures`)

Genera in codice due `Texture2D`: Italia (3 bande verticali verde/bianco/rosso) e Regno Unito (Union Jack; se la diagonale completa risulta onerosa, versione stilizzata leggibile — croce bianca+rossa su campo blu). Cache statica.

**Files:**
- Create: `Assets/Scripts/Drawing/Tutorial/FlagTextures.cs`

**Interfaces:**
- Produces: `static Texture2D FlagTextures.Italy()`, `static Texture2D FlagTextures.UnitedKingdom()`.

- [ ] **Step 1: Creare il file**

```csharp
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Bandiere generate in codice (nessun asset esterno), coerenti con lo stile procedurale
    /// delle altre texture dell'app. Cache statica: una Texture2D per bandiera.
    /// </summary>
    public static class FlagTextures
    {
        const int W = 96, H = 64;
        static Texture2D italy, uk;

        public static Texture2D Italy()
        {
            if (italy != null) return italy;
            italy = New();
            var green = new Color(0f, 0.56f, 0.27f);
            var white = Color.white;
            var red = new Color(0.81f, 0.13f, 0.16f);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = x < W / 3 ? green : x < 2 * W / 3 ? white : red;
                    px[y * W + x] = c;
                }
            italy.SetPixels(px); italy.Apply();
            return italy;
        }

        public static Texture2D UnitedKingdom()
        {
            if (uk != null) return uk;
            uk = New();
            var blue = new Color(0.0f, 0.14f, 0.45f);
            var white = Color.white;
            var red = new Color(0.80f, 0.10f, 0.18f);
            var px = new Color[W * H];
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
            // Rapporto larghezza/altezza per le diagonali (croce di Sant'Andrea).
            float k = (float)H / W;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    Color c = blue;
                    // Diagonali bianche (X), poi la loro versione rossa più sottile.
                    float diag = Mathf.Min(Mathf.Abs(dy - k * dx), Mathf.Abs(dy + k * dx));
                    if (diag < 9f) c = white;
                    if (diag < 4f) c = red;
                    // Croce di San Giorgio: banda verticale + orizzontale, bianca poi rossa.
                    bool crossWhite = Mathf.Abs(dx) < 10f || Mathf.Abs(dy) < 10f;
                    bool crossRed = Mathf.Abs(dx) < 5f || Mathf.Abs(dy) < 5f;
                    if (crossWhite) c = white;
                    if (crossRed) c = red;
                    px[y * W + x] = c;
                }
            uk.SetPixels(px); uk.Apply();
            return uk;
        }

        static Texture2D New() => new(W, H, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
    }
}
```

- [ ] **Step 2: Verifica visiva** — render locale con Python/PIL nella scratchpad replicando l'algoritmo per confermare che le bandiere siano riconoscibili prima del test in visore (l'algoritmo del Union Jack è tarabile qui). Compilazione 0 errori.

- [ ] **Step 3: Commit (checkpoint utente)** — `feat(tutorial): bandiere procedurali Italia/UK`.

---

### Task 5: Indicatore spaziale (`TutorialHighlighter`)

Alone/anello pulsante opaco, riposizionabile ogni frame su un `Transform` bersaglio, con offset e dimensione impostabili. Occlude il passthrough.

**Files:**
- Create: `Assets/Scripts/Drawing/Tutorial/TutorialHighlighter.cs`

**Interfaces:**
- Consumes: `BrushMaterials.CreateUnlit`, `RoundedMesh` (o primitiva).
- Produces: `TutorialHighlighter` (MonoBehaviour) con `void Show(Transform target, Vector3 worldOffset, float size)` e `void Hide()`.

- [ ] **Step 1: Creare il componente**

```csharp
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Alone pulsante che indica l'oggetto coinvolto nello step corrente del tutorial.
    /// Opaco (occlude il passthrough); segue il target ogni frame finché è mostrato.
    /// </summary>
    public class TutorialHighlighter : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color HiColor = new(0.30f, 0.75f, 1f, 1f); // ciano "guida"

        Transform target;
        Vector3 offset;
        Material mat;
        Transform disc;

        void Awake()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            mat = BrushMaterials.CreateUnlit(HiColor, opaque: true);
            go.GetComponent<MeshRenderer>().material = mat;
            disc = go.transform;
            gameObject.SetActive(false);
        }

        public void Show(Transform t, Vector3 worldOffset, float size)
        {
            target = t; offset = worldOffset;
            disc.localScale = Vector3.one * size;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            target = null;
            gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (target == null) return;
            transform.position = target.position + offset;
            if (Camera.main != null)
                disc.rotation = Quaternion.LookRotation(disc.position - Camera.main.transform.position);
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 5f);
            var c = HiColor * pulse; c.a = 1f;
            mat.SetColor(BaseColorId, c);
        }
    }
}
```

- [ ] **Step 2: Compilazione 0 errori. Commit (checkpoint)** — `feat(tutorial): highlighter spaziale`.

---

### Task 6: Modello degli step (`TutorialStep`) + definizione dei 5 step

Dato semplice con delegati; la lista dei 5 step del nucleo la fornisce un factory statico. Le precondizioni usano l'API di Task 2; il completamento legge stato pubblico.

**Files:**
- Create: `Assets/Scripts/Drawing/Tutorial/TutorialStep.cs`

**Interfaces:**
- Consumes: `PaletteController.Instance/IsOpen/SetOpen/RedockPublic/Placed`, `StrokeSettings.BaseColor/FixedRadius`, `StrokeHistory` (conteggio pubblico — verificare il nome esatto del membro al Passo 1), `BrushController.Tip`, `ColorWheel`/`SizeSlider` transform per il target.
- Produces:
  - `class TutorialStep { string TitleKey; Func<Transform> Target; Action Enter; Func<bool> IsComplete; }`
  - `static List<TutorialStep> TutorialStep.CoreSteps(BrushController brush, PaletteController palette)`

- [ ] **Step 1: Verificare l'API di conteggio di `StrokeHistory`** — aprire `Assets/Scripts/Drawing/Geometry/StrokeHistory.cs` e individuare il membro pubblico che espone quanti elementi/tratti esistono (es. `Count`). Usarlo nel predicato dello step 1. Individuare anche i `Transform` bersaglio della ruota colore (`ColorWheel`) e dello slider size (`SizeSlider`) — se non facilmente raggiungibili, usare come target la palette stessa (`palette.transform`) per lo step 3/4 (rifinibile in visore).

- [ ] **Step 2: Creare il file**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>Uno step del tutorial: istruzione, bersaglio da evidenziare, precondizione
    /// (stato palette) e predicato di completamento (legge solo stato pubblico esistente).</summary>
    public class TutorialStep
    {
        public string TitleKey;
        public Func<Transform> Target;
        public Action Enter;        // stabilisce la precondizione + eventuale snapshot
        public Func<bool> IsComplete;

        /// <summary>I 5 step del nucleo, nell'ordine confermato in spec.</summary>
        public static List<TutorialStep> CoreSteps(BrushController brush, PaletteController palette)
        {
            int strokeSnap = 0;
            Color colorSnap = default;
            float sizeSnap = 0f;

            return new List<TutorialStep>
            {
                // 1) Disegna un tratto — palette chiusa.
                new TutorialStep {
                    TitleKey = "tutorial.step.draw",
                    Target = () => brush != null ? brush.Tip : null,
                    Enter = () => { palette.SetOpen(false); strokeSnap = StrokeHistory.Count; },
                    IsComplete = () => StrokeHistory.Count > strokeSnap,
                },
                // 2) Apri la palette — precondizione: chiusa.
                new TutorialStep {
                    TitleKey = "tutorial.step.open",
                    Target = () => palette.transform,
                    Enter = () => palette.SetOpen(false),
                    IsComplete = () => PaletteController.IsOpen,
                },
                // 3) Cambia colore — aperta, docked.
                new TutorialStep {
                    TitleKey = "tutorial.step.color",
                    Target = () => palette.transform,
                    Enter = () => { palette.RedockPublic(); palette.SetOpen(true); colorSnap = StrokeSettings.BaseColor; },
                    IsComplete = () => StrokeSettings.BaseColor != colorSnap,
                },
                // 4) Cambia spessore — aperta, docked.
                new TutorialStep {
                    TitleKey = "tutorial.step.size",
                    Target = () => palette.transform,
                    Enter = () => { palette.RedockPublic(); palette.SetOpen(true); sizeSnap = StrokeSettings.FixedRadius; },
                    IsComplete = () => !Mathf.Approximately(StrokeSettings.FixedRadius, sizeSnap),
                },
                // 5) Sposta la palette — aperta.
                new TutorialStep {
                    TitleKey = "tutorial.step.move",
                    Target = () => palette.transform,
                    Enter = () => { palette.RedockPublic(); palette.SetOpen(true); },
                    IsComplete = () => PaletteController.Placed,
                },
            };
        }
    }
}
```

- [ ] **Step 3: Compilazione 0 errori** (risolvere il nome esatto di `StrokeHistory.Count`/`StrokeSettings.FixedRadius` se differiscono). **Commit (checkpoint)** — `feat(tutorial): modello step + 5 step del nucleo`.

---

### Task 7: Cartello dello step (`TutorialCard`)

Pannello world-space con testo dell'istruzione + due `PaletteButton`: "Salta step" e "Esci". Costruito con `RoundedMesh`/`BrushMaterials`/`PaletteButton`, layer palette per l'interazione ray/poke.

**Files:**
- Create: `Assets/Scripts/Drawing/Tutorial/TutorialCard.cs`

**Interfaces:**
- Consumes: `RoundedMesh`, `BrushMaterials`, `PaletteButton`, `Localization.Get`, `PaletteController.PaletteLayer`, TextMeshPro.
- Produces: `TutorialCard` con `void SetText(string)`, eventi `Action OnSkip`, `Action OnExit`, `void SetAnchor(Transform)`, `void Hide()/Show()`.

- [ ] **Step 1: Creare il componente** (costruzione pannello + label TMP + due bottoni cliccabili; ancoraggio davanti/sopra la mano-palette via `SetAnchor`). Riferimento di costruzione: stessa ricetta di `MakeRounded`/`MakeRoundedButton` in `PaletteController` ma con gli helper pubblici. Assegnare `SetLayerRecursively`-equivalente impostando `gameObject.layer = PaletteController.PaletteLayer` su root e figli con collider, così ray/poke funzionano. Testo via `TextMeshPro`. I due bottoni chiamano `OnSkip`/`OnExit`.

```csharp
using System;
using TMPro;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>Cartello del tutorial: testo dell'istruzione corrente + bottoni Salta/Esci.
    /// Ancorato davanti alla mano-palette. Interagibile via ray/poke (layer palette).</summary>
    public class TutorialCard : MonoBehaviour
    {
        public Action OnSkip, OnExit;
        Transform anchor;
        TextMeshPro label;

        // Costruzione: un quad arrotondato opaco come sfondo, una label TMP, due bottoni.
        public void Build()
        {
            var bg = new GameObject("CardBg");
            bg.transform.SetParent(transform, false);
            bg.AddComponent<MeshFilter>().mesh = RoundedMesh.Quad(0.20f, 0.10f, 0.012f);
            bg.AddComponent<MeshRenderer>().material =
                BrushMaterials.CreateUnlit(new Color(0.12f, 0.12f, 0.14f), opaque: true);

            var textGO = new GameObject("CardText");
            textGO.transform.SetParent(transform, false);
            textGO.transform.localPosition = new Vector3(0f, 0.02f, -0.004f);
            label = textGO.AddComponent<TextMeshPro>();
            label.fontSize = 2.2f; label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(0.18f, 0.05f);

            MakeButton("tutorial.skip", new Vector3(-0.05f, -0.03f, -0.004f), () => OnSkip?.Invoke());
            MakeButton("tutorial.exit", new Vector3(0.05f, -0.03f, -0.004f), () => OnExit?.Invoke());

            SetLayer(gameObject, PaletteController.PaletteLayer);
            gameObject.SetActive(false);
        }

        void MakeButton(string key, Vector3 pos, Action onPress)
        {
            var b = new GameObject(key);
            b.transform.SetParent(transform, false);
            b.transform.localPosition = pos;
            b.AddComponent<MeshFilter>().mesh = RoundedMesh.Quad(0.08f, 0.03f, 0.008f);
            b.AddComponent<MeshRenderer>().material =
                BrushMaterials.CreateUnlit(new Color(0.25f, 0.28f, 0.34f), opaque: true);
            var col = b.AddComponent<BoxCollider>();
            col.isTrigger = true; col.size = new Vector3(0.08f, 0.03f, 0.02f);
            b.AddComponent<Rigidbody>().isKinematic = true;
            var pb = b.AddComponent<PaletteButton>();
            pb.OnPressed = onPress; // verificare nome esatto callback in PaletteButton al Passo 1

            var t = new GameObject("txt");
            t.transform.SetParent(b.transform, false);
            t.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            var tl = t.AddComponent<TextMeshPro>();
            tl.text = Localization.Get(key); tl.fontSize = 1.4f;
            tl.alignment = TextAlignmentOptions.Center;
            tl.rectTransform.sizeDelta = new Vector2(0.075f, 0.028f);
        }

        public void SetText(string s) { if (label != null) label.text = s; }
        public void SetAnchor(Transform a) { anchor = a; }
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        void LateUpdate()
        {
            if (anchor == null) return;
            transform.position = anchor.position + anchor.forward * 0.18f + Vector3.up * 0.06f;
            if (Camera.main != null)
                transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }
    }
}
```

- [ ] **Step 1a: Verificare l'API reale** — `PaletteButton` (nome del callback: `OnPressed`? un metodo `Press()`? un `UnityEvent`?) e `RoundedMesh` (esiste `Quad(w,h,corner)`? altrimenti usare il metodo pubblico reale, es. `RoundedMesh.TexturedQuad`). Adeguare il codice ai nomi reali.
- [ ] **Step 2: Compilazione 0 errori. Commit (checkpoint)** — `feat(tutorial): cartello step con Salta/Esci`.

---

### Task 8: Schermata di benvenuto (`TutorialWelcomePanel`)

Pannello con due bandiere cliccabili (imposta `Localization.Current`) e, in seconda battuta, titolo + Sì/No.

**Files:**
- Create: `Assets/Scripts/Drawing/Tutorial/TutorialWelcomePanel.cs`

**Interfaces:**
- Consumes: `FlagTextures`, `Localization`, `PaletteButton`, `RoundedMesh`, `BrushMaterials`, TextMeshPro.
- Produces: `TutorialWelcomePanel` con `void Build(Action onYes, Action onNo)` e `void Show()/Hide()`. Le bandiere quad usano `MeshRenderer` con materiale unlit + `mainTexture = FlagTextures.Italy()/UnitedKingdom()`.

- [ ] **Step 1: Creare il componente** — struttura analoga a `TutorialCard`: sfondo, titolo `Localization.Get("tutorial.welcome.title")`, due quad-bandiera con `BoxCollider` trigger + `PaletteButton` (click → `Localization.Current = "it"/"en"`), due bottoni `confirm.yes`/`confirm.no` → `onYes`/`onNo`. Layer palette. Le texture bandiera si assegnano al materiale via `material.mainTexture` (o `SetTexture("_BaseMap", tex)` per URP Unlit). Posizionato davanti allo sguardo all'avvio.

- [ ] **Step 2: Verifica visiva bandiere (render locale) + compilazione 0 errori. Commit (checkpoint)** — `feat(tutorial): schermata benvenuto lingua + Sì/No`.

---

### Task 9: Controller e stato macchina (`TutorialController`) + hook in `DrawingRig`

Lega tutto: welcome all'avvio, avanzamento step, card+highlighter, persistenza, uscita.

**Files:**
- Create: `Assets/Scripts/Drawing/Tutorial/TutorialController.cs`
- Modify: `Assets/Scripts/Drawing/DrawingRig.cs` (istanzia il controller in `Start()`, dopo pennello/palette; passa `brush`, `palette`, `eyeAnchor`, `PaletteAnchor()`).

**Interfaces:**
- Consumes: `TutorialProgress`, `TutorialStep.CoreSteps`, `TutorialCard`, `TutorialHighlighter`, `TutorialWelcomePanel`, `Localization`, `ToastController` (facolt.).
- Produces:
  - `TutorialController` con proprietà iniettabili `BrushController Brush`, `PaletteController Palette`, `Transform Head`, `Transform PaletteHand`.
  - Metodi pubblici per il menu Options (Task 10): `void StartFromBeginning()`, `void Resume()`, `void ShowWelcome()`.

- [ ] **Step 1: Creare il controller** — macchina a stati:
  - `Start()`: crea card/highlighter/welcome (chiamando i rispettivi `Build`). Se `!TutorialProgress.Proposed` → `ShowWelcome()`. Altrimenti se `State == Paused` non fa nulla (si riprende dal menu).
  - `ShowWelcome()`: mostra il welcome; su **Sì** → `TutorialProgress.Proposed = true; StartFromBeginning()`; su **No** → `TutorialProgress.Proposed = true`, nasconde.
  - `StartFromBeginning()`: `steps = TutorialStep.CoreSteps(Brush, Palette); index = 0; State = InProgress;` entra nello step 0.
  - `Resume()`: `index = TutorialProgress.Step;` entra in quello step.
  - `EnterStep(i)`: `steps[i].Enter(); TutorialProgress.Step = i; card.SetAnchor(PaletteHand); card.SetText(Localization.Get(steps[i].TitleKey)); card.Show();`
  - `Update()`: se in corso, aggiorna highlighter sul `steps[index].Target()`; se `steps[index].IsComplete()` → feedback e `Advance()`.
  - `Advance()`: `index++`; se oltre l'ultimo → `Finish()`; altrimenti `EnterStep(index)`.
  - `Finish()`: `State = Completed; card.Hide(); highlighter.Hide();` toast `tutorial.done`.
  - `ExitTutorial()`: `State = Paused; card.Hide(); highlighter.Hide();` (step già salvato).
  - Collega `card.OnSkip = () => Advance(); card.OnExit = () => ExitTutorial();`.
  - `LanguageChanged`: alla card in corso, `card.SetText(...)` aggiornato (iscriversi a `Localization.LanguageChanged`, desottoscriversi in `OnDestroy`).

- [ ] **Step 2: Hook in `DrawingRig.Start()`** — dopo la creazione di palette/brush:

```csharp
            // Tutorial guidato: al primo avvio propone il welcome; riprendibile dal menu Options.
            var tutorialGO = new GameObject("Tutorial");
            var tutorial = tutorialGO.AddComponent<TutorialController>();
            tutorial.Brush = brush;
            tutorial.Palette = palette;
            tutorial.Head = eyeAnchor;
            tutorial.PaletteHand = paletteAnchor;
```

- [ ] **Step 3: Compilazione 0 errori.** **Verifica in visore (utente):** primo avvio mostra welcome; Sì avvia gli step; disegnando/aprendo/cambiando colore-size/spostando la palette gli step avanzano; Esci mette in pausa. **Commit (checkpoint)** — `feat(tutorial): controller macchina a stati + hook DrawingRig`.

---

### Task 10: Voci nel menu Options (partial)

Aggiungere al menu Options le voci contestuali Tutorial/Riprendi/Ricomincia, in `PaletteController.Tutorial.cs` (partial), senza toccare il file principale. Le voci chiamano il `TutorialController`.

**Files:**
- Modify: `Assets/Scripts/Drawing/Palette/PaletteController.Tutorial.cs`

**Interfaces:**
- Consumes: `TutorialProgress.State`, `TutorialController` (riferimento statico o via `FindAnyObjectByType`), gli helper privati di costruzione voce menu (`MakeRoundedButton`/`MakeLabel`) accessibili dalla partial.
- Produces: `void BuildTutorialMenuEntry(Transform optionsParent, ...)` chiamato dalla costruzione del menu.

- [ ] **Step 1: Verificare l'aggancio** — poiché `BuildOptionsPanel` è nel file principale (non modificabile), scegliere il punto di iniezione minimale: esporre dalla partial un metodo pubblico `AddTutorialOptions()` che il `TutorialController` chiama **dopo** che il menu è costruito, aggiungendo i propri bottoni come figli di `optionsPanel` (campo privato accessibile dalla partial). Le voci si (ri)creano ad ogni apertura del menu o si aggiornano via `optionsSync`. Definire i dettagli leggendo `optionsPanel`/`optionsSync` nel file principale.
- [ ] **Step 2: Implementare le voci contestuali** in base a `TutorialProgress.State` (NotStarted→"Tutorial"; Paused→"Riprendi"+"Ricomincia"; Completed→"Ricomincia"), ciascuna con callback verso `TutorialController.StartFromBeginning()/Resume()`.
- [ ] **Step 3: Compilazione 0 errori.** Verifica in visore (utente): menu mostra le voci giuste; Riprendi/Ricomincia funzionano. **Commit (checkpoint)** — `feat(tutorial): voci menu Options riprendi/ricomincia`.

---

### Task 11: Documentazione

**Files:**
- Create: `documentazione/tutorial-guidato.md`

- [ ] **Step 1: Scrivere la pagina** — scopo, flusso (welcome→step→fine), architettura (controller + step dichiarativi + polling), coerenza stati palette (precondizioni + API partial), persistenza PlayerPrefs, localizzazione, bandiere procedurali, elenco file. Collegare alla spec/piano.
- [ ] **Step 2: Commit (checkpoint utente)** — `docs: tutorial guidato`.

---

## Self-Review

**Spec coverage:**
- §2 welcome (lingua+Sì/No) → Task 4 (bandiere) + Task 8 (welcome) + Task 9 (avvio). ✓
- §3 architettura (controller/step/card/highlighter/welcome) → Task 5,6,7,8,9. ✓
- §4 i 5 step → Task 6. ✓
- §5 coerenza stati + API partial → Task 2 + precondizioni in Task 6. ✓
- §6 interrompi/riprendi/ricomincia → Task 7 (Esci/Salta) + Task 9 (pausa/finish) + Task 10 (menu). ✓
- §7 persistenza → Task 1. ✓
- §8 localizzazione → Task 3. ✓
- §9 file → coperti. ✓
- §10 bandiere procedurali → Task 4. ✓

**Placeholder scan:** i punti "verificare l'API reale" (Task 6 `StrokeHistory.Count`, Task 7 `PaletteButton`/`RoundedMesh`, Task 10 aggancio menu) sono **step di verifica espliciti** contro il codice esistente, non TODO aperti: il codice mostrato è la forma attesa, da confermare/adeguare ai nomi reali al primo step della task. Nessun "TBD/implement later".

**Type consistency:** `SetOpen`/`RedockPublic`/`IsOpen`/`Placed`/`Instance` (Task 2) usati coerentemente in Task 6/9; `TutorialProgress.State/Step/Proposed/Reset` (Task 1) usati in Task 9/10; `TutorialStep.CoreSteps(brush, palette)` (Task 6) consumato in Task 9; `card.OnSkip/OnExit/SetText/SetAnchor/Show/Hide` (Task 7) usati in Task 9.

**Rischi noti da confermare in implementazione:** nomi esatti di `StrokeHistory` (conteggio), `StrokeSettings.FixedRadius`, callback di `PaletteButton`, API di `RoundedMesh`, e punto di iniezione del menu Options. Tutti risolti al primo step della rispettiva task leggendo il codice reale.
