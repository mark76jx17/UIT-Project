# Palette procedurale — redesign

> Documento tecnico della **palette procedurale** (stile Gravity Sketch) migrata da
> `UIT-Project-C` e del suo redesign. Sostituisce la vecchia palette uGUI documentata
> in [`palette.md`](palette.md) (disabilitata a runtime da `DrawingRig`).
>
> | Campo | Valore |
> |---|---|
> | Data inizio redesign | 2026-06-18 |
> | Branch | `Palette` |
> | Unity | `6000.4.6f1` (Unity 6) |
> | Render pipeline | URP 17.4 |
> | Scena | `Assets/Scenes/SampleScene.unity` |
> | Sorgenti | `Assets/Scripts/Drawing/` |

---

## 1. Com'è fatta (stato pre-redesign)

Palette costruita **interamente in codice** in `PaletteController.BuildPanel()`. Nessun
prefab/Canvas: mesh procedurali (`RoundedMesh`) con materiali `URP/Unlit`
(`BrushMaterials.CreateUnlit`). Montata a runtime da `DrawingRig` sulla mano della
palette (sinistra di default), fa **billboard** verso la testa in `LateUpdate`; in
editor è "pinnata" davanti alla camera (`pinPaletteInEditor`).

### Elementi

- **Striscia pennelli** (sinistra): 4 tipi `BrushType` (`tube/ribbon/glow/dash`).
- **Ruota colori HSV** (`ColorWheel`) + **slider luminosità** verticale (`BrightnessSlider`).
- **Toggle Pressure / Mirror** (pillola accent quando attivo).
- **Colori recenti** (5 swatch, persistono in `PlayerPrefs`).
- **Slider trasparenza** (`AlphaSlider`) e **slider spessore** (`SizeSlider`).
- **Strumenti** Draw / Fill / Erase (`ToolMode`).
- **Azioni** Undo / Redo / Save / Load (`StrokeHistory`, `DrawingStore`).

### Interazione

Niente ray/ISDK: ogni controllo è un `BoxCollider` trigger premuto **toccandolo con la
punta del pennello** (`BrushTip`). `PaletteButton` gestisce press con debounce 0.3 s,
rimpicciolimento e impulso aptico sul controller del pennello. Gli slider e la ruota
leggono il punto di contatto in `OnTriggerStay`.

### Visibilità

`PaletteController.Update`: `panel.SetActive(!Brush.IsDrawing)` — la palette si nasconde
solo mentre disegni. (Niente toggle col tasto X, vedi redesign Step 4.)

### Problemi rilevati (feedback utente + analisi codice)

| Problema | Causa |
|---|---|
| Sovrapposizioni | layout a coordinate "magiche" cucite a mano: collider riga Pressure ↔ slider luminosità (~1 cm), righe label/slider distanti ~12 mm, striscia pennelli fuori dal pannello |
| Piccola / si vede poco | ruota Ø 9 cm, pomelli 9 mm, barre slider ad alpha 0.35 (basso contrasto su pannello scuro) |
| Icone poco informative | PNG glifi a linea bianchi su trasparente, senza chip di sfondo né stato; per i 4 pennelli non comunicano il tratto |
| Niente animazione X | la visibilità ha un solo criterio (IsDrawing) |

---

## 2. Redesign — changelog

### Step 1 (2026-06-18) — sistema di layout + scheletro senza sovrapposizioni

Nuovo helper geometrico **`PaletteLayout`** (`Assets/Scripts/Drawing/Palette/PaletteLayout.cs`):
dispone i controlli in **righe** (`Row`) e **celle** (`Left`/`Right`/`Fill`/`Split`/`StackV`)
in coordinate locali al pannello, con **padding/rowSpacing/colSpacing** espliciti.
Elimina alla radice le coordinate magiche e quindi le sovrapposizioni.

`PaletteController.BuildPanel()` riscritto su `PaletteLayout`:

- Pannello principale **0.34 × 0.33 m** (padding 0.018, gap righe/celle 0.012).
- Righe (alto→basso): **colore** (ruota 0.10 + luminosità 0.018 a sx, Pressure/Mirror
  impilati a dx via `StackV(2)`), **recenti** (label + 5 swatch), **trasparenza**
  (label + slider `Fill`), **strumenti** (`Split(3)`), **spessore** (label + slider),
  **azioni** (`Split(4)`).
- Striscia pennelli estratta in `BuildBrushStrip()`: pannello dedicato ancorato appena a
  sinistra del principale (non più "fuori" a coordinate fisse), 4 bottoni 0.044 impilati.
- `MakeToggle` ora prende una `Cell` (centro + dimensione dal layout) invece di
  posizione + larghezza fissa; pillola/pomello dimensionati sulla cella.

Comportamento controlli invariato (ruota, slider, toggle, recenti, undo/redo/save/load).
Compila pulito. **Da verificare su device**: assenza di sovrapposizioni e ingombro.

### Step 2 (2026-06-18) — passata visiva completa + tasto X + loop di anteprima

Feedback utente dopo test VR (Step 1 "di poco meglio, ancora da rifare"): testo
Pressure/Mirror enorme e soluzione a pillola non gradita, tasto X non chiude, striscia
pennelli a sinistra irraggiungibile, sfondo PNG ancora problematico. Affrontato tutto:

- **Loop di anteprima in edit mode**: `Assets/Editor/PalettePreviewTool.cs` (menu
  *Tools/Palette Preview/Build* e *Clear*) costruisce la palette in edit mode (via
  reflection su `BuildPanel`, senza Play) così la si può **fotografare e iterare**
  (`manage_camera screenshot`). Strumento di sviluppo, escluso dalla build. NB:
  `execute_code` MCP non è utilizzabile su questa macchina (backend CodeDom/mono rotto:
  "nome file troppo lungo").
- **Testo a dimensione fissa**: `MakeLabel` non usa più l'auto-size (che gonfiava alcune
  scritte e ne rimpiccioliva altre). Font in unità mondo: `SectionFont 0.175`,
  `ButtonFont`/`ToggleFont 0.24` (≈0.047 m per unità di fontSize, tarato a schermo).
- **Pressure/Mirror → toggle compatti** (`MakeToggleButton`): bottone che si illumina
  (accent) quando attivo, stesso linguaggio di Draw/Fill/Erase. Niente più pillola.
- **Striscia pennelli a destra** del pannello (lato della mano che disegna, prima a
  sinistra e poco raggiungibile).
- **Icone pennello = anteprime del tratto** (`BrushPreview.cs`): texture procedurali che
  mostrano il tratto reale (tubo pieno, nastro piatto, linea glow, tratteggio). Niente
  più PNG astratti per i pennelli.
- **Import PNG corretto** (icone strumenti pencil/droplet/eraser/undo/redo/save/load):
  `alphaIsTransparency=1` (via `manage_asset`), `mipmapEnabled=false`, `Uncompressed` →
  spariti gli aloni scuri attorno ai glifi. (Le PNG restano glifi line-art sottili: resa
  da valutare su device.)
- **Tasto X apre/chiude con animazione** (`PaletteController`): `isOpen` commutato dal
  tasto X (mano palette; A se palette su mano destra/mancini), pannello che scala in/out
  con `SmoothStep` (0.16 s) + impulso aptico. Visibilità = `isOpen && !IsDrawing`.
  **Rimosse** le scorciatoie X/Y per Undo/Redo dal controller (X ora apre/chiude;
  Undo/Redo restano sui pulsanti del pannello).
- **Contrasto slider**: track di Alpha/Size da bianco α0.35 (quasi invisibile) a grigio
  pieno `(0.32,0.32,0.38)`; barra luminosità più larga.

### Step 3 (2026-06-18) — icone procedurali + fix sorting trasparente

- **Icone procedurali** (`ToolIcon.cs`): glifi bianchi disegnati in codice con SDF
  (segmenti/dischi/triangoli/archi + anti-aliasing) per pencil/droplet/eraser/undo/redo/
  save/load. `MakeIconImage` ora usa `ToolIcon.Get(name)` invece dei PNG → coerenti e
  nitidi, problema sfondo PNG eliminato del tutto. I PNG in `Resources/Icons` restano ma
  non sono più usati dai pulsanti (le 4 anteprime pennello sono in `BrushPreview`).
- **MakeTextButton riorganizzato**: icona a sinistra, testo nello spazio a destra
  (niente più icona sotto il testo, come capitava su "Fill").
- **FIX sorting trasparente** (il difetto più grave): tutti i materiali UI sono Unlit
  trasparenti con ZWrite off → con un unico render queue lo sfondo del pannello copriva
  in modo instabile alcuni controlli (es. il bottone "Fill" appariva bianco, la ruota
  spenta). Introdotti **render queue espliciti** a livelli: sfondo `3000`, controlli
  `3002` (bottoni/swatch/ruota/slider via `SetQueue`), icone `3003`, testo TMP `3004`.
  Risultato: pulsanti uniformi e ruota colori molto più viva.
- Droplet/pencil ribilanciati come tratto sottile coerente con gli altri glifi.

**Strumento di anteprima** aggiornato: disabilita temporaneamente la vecchia palette
uGUI (attiva in edit mode) durante il preview e la ripristina su *Clear*.

**Da verificare su device**: tasto X (apri/chiude animato + aptica); raggiungibilità
striscia a destra; leggibilità generale; che il fix sorting tenga anche in build URP/Quest
(in passthrough). Le icone azione (Undo/Redo/Save/Load) sono un po' strette
icona+testo: se in VR risultano affollate, ridurre il font o l'icona di quella riga.

---

## 3. Recupero — conflitto di merge non risolto (2026-06-19)

Dopo un push `Palette → main`, all'apertura Unity partiva in **Safe Mode** con due errori
e l'MCP non si avviava:

```
DrawingRig.cs(102): CS0246 — 'PaletteToggle' could not be found
DrawingRig.cs(108): CS0234 — 'BrushTip' does not exist in namespace 'MixedRealityProject'
```

**Causa radice.** Un `git stash pop` in conflitto era stato **committato con i marcatori
dentro** (commit `46f5c2f`):

- `Assets/Scenes/SampleScene.unity` conteneva **123 righe di marcatori** di conflitto
  (`<<<<<<< Updated upstream` / `=======` / `>>>>>>> Stashed changes`) → YAML invalido.
  Il lato *Stashed changes* re-introduceva la vecchia palette uGUI (PaletteToggle /
  PaletteState / PaletteFeedback / PalettePanel), ormai rimossa dal codice su `main`.
- `DrawingRig.DisableLegacyPalette()` referenziava quei tipi legacy inesistenti su `main`
  (`PaletteToggle` e il `MixedRealityProject.BrushTip` di root, diverso dal nuovo
  `MixedRealityProject.Drawing.BrushTip`) → non compilava → Safe Mode → MCP bloccato.

**Risoluzione.**

- **Scena**: risolto il conflitto tenendo **sempre il lato `Updated upstream`** (lo stato
  pulito di `main`). Risultato: 2464 righe, 0 marcatori, header YAML corretto, `DrawingRig`
  conservato (è nella parte comune, fuori dai conflitti), nessun oggetto palette legacy.
  Coincide con la baseline stabile `ab4c53d` + il GameObject `DrawingRig`.
- **`DrawingRig.cs`**: rimosso `DisableLegacyPalette()`, il campo `disableLegacyPalette`
  e la chiamata in `Start()`. Era codice morto: con la scena pulita non c'è più nessun
  oggetto legacy da disabilitare, e referenziava tipi inesistenti.

**Nessuna perdita del lavoro di ieri**: tutto il sistema di disegno e la palette procedurale
sono codice (committato e intatto in `46f5c2f`) costruito a runtime da `DrawingRig`; la scena
deve solo contenere il GameObject bootstrap, che è stato preservato. Solo la palette uGUI
legacy (già deprecata) è stata definitivamente eliminata dalla scena.

`Assets/_Recovery/0.unity` è una scena minimale di recovery di Unity **senza** `DrawingRig`:
non è la scena buona, si può eliminare.

**Da verificare**: riaprire Unity in modalità normale → compilazione pulita, niente Safe Mode,
MCP che si avvia, e la scena che apre senza componenti "missing script".

### Fix runtime — TMP fontMaterial null in `MakeLabel` (2026-06-19)

Dopo il recupero, in Play (simulatore) la palette mostrava **solo ruota colori + slider
luminosità**, mancavano tutti i button. Causa (da `Editor.log`):

```
ArgumentNullException: Value cannot be null. Parameter name: source
  at TMPro.TMP_Text.get_fontMaterial ()
  at PaletteController.MakeLabel ()       PaletteController.cs:399
  at PaletteController.MakeToggleButton () PaletteController.cs:329
  at PaletteController.BuildPanel ()       PaletteController.cs:213
```

`BuildPanel()` costruisce ruota + slider luminosità, poi al primo testo TMP
(`MakeToggleButton` → `MakeLabel`) la riga `tmp.fontMaterial.renderQueue = QueueText`
(introdotta nel fix sorting dello Step 3, mai testato in editor — vedi "da verificare su
device") accedeva a `fontMaterial` quando il material condiviso del font non era ancora
inizializzato → `CreateMaterialInstance(null)` → eccezione che **abortisce l'intera
BuildPanel**, facendo sparire tutto ciò che viene dopo il primo testo.

**Fix** (`MakeLabel`): assegnare un font valido (`TMP_Settings.defaultFontAsset`) se
mancante, e impostare `fontMaterial.renderQueue` solo se `fontSharedMaterial != null`.
Così non lancia mai e la palette si costruisce per intero. **Da ritestare in Play.**

### Test interazione nel simulatore (2026-06-19)

Il Meta XR Simulator gira **dentro l'editor**, quindi `#if UNITY_EDITOR` è attivo: con
`pinPaletteInEditor = true` la palette veniva staccata dalla mano e pinnata davanti alla
camera (`PlacePaletteForEditor`), comodo per guardarla ma non per provare l'interazione
reale. Default cambiato a **`pinPaletteInEditor = false`** così nel simulatore la palette
resta **sulla mano-palette** (sinistra), come sul device. La palette si avvia già aperta
(`isOpen = true`); il trigger della mano-palette la apre/chiude.

### Fix leak materiali in anteprima — `SetQueue` (2026-06-22)

`Tools/Palette Preview/Build` (anteprima in edit mode) emetteva:
*"Instantiating material due to calling renderer.material during edit mode. This will leak
materials into the scene"* da `SetQueue` (`PaletteController.cs`). In edit mode il getter
`renderer.material` **clona** il materiale e lascia materiali orfani nella scena. Dato che
ogni controllo ha già un materiale unico (`BrushMaterials.CreateUnlit`), il fix usa
`sharedMaterial` (modifica in place, nessuna clonazione), valido sia a runtime sia in editor.
Verificato: l'anteprima non salvava comunque oggetti/materiali nel file di scena (la scena su
disco resta pulita).

---

## 4. Step 4 — redesign grafico e usabilità (2026-06-22)

Obiettivo (richiesta utente): togliere la trasparenza che fa vedere il passthrough
attraverso slider e bordo della ruota, usare più spazio per il picker, slider orizzontali
auto-esplicativi al posto delle label lunghe. Diviso in 4 sotto-step (4a–4d), ognuno
compilato e verificato in anteprima (`Tools/Palette Preview`) via screenshot MCP.

### 4a — Materiali opachi (fix trasparenza)
`BrushMaterials.CreateUnlit(color, opaque=false)`: nuova variante **opaca** (`MakeOpaque`:
`_Surface=0`, `ZWrite=1`, blend One/Zero, queue Geometry). `MakeRounded` ora crea sfondi
opachi di default (`opaque=true`) → pannello, striscia, bottoni, separatori, swatch non si
vedono più "attraverso" sul passthrough e il sorting è risolto dal **depth** invece che dalle
render queue trasparenti. `EmptySwatch` da bianco α0.15 a grigio scuro opaco (slot vuoto
leggibile anche senza alpha).

### 4b — ColorSquare + HueBar (via la ruota)
Nuovi componenti `ColorSquare` (quadrato Saturazione×Valore grande, texture rigenerata al
cambio tinta) e `HueBar` (barra tinta orizzontale), entrambi opachi, col tocco del `BrushTip`
(`OnTriggerStay`) o mouse/raggio (`PressAt`). **Rimossi** `ColorWheel` e lo slider
`BrightnessSlider` (la luminosità è l'asse Y del quadrato). Pannello alzato da `0.56` a
`0.64` m per fare spazio al picker.

### 4c — Slider orizzontali auto-esplicativi
- `AlphaSlider`: rampa del colore corrente (trasparente→pieno) sopra una **scacchiera opaca**
  di sfondo → la trasparenza si legge come scacchiera, niente più see-through sul passthrough.
- `SizeSlider`: riscritto come **cuneo** (sottile→spesso) tinto col colore corrente → mostra
  lo spessore a colpo d'occhio. Entrambi si aggiornano col colore live.
- Rimosse le label testuali "Transparency", "Size", "Recent"; swatch recenti distribuiti su
  5 celle piene (`Split(5)`). Font `SectionFont` non più usato → rimosso.

### 4d — Rifinitura e pulizia
- `PaletteRay` (raggio) e `DesktopBrushSimulator` (mouse simulatore) riallineati da
  `ColorWheel`/`BrightnessSlider` a `ColorSquare`/`HueBar`, così il nuovo picker risponde
  anche a distanza e col mouse, non solo al tocco diretto.
- Eliminati i file ora morti `ColorWheel.cs` e `BrightnessSlider.cs`; rimossi `SetQueue` e
  `SectionFont` inutilizzati.

**Verificato in anteprima editor** (screenshot): pannello opaco, picker S×V + hue, slider a
scacchiera e a cuneo, layout completo senza overflow. **Da verificare su device/simulatore**:
assenza di see-through sul passthrough e interazione (tocco/mouse/raggio) con picker e slider.
