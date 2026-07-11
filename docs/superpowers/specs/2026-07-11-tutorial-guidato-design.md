# Tutorial guidato per utenti non esperti dell'app — design

**Data:** 2026-07-11
**Stato:** approvato (in attesa di review scritta prima del piano di implementazione)

## 1. Obiettivo e ambito

Aiutare un utente che **conosce già la VR ma non la nostra app** a diventare operativo, con un
**percorso guidato "impara facendo"** di **~5 step del nucleo essenziale**. Il tutorial:

- parte al **primo avvio** con una schermata di benvenuto (scelta lingua + Sì/No);
- è **interrompibile ad ogni step** e **riprendibile / ricominciabile** dal menu Options;
- **non insegna le basi VR** (guardarsi attorno, impugnare i controller): quello è fuori ambito.

Approccio implementativo: **controller a step dichiarativi con rilevamento del completamento via
polling dello stato pubblico esistente** (nessun evento nuovo nei sottosistemi, massimo
isolamento, niente refactor del codice di disegno/palette).

### Non-obiettivi (YAGNI)
- Niente insegnamento delle basi VR.
- Niente copertura di tutte le funzioni (gomma, elimina, riempimento, specchio, griglia, undo,
  salva/carica restano fuori dal percorso guidato; si scoprono da soli o con hint futuri).
- Niente modalità "su binari" che disabilita il resto dell'app durante uno step.

## 2. Flusso d'avvio (schermata di benvenuto)

Un **pannello world-space** fluttuante davanti all'utente (non ancorato alla palette), in tre
momenti sequenziali:

1. **Scelta lingua** — due **bandiere procedurali** cliccabili, 🇮🇹 Italia e 🇬🇧 Regno Unito,
   realizzate come quad con texture generata in codice (vedi §11), ciascuna con un `PaletteButton`.
   Al click: `Localization.Current = "it" | "en"` → tutta la UI passa alla lingua scelta
   (`LanguageChanged` è già gestito).
2. **Proposta** — testo "Vuoi fare il tutorial?" + due tasti **Sì / No** (riuso chiavi
   `confirm.yes` / `confirm.no`).
3. **Esito**:
   - **Sì** → chiude il welcome e avvia lo step 1.
   - **No** → chiude il welcome; l'app è libera. Salva `tutorial.proposed = true` così non
     ripropone ai prossimi avvii (resta lanciabile dal menu).

Il pannello riusa lo stile modale esistente (`RoundedMesh` + `PaletteButton` + registrazione in
`ModalRoot`), quindi è azionabile a distanza (ray) e da vicino (poke) come gli altri pannelli.

## 3. Architettura

Componenti nuovi, tutti isolati dal resto:

- **`TutorialController`** (`MonoBehaviour`, istanziato da `DrawingRig`): macchina a stati che
  guida una **lista ordinata di `TutorialStep`**. Possiede card, highlighter e welcome panel;
  gestisce persistenza, avanzamento, uscita/ripresa.
- **`TutorialStep`** (dato semplice, non `MonoBehaviour`): descrive uno step con
  - `titleKey` — chiave di localizzazione dell'istruzione;
  - `Target()` → `Transform` — l'oggetto da evidenziare (palette, punta pennello, uno slider…);
  - `EnterPrecondition()` — azione eseguita **all'ingresso** dello step per portare la palette
    nello stato atteso (vedi §5);
  - `IsComplete()` → `bool` — predicato valutato ogni frame, legge **solo stato pubblico
    esistente** (`StrokeHistory`, statici di `PaletteController`, `StrokeSettings`).
- **`TutorialCard`**: il cartello di testo (stile pannello esistente), ancorato in un punto comodo
  (davanti/sopra la mano-palette). Mostra il testo dello step corrente e due tasti persistenti:
  **Salta step** e **Esci**.
- **`TutorialHighlighter`**: evidenziatore spaziale riutilizzabile (alone/freccia pulsante),
  agganciato ogni frame al `Target()` dello step corrente. Opaco così occlude il passthrough.
- **`TutorialWelcomePanel`**: incapsula la schermata di benvenuto (bandiere + Sì/No) di §2.

### Ciclo per frame (quando un tutorial è in corso)
1. Mostra la card con il testo dello step; posiziona l'highlighter sul `Target()`.
2. Valuta `IsComplete()`.
3. Se completo → feedback (toast + impulso aptico) e **avanza** allo step successivo, eseguendone
   la `EnterPrecondition()`. Dopo l'ultimo → messaggio finale e chiusura (`state = Completed`).

## 4. Gli step del nucleo (ordine confermato)

| # | Istruzione (chiave) | Precondizione (stato palette) | Target evidenziato | Completato quando |
|---|---------------------|-------------------------------|--------------------|-------------------|
| 1 | "Disegna un tratto" | palette **chiusa** (non intralcia) | punta del pennello | il conteggio in `StrokeHistory` è **cresciuto** rispetto allo snapshot d'ingresso |
| 2 | "Apri la palette" (grilletto mano-palette) | chiusa | mano-palette | `PaletteController.IsOpen == true` |
| 3 | "Scegli un colore dalla ruota" | aperta, docked | ruota colore | `StrokeSettings.BaseColor` ≠ snapshot d'ingresso |
| 4 | "Regola lo spessore" | aperta, docked | slider Size | `StrokeSettings.FixedRadius` ≠ snapshot d'ingresso |
| 5 | "Avvicina la mano al bordo e col grip sposta la palette" | aperta | bordo palette | `PaletteController.Placed == true` |

Al termine: breve messaggio "Fatto! Buon disegno" e chiusura.

Note di rilevamento:
- Gli step 3 e 4 confrontano con uno **snapshot** catturato in `EnterPrecondition()` (il valore
  cambia = azione compiuta). Snapshot banale (una `Color` / un `float`).
- Lo step 1 legge il conteggio degli elementi in `StrokeHistory` (già pubblico).

## 5. Coerenza degli stati della palette

Ogni step, **entrando**, stabilisce la propria precondizione: se l'utente durante uno step porta
la palette in uno stato incompatibile con lo step successivo, il controller la **ripristina**
all'ingresso di quello step. Questo evita percorsi incoerenti (es. arrivare allo step "apri la
palette" con la palette già aperta, o allo step "sposta" con la palette chiusa/fissata).

Serve una **piccola API pubblica additiva** su `PaletteController`, collocata in una
**`partial class` in un file separato** (`PaletteController.Tutorial.cs`) per **non toccare il
file principale di fla52**:

```csharp
public partial class PaletteController
{
    public static bool IsOpen => instance != null && instance.isOpen;
    public void SetOpen(bool open);  // apre/chiude in modo esplicito (niente toggle "a indovinare")
    public void Redock();            // riporta a Docked se era Placed
}
```

- `isOpen` e `instance` sono già campi della classe → accessibili dalla partial.
- `SetOpen` imposta `isOpen` e sincronizza il feedback come fanno i toggle esistenti; `Redock`
  riusa la logica di re-dock già presente (`SetParent(HandAnchor)` + `placeMode = Docked`).
- **Nessuna logica di disegno o di palette viene modificata**: solo questi accessori.

## 6. Interrompi / riprendi / ricomincia

- La **card** espone sempre:
  - **Esci** → chiude il tutorial, salva lo step corrente (`state = Paused`, `step = N`).
  - **Salta step** → passa allo step successivo senza attendere il completamento.
- Nel menu **Options** (`BuildOptionsPanel`) compaiono voci contestuali:
  - se `state == NotStarted`: una voce **Tutorial** (avvia dallo step 1);
  - se `state == Paused`: **Riprendi tutorial** (dallo step salvato) + **Ricomincia tutorial**
    (dallo step 1);
  - se `state == Completed`: **Ricomincia tutorial**.
- Le voci del menu sono aggiunte in una **partial class** (§5, stesso file) per non toccare il
  file principale della palette; agiscono chiamando metodi pubblici del `TutorialController`.

## 7. Persistenza (PlayerPrefs)

Stesso pattern di `Localization` / `StrokeSettings`:

| Chiave | Tipo | Significato |
|--------|------|-------------|
| `tutorial.proposed` | bool (int 0/1) | il welcome è già stato mostrato almeno una volta |
| `tutorial.state` | int (enum `NotStarted/InProgress/Paused/Completed`) | stato del percorso |
| `tutorial.step` | int | indice dello step corrente/salvato |

"Ricomincia" azzera `state`/`step`. Il welcome all'avvio parte solo se `!tutorial.proposed`.

## 8. Localizzazione

Nuove chiavi in `Localization.Tables` (blocchi `it` ed `en`), stessa convenzione a chiavi:

- welcome: `tutorial.welcome.title` ("Vuoi fare il tutorial?"), (riuso `confirm.yes`/`confirm.no`);
- istruzioni step 1–5: `tutorial.step.draw`, `tutorial.step.open`, `tutorial.step.color`,
  `tutorial.step.size`, `tutorial.step.move`;
- controlli card: `tutorial.skip` ("Salta step"), `tutorial.exit` ("Esci");
- menu: `tutorial.menu.start` ("Tutorial"), `tutorial.menu.resume` ("Riprendi tutorial"),
  `tutorial.menu.restart` ("Ricomincia tutorial");
- fine: `tutorial.done` ("Fatto! Buon disegno").

## 9. File

**Nuovi:**
- `Assets/Scripts/Drawing/Tutorial/TutorialController.cs`
- `Assets/Scripts/Drawing/Tutorial/TutorialStep.cs`
- `Assets/Scripts/Drawing/Tutorial/TutorialCard.cs`
- `Assets/Scripts/Drawing/Tutorial/TutorialHighlighter.cs`
- `Assets/Scripts/Drawing/Tutorial/TutorialWelcomePanel.cs`
- `Assets/Scripts/Drawing/Palette/PaletteController.Tutorial.cs` (partial: accessori + voci menu)

**Toccati (minimo, additivo):**
- `Assets/Scripts/Drawing/DrawingRig.cs` — istanzia `TutorialController` e, al primo avvio,
  mostra il welcome se `!tutorial.proposed`.
- `Assets/Scripts/Drawing/Localization.cs` — nuove chiavi it/en.
- `documentazione/` — nuova pagina che documenta la feature (convenzione di progetto).

Il file principale `PaletteController.cs` **non viene modificato** (tutto il nuovo sta nella
partial), coerentemente con la richiesta di non pestare i piedi a fla52.

## 10. Bandiere procedurali

Le due bandiere sono **generate in codice** (niente asset esterni), coerentemente con lo stile
delle altre icone dell'app:

- **Italia**: tre bande verticali verde/bianco/rosso — una `Texture2D` riempita a fasce.
- **Regno Unito (Union Jack)**: croce di San Giorgio (rossa su bianco) + croci diagonali (San
  Andrea/San Patrizio) su fondo blu — disegnata proceduralmente come le altre texture generate
  (`DashTexture`/SDF): fasce e diagonali su una `Texture2D`. Se il Union Jack diagonale risultasse
  troppo oneroso, si ripiega su una versione stilizzata leggibile (croce + campo blu) — decisione
  in fase di implementazione, resta procedurale.

Ogni bandiera è un quad `RoundedMesh` con la texture generata, con `PaletteButton` per il click,
bordo sottile per leggibilità sul passthrough.

## 11. Rischi e mitigazioni

- **Disegno del Union Jack** proceduralmente non banale → mitigazione: versione stilizzata di
  fallback (§10), comunque riconoscibile.
- **Tocchi a `PaletteController`** (territorio fla52) → mitigazione: tutto in `partial class`
  separata, solo accessori additivi, file principale intatto.
- **Rilevamento via polling** rileva "un frame dopo" e richiede snapshot per gli step 3/4 →
  impatto nullo in pratica; snapshot è un `Color`/`float`.
- **Precondizioni che muovono la palette** potrebbero sorprendere l'utente se attivate durante
  un'azione → si applicano solo **all'ingresso** dello step, non a metà.

## 12. Verifica

Il collaudo in Play/visore è a carico dell'utente (Play mode non si avvia via MCP). Per ogni
step di implementazione: compilazione a **0 errori** via MCP e verifica visiva di card/highlighter/
bandiere con render locale (Python/PIL) dove utile, prima del test in visore. Sviluppo **a piccoli
passi**: un componente/step alla volta, spiegato e verificato in visore prima del successivo.
