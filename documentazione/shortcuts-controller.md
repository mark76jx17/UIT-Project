# Scorciatoie da controller + pannello "Shortcuts" con schema line-art (giugno 2026)

> Rifinitura delle scorciatoie da controller (palette usabile a pannello chiuso) e
> **riprogettazione del pannello "View Shortcuts"**: da due colonne di testo a una
> rappresentazione con **un'unica immagine schematica** (line-art dei due controller)
> centrata, con le etichette collegate ai tasti tramite linee-guida e anelli. Più i fix
> di interazione emersi nei test in visore (toggle del menu, raggio, pallino accidentale,
> icona ✕, altezza palette).

## 1. Pannelli orientati verso l'utente (`FacePlayer`)

I sotto-pannelli modali **Options** e **Shortcuts**, aprendosi di lato, giacevano sul
piano della palette e si leggevano male. Un piccolo `MonoBehaviour` (`FacePlayer`) li
orienta verso la testa (`Camera.main`) a ogni `LateUpdate` con uno Slerp esponenziale
(niente jitter); punta subito la testa all'apertura (`OnEnable`). Tocca solo la
**rotazione**: la posizione resta gestita da `PositionMenus`. La palette NON è influenzata.

## 2. Menu Options sul tasto ☰, come toggle unico

L'apertura/chiusura del menu Options è sul **tasto menu "classico"**
(`OVRInput.Button.Start`), fisicamente solo sul **Touch sinistro** (indipendente dalla
mano dominante). `ToggleOptionsPanel` è un **toggle unico** (vale anche per il bottone
"..." della striscia azioni):

- se è aperto **qualcosa** del menu (Options, o le Shortcuts che gli stanno sopra) →
  chiude **tutto** (`optionsOpen=false`, `shortcutsOpen=false`), la palette resta aperta;
- altrimenti → apre Options e, **se la palette era chiusa, la apre con sé** (`isOpen=true`).

`CloseOptionsPanel` (✕ del menu) chiude anche un'eventuale Shortcuts sopra. `SetActive`/
`ModalRoot` restano sincronizzati ogni frame da `AnimateVisibility`, quindi il toggle
tocca solo i flag di stato.

## 3. `ControllerShortcuts.All` per tasto fisico

La tabella unica è `(string Hand, Btn Button, string Action)`, dove `Btn` è il **tasto
fisico** (`StickClick/StickUp/StickDown/StickLeft/StickRight/FaceA/FaceB/Menu`). Così il
pannello sa DOVE puntare la linea-guida. Resta l'unica fonte di verità del comportamento
e del diagramma; `ActionFor(role, Btn)` la interroga.

## 4. Pannello "Shortcuts": un solo schema line-art centrato

- **Una sola immagine** (`Resources/Controllers/schematic`) coi due controller disegnati
  in modo semplice (stick + X/Y a sinistra, A/B a destra), centrata; attorno le etichette
  delle scorciatoie con **linea-guida + anello** sul tasto.
- Il **ruolo** (mano palette / mano pennello) di ciascun controller fisico dipende dalla
  mano dominante: `leftIsPalette = StrokeSettings.PaletteHand == LTouch`. Il **Menu** (☰) è
  sempre cercato sul controller fisico sinistro.
- Costruzione: `BuildSchematicDiagram` (carica lo schema, mappa frazioni→locale con `P(f)`),
  `StickBlock` (blocco "Thumbstick": click + 4 direzioni, a lato), `TagV` (etichetta
  sopra/sotto un tasto con leader verticale). Anelli `AnchorDot` (disco accent + disco
  interno scuro), linee `Leader` (barra sottile ruotata).
- **Anchor dei tasti** = frazioni 0..1 dell'immagine, tarate con `DebugAnchorGrid`:
  `SchLStick/SchY/SchLMenu` (sx), `SchRStick/SchB` (dx).
- La **✕** di chiusura usa l'icona procedurale `close` (vedi §7), non più la lettera del
  font.

### Import dello schema (nero-su-bianco → bianco-su-trasparente)

Il sorgente è line-art **nero su sfondo bianco opaco**. `SchematicImporter`
(menu **Tools/Drawing/Import Controller Schematic**) lo converte in **linee bianche su
trasparente** così si legge sul pannello scuro:

```
alpha = alphaSorgente * (1 - luminanza)   // tratto nero → opaco; sfondo bianco → trasparente
colore = bianco
```

Funziona sia con sfondo bianco opaco sia con sfondo già trasparente. Output in
`Assets/Drawing/Resources/Controllers/schematic.png`. (Senza questa conversione lo schema
appariva come un **rettangolo bianco pieno**.)

## 5. Tool da Editor (Edit mode, niente Play)

- **Tools/Drawing/Import Controller Schematic** (`SchematicImporter`) — vedi §4.
- **Tools/Drawing/Preview Shortcuts Panel** (`ShortcutsPanelPreview`) — costruisce SOLO il
  pannello (`PaletteController.EditorBuildShortcutsPanel`, guardia `#if UNITY_EDITOR`) e lo
  renderizza in `Assets/Drawing/Previews/shortcuts.png` per iterare il layout senza visore.
  Flag `PaletteController.DebugAnchorGrid` per disegnare la griglia di calibrazione anchor
  (default **false**). Il render è delegato all'helper condiviso `PanelPreviews.CapturePanel`.
  - **IMPORTANTE**: la `PreviewCam` del tool è **figlia di `root`**, così il
    `DestroyImmediate(root)` la elimina. (Bug risolto: prima era orfana e, col background
    grigio opaco, se la scena veniva salvata restava in scena coprendo tutto il passthrough
    → "tutto grigio". Vedi `documentazione/ambiente-mr-pulizia.md`.)
- **Tools/Drawing/Preview All Panels** (`PanelPreviews`) — genera i PNG di **tutti** i pannelli
  (palette / Options / shortcuts) in **tutte** le lingue, in un colpo solo. Dettagli completi
  in `documentazione/preview-pannelli.md`.
- `ShortcutControllerBaker` (Bake Controller Images) è **superato**: renderizzava i modelli
  FBX dei Touch Pro, approccio abbandonato a favore dello schema line-art. I PNG
  `touch-left/right.png` non sono più usati dal pannello.

## 6. Raggio (`PaletteRay`): raggio visibile sul modale + niente pallino accidentale

Due problemi emersi sul pannello Shortcuts (che ha **solo la ✕** come controllo):

1. **Raggio invisibile** se non puntavi esattamente la ✕ (il resto del pannello non ha
   collider). → Aggiunto un **collider di superficie** sul fondo dei pannelli modali
   (`AddModalSurfaceCollider`, BoxCollider trigger sottile dietro i pulsanti) e un fallback
   `TryHitModalSurface` in `PaletteRay`: con un modale aperto, se il ray non colpisce un
   controllo ma punta il pannello, il **raggio resta visibile su tutta l'area** (lo sfondo
   non è premibile: niente hover/press).
2. **Pallino di colore accidentale** premendo la ✕/View Shortcuts: il bersaglio spariva
   mentre il trigger era ancora premuto, `onPalette` tornava falso e il pennello disegnava
   un punto. → `SuppressDrawing` ora è **latchato**: a trigger premuto vale la modalità
   iniziata (`pressed ? startedOnPalette : onPalette`), quindi se la pressione è iniziata
   sulla palette il disegno resta soppresso **finché non si rilascia**.

## 7. Icona ✕ procedurale + altezza palette

- **Icona ✕** (`ToolIcon.Get("close")` → `Cross()`): due segmenti diagonali SDF con
  estremità morbide, molto più pulita della lettera "X" del font. Usata su entrambi i
  bottoni di chiusura (Options e Shortcuts) via `MakeIconImage`.
- **Altezza palette**: `heightAboveHand` **0.22 → 0.30**. Prima il bordo inferiore del
  pannello (alto 0.48, metà 0.24) finiva ~2 cm *sotto* la mano e il controller che la
  tiene lo trapassava; ora resta ~6 cm sopra la mano.

## File toccati

- `Drawing/FacePlayer.cs` — billboard del pannello verso la testa.
- `Drawing/ControllerShortcuts.cs` — menu su `Button.Start` (LTouch); enum `Btn`; `All`
  per tasto fisico.
- `Drawing/Geometry/GrabController.cs` — commento "duplica" corretto (tap B/Y = duplica;
  l'altra mano è "Save", non "redo").
- `Palette/PaletteController.cs` — `FacePlayer` su Options/Shortcuts; toggle menu unico
  (`ToggleOptionsPanel`/`CloseOptionsPanel`); pannello Shortcuts con schema
  (`BuildSchematicDiagram`/`StickBlock`/`TagV`/`AnchorDot`/`Leader`), anchor `Sch*`,
  `DebugAnchorGrid`, entry `EditorBuildShortcutsPanel`; ✕ via icona `close`;
  `AddModalSurfaceCollider`; `heightAboveHand=0.30`.
- `Palette/PaletteRay.cs` — `TryHitModalSurface` + `SuppressDrawing` latchato.
- `Palette/ToolIcon.cs` — glifo `close` (`Cross`); rimosso il vecchio controller schematico
  procedurale.
- `Drawing/Editor/SchematicImporter.cs` — **nuovo**: importer line-art → bianco/trasparente.
- `Drawing/Editor/ShortcutsPanelPreview.cs` — **nuovo**: anteprima a PNG (PreviewCam figlia
  di root).
- `Drawing/Editor/ShortcutControllerBaker.cs` — baker FBX, **superato** (non più usato).

## Verifica

- Compilazione 0 errori dopo ogni passo; pannello renderizzato e ispezionato via
  *Preview Shortcuts Panel*.
- Test in Play/visore (a carico dell'utente): pannelli rivolti all'utente; ☰ apre/
  chiude il menu come toggle unico (palette chiusa → apre palette+menu; ripremuto → chiude
  tutto, palette resta); raggio visibile su tutta l'area del pannello Shortcuts; nessun
  pallino dopo aver premuto ✕/View Shortcuts; ✕ pulita; palette non trapassata dal
  controller. **Confermato funzionante dall'utente.**

## Parametri tarabili

- Billboard: `FacePlayer.TurnSpeed`.
- Schema: `diagW` in `BuildSchematicDiagram`, anchor `SchLStick/SchY/SchLMenu/SchRStick/SchB`
  (ritarabili con `DebugAnchorGrid`).
- Anteprima: `ShortcutsPanelPreview.OrthoSize`, `Width`/`Height`.
- Pannello: `size` in `BuildShortcutsPanel`; offset etichette/leader in
  `StickBlock`/`TagV`; colore `LeaderColor`.
- Altezza palette: `heightAboveHand`.
