# Preview a PNG dei pannelli UI (palette / Options / shortcuts, EN + IT) — giugno 2026

> Tool Editor per **vedere i pannelli della UI come immagini** senza indossare il visore e
> senza entrare in Play (che con questo progetto fa crashare Unity). Estende l'approccio del
> preview delle sole scorciatoie a **tutti i pannelli e tutte le lingue**.

## Menu: `Tools/Drawing/Preview All Panels`

File: `Assets/Scripts/Drawing/Editor/PanelPreviews.cs` (`#if UNITY_EDITOR`).

Genera **6 PNG** in `Assets/Drawing/Previews/`, iterando **pannelli × lingue**:

```
palette-en.png    palette-it.png
options-en.png    options-it.png
shortcuts-en.png  shortcuts-it.png
```

Le lingue **non sono hardcoded**: il tool itera su `Localization.Languages` (le chiavi di
`Tables`), quindi aggiungendo una lingua escono i suoi PNG in automatico — coerente con la
filosofia di `Localization` ("aggiungi una lingua e tutto il resto segue").

### Come funziona

1. **Snapshot lingua**: salva `Localization.Current` e lo **ripristina nel `finally`**.
   Settare `Localization.Current` scrive in `PlayerPrefs`, quindi senza ripristino il tool
   cambierebbe la lingua salvata dall'utente.
2. Per ogni lingua → per ogni pannello: crea un `PaletteController` **usa-e-getta** su un
   `root` temporaneo, chiama l'entry-point editor del pannello, renderizza, salva il PNG e
   fa `DestroyImmediate(root)`. **Nessun GameObject resta in scena** (niente rischio del tipo
   doppione `PalettePreview` / "tutto grigio").
3. In Edit mode `Awake/Start` non girano: istanziare e chiamare i builder è sicuro,
   costruiscono solo mesh leggendo gli static di `StrokeSettings`/`Localization`.

### Entry-point editor in `PaletteController` (`#if UNITY_EDITOR`)

Costruiscono UN pannello e lo restituiscono attivo:

- `EditorBuildShortcutsPanel()` → `BuildShortcutsPanel` (preesistente).
- `EditorBuildMainPanel()` → `BuildPanel`. **Imposta esplicitamente il layer**
  (`SetLayerRecursively(panel, PaletteLayer)`): `BuildPanel` non lo fa da solo (in-app lo fa
  `Start`/`Rebuild` dopo), e la camera di anteprima filtra per `PaletteLayer`. Le strisce
  pennelli/azioni sono figlie di `panel`, quindi ritornare `panel` basta a inquadrarle tutte.
- `EditorBuildOptionsPanel()` → `BuildOptionsPanel` + `SetActive(true)` (il builder lo lascia
  disattivato perché in-app si apre col menu ☰).

### Render condiviso + framing automatico

`PanelPreviews.CapturePanel(root, panel, outFile, w, h)` è l'helper di render, usato **anche**
dal tool `Preview Shortcuts Panel` (refactor: niente più duplicazione del codice di render).

- **Camera ortografica figlia di `root`** (così il cleanup la elimina: niente camere orfane —
  vedi il bug "tutto grigio" in `ambiente-mr-pulizia.md`).
- **Inquadratura automatica sui `Renderer.bounds`** del pannello, con ~8% di padding: invece di
  un `orthoSize` fisso (andava bene solo per le scorciatoie), così palette (con strisce
  laterali), Options e Shortcuts sono inquadrati **ciascuno alla sua misura**, senza tarature.
- I pannelli guardano verso −Z: la camera sta sul lato −Z e guarda +Z. Sfondo grigio neutro
  `{0.30, 0.30, 0.33}` per leggere bene i pannelli scuri. Output 1150×980, AA 8×.

## In alternativa: pannelli come GameObject nella Scene view

`Tools/Drawing/Show Panels In Scene` (`ScenePanelPreview`) costruisce gli stessi pannelli come
**GameObject reali** nella scena, a griglia (colonne = tipi di pannello, righe = lingue), sotto
un root **`DrawingPanelPreview`**, e inquadra la selezione nella Scene view. Serve per
ispezionarli/orbitarli in Unity (non solo come immagine).

- **Sicurezza**: root e tutto il sotto-albero hanno `HideFlags.DontSave`, quindi **non vengono
  mai serializzati nel file `.unity`** anche salvando la scena — niente ripetizione del bug del
  doppione `PalettePreview` / "tutto grigio" (`ambiente-mr-pulizia.md`).
- Possibile perché nessuno script di disegno è `ExecuteAlways`/`ExecuteInEditMode`: in Edit mode
  i pannelli sono mesh statiche (niente `Update`/billboard che gira).
- **Oggetti transitori + autopulizia (importante)**: le label TMP creano istanze di materiale che
  NON sono `DontSave`; a un **domain reload** (ricompile degli script, ingresso in Play, processo
  di Build) quelle istanze vengono distrutte mentre i componenti TMP sopravvivono → `MissingReference
  Exception: ...Material... has been destroyed` **a ripetizione** (tutto lo stack dentro TMP, nessun
  nostro script). Per questo il preview si **autopulisce**: `[InitializeOnLoad]` sottoscrive
  `AssemblyReloadEvents.beforeAssemblyReload` e `playModeStateChanged (ExitingEditMode)`, più un
  `IPreprocessBuildWithReport` che lo rimuove prima di una Build. Se compare quell'errore, basta
  `Clear Panels In Scene` (o un ricompile).
- `Tools/Drawing/Clear Panels In Scene` rimuove il root a mano. `Show` ripulisce comunque un
  eventuale root precedente prima di ricostruire (no accumulo).
- Stessa gestione lingua del tool PNG (snapshot + ripristino di `Localization.Current`); il testo
  è "cotto" nel TMP al build, quindi i pannelli già creati non cambiano passando alla lingua dopo.

## Relazione con gli altri tool

- **Preview Shortcuts Panel** (`ShortcutsPanelPreview`) resta utile per iterare **solo** le
  scorciatoie nella lingua corrente (con la griglia anchor via `DebugAnchorGrid`); ora delega
  il render a `PanelPreviews.CapturePanel`.
- Import Controller Schematic e Bake Controller Images: vedi `shortcuts-controller.md` §4–§5.

## Verifica

- Compilazione 0 errori. `Tools/Drawing/Preview All Panels` esegue e logga
  "Anteprime salvate ... (2 lingue × 3 pannelli)". I 6 PNG sono stati ispezionati: palette
  completa con strisce e tool (Disegna/Riempi/Cancella/Elimina in IT), Options con riga
  "Lingua → Italiano", schema scorciatoie in EN/IT. Nessun GameObject lasciato in scena,
  lingua dell'utente invariata. Commit a carico dell'utente.
