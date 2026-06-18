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
