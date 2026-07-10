# Clear (svuota tutto) reso annullabile con Undo — luglio 2026

> Fix di un bug segnalato dall'utente: **dopo una cancellazione totale della scena
> (pulsante Clear / "Svuota tutto") l'Undo non ripristinava nulla**. La cancellazione
> totale era per design irreversibile (recuperabile solo dal file di backup); ora è una
> singola azione annullabile dallo stesso Undo dei tratti.
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificati | `Geometry/DrawingStore.cs` |

## Sintomo

Disegnare qualcosa → **Clear** (svuota tutto) → **Undo**: la scena restava vuota, i
tratti non tornavano. L'unico recupero era rinominare `drawing_backup.json` in
`drawing.json` e premere **Load**.

## Root cause

Il pulsante **Clear** della palette (`PaletteController` → voce `"clear"` →
`DrawingStore.NewScene`), il tasto **N** del simulatore desktop e la scorciatoia da
controller chiamano tutti **`DrawingStore.NewScene()`**, che faceva due operazioni
**irreversibili**:

```csharp
public static void NewScene()
{
    SaveBackup();
    StrokeHistory.Clear();                    // ① svuota TUTTA la cronologia undo/redo
    foreach (var item in ...DrawnItem...)
        UnityEngine.Object.Destroy(...);      // ② DISTRUGGE davvero tutti gli oggetti
}
```

Dopo il Clear lo stack di undo era vuoto e gli oggetti **distrutti**: `StrokeHistory.Undo()`
trovava lo stack vuoto e non faceva nulla. La cancellazione totale **non era annullabile**
per costruzione.

## Fix

Rendere Clear una **singola azione di gomma annullabile**, riusando il meccanismo già
esistente per la gomma (`StrokeHistory.PushErase`): invece di distruggere gli oggetti, li
si **nasconde** (`SetActive(false)`) e si registra l'azione. Un solo **Undo** li riattiva
tutti in un colpo; **Redo** ri-svuota.

```csharp
public static void NewScene()
{
    SaveBackup();
    // Solo gli oggetti attivi (i visibili): gli inattivi sono già "undo-ati" nel
    // ramo redo e verranno liberati da PushErase→ClearRedo.
    var active = new List<GameObject>();
    foreach (var item in UnityEngine.Object.FindObjectsByType<DrawnItem>(FindObjectsSortMode.None))
        active.Add(item.gameObject);
    if (active.Count == 0)
        return;
    foreach (var go in active)
        go.SetActive(false); // nascondi, non distruggere: così Undo può riattivarli
    StrokeHistory.PushErase(active.ToArray()); // un solo passo di undo per tutta la scena
    Debug.Log($"[DrawingStore] Scena svuotata ({active.Count} oggetti, annullabile).");
}
```

Punti chiave, coerenti col modello della gomma già in `StrokeHistory`:

- **Nasconde invece di distruggere.** Gli oggetti restano in memoria (inattivi) finché
  l'azione è nello stack di undo. Quando l'azione esce dal tetto `MaxUndo` (200) il ramo
  di eviction li distrugge (`foreach (var o in old.removed) Object.Destroy(o)`) → nessun
  leak permanente.
- **Solo gli oggetti attivi.** Gli inattivi sono i tratti già annullati che stanno nel
  ramo *redo*; `PushErase → Add → ClearRedo()` li libera come per qualsiasi nuova azione.
- **Backup su file conservato.** `SaveBackup()` resta come ulteriore rete di sicurezza
  (recupero via rename + Load) oltre all'Undo in-app.

## Effetto collaterale (voluto)

Prima Clear azzerava anche la cronologia *precedente*. Ora, dopo aver annullato il Clear,
si può continuare a fare **Undo anche sulle azioni precedenti al Clear** — comportamento
più consistente e prevedibile (il Clear è un'azione come le altre, non una barriera).

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Test in Play/visore a carico dell'utente: disegna → Clear (scena vuota) → Undo (torna
  tutto) → Redo (ri-svuota). Verificare anche più Clear consecutivi e Undo oltre il Clear.

## Aggiornamento (2026-07-10) — conferma Sì/No prima dello svuotamento

Con il commit `1f3f2eb` (collaboratore) sia il bottone **clear** della palette sia l'hold
B/Y da controller non chiamano più `NewScene()` direttamente: aprono un **pannello modale
di conferma Sì/No** (`PaletteController.OpenConfirmClear` / `BuildConfirmClearPanel`,
stringhe `confirm.*` in `Localization`). Il **Sì** chiama `DrawingStore.NewScene()`, quindi
lo svuotamento resta **annullabile con Undo** come descritto sopra: le due protezioni si
sommano (conferma prima, Undo dopo).
