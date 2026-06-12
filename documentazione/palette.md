# Palette — Menu ancorato al controller sinistro

> Documento tecnico — Stato dell'implementazione della feature *Palette*: pannello UI
> ancorato alla mano non dominante (sinistra), interagibile con la mano dominante
> (destra) via poke o ray. Pensato per consentire il **riuso** e la **reimplementazione
> da zero** della feature.
>
> | Campo | Valore |
> |---|---|
> | Data | 2026-06-12 |
> | Branch | `Palette` |
> | Unity | `6000.4.6f1` (Unity 6) |
> | Meta XR SDK | `201.0.0` (core, interaction.ovr, platform) |
> | Render pipeline | URP 17.4 |
> | Scena | `Assets/Scenes/SampleScene.unity` |

---

## 1. Obiettivo e design

Metafora della **tavolozza del pittore** (pattern Tilt Brush, vedi
[case study UX](https://blog.hamaluik.ca/posts/vr-ux-case-study-tilt-brush/)):

- la **mano non dominante (sinistra)** porta la palette, ancorata al controller —
  il menu è sempre raggiungibile perché attaccato alla mano, non world-locked;
- la **mano dominante (destra)** seleziona: **poke** (tocco diretto) o
  **ray + grilletto** (a distanza);
- interfacce 2D (uGUI) disposte nello spazio 3D.

### Piano di sviluppo (fasi)

| Fase | Contenuto | Stato |
|---|---|---|
| 1a | Shell: `PalettePanel` segnaposto ancorato al controller sinistro, posa da tarare | ✅ fatto (2026-06-12) |
| 1b | Script `PaletteToggle`: tasto X mostra/nasconde, animazione scala, aptica, **visibile solo con controller sinistro in mano** | ✅ fatto (2026-06-12) |
| 2a | Interazione via ray: `EventSystem` + `PointableCanvasModule`, canvas pointable (`EmptyUIBackplateWithCanvas`) al posto del cubo, switch di prova | ✅ fatto (2026-06-12) |
| 2b | Poke da controller destro (tocco diretto) — **già presente nel rig**, nessuna modifica | ✅ solo verifica (2026-06-12) |
| 3a | Riga strumenti: 3 tile toggle (Pennello/Gomma/Selezione) in ToggleGroup | ✅ fatto (2026-06-12) |
| 3b | Griglia 8 swatch colore con selezione esclusiva + dorso opaco anti-specchio | ✅ fatto (2026-06-12) |
| 3c | Slider dimensione pennello | ✅ fatto (2026-06-12) |
| 3d | Bottone "Colore personalizzato" + pagina Flexible Color Picker | ✅ fatto (2026-06-12) |
| 4a | `PaletteState` + cablaggio strumenti + attenuazione sezione colori | ✅ fatto (2026-06-12) |
| 4b | Cablaggio colori (swatch, cella custom, FCP live + tinta cella) | ✅ fatto (2026-06-12) |
| 4c | Cablaggio slider + etichetta "%" live | ✅ fatto (2026-06-12) |
| 5 | Polish: aptica hover/press, suoni UI, test finale | ✅ implementata (2026-06-12), test finale in corso |

### Asset previsti

1. **Meta UI Set** — `com.meta.xr.sdk.interaction` (Essentials) `201.0.0`, già nel progetto:
   `Runtime/Sample/Objects/UISet/Prefabs/` (Backplate, Button, ContextMenu, Dialog,
   DropDown, Patterns, Slider, TextInputField, Tooltip).
2. **FlatUnityCanvas** — `Runtime/Sample/Objects/Props/FlatUnityCanvas.prefab` +
   `PointableCanvasModule`: rende un canvas uGUI interagibile in VR (poke/ray/scroll).
   Il canvas curvo è overkill per un pannello palmare.
3. **[Flexible Color Picker](https://assetstore.unity.com/packages/tools/gui/flexible-color-picker-150497)**
   (Asset Store) — picker HSV uGUI; su un PointableCanvas funziona in VR senza codice
   custom. **Da importare manualmente** dal Package Manager (My Assets). Serve in Fase 3.

### Stato della scena rilevante (rilevato il 2026-06-12)

- Il rig `[BuildingBlock] OVRInteractionComprehensive` supporta già **mani e controller**
  con switching automatico (`ControllerActiveState` / `HandActiveState`).
- Sul lato destro esiste già `RightInteractions/Interactors/Controller/ControllerRayInteractor`
  (ray + grilletto): il fallback a distanza della Fase 2 è già coperto.
- ~~**Manca** un `ControllerPokeInteractor` sotto `Interactors/Controller` (solo ray) e
  **manca** il `PointableCanvasModule` in scena → entrambi da aggiungere in Fase 2.~~
  **Correzione (2026-06-12, Fase 2b)**: il `ControllerPokeInteractor` esiste già in
  `RightInteractions/Interactors/Controller and No Hand/` (la prima ricognizione aveva
  guardato solo la sottocartella `Controller`), completo e registrato nel
  `BestHoverInteractorGroup`. Solo il `PointableCanvasModule` andava aggiunto (fatto in 2a).
- ⚠️ Il toggle con `OVRInput.Button.Three` (tasto X) funziona **solo con i controller**:
  in modalità hand-tracking la palette non sarà apribile (eventuale gesto in futuro).

---

## 2. Fase 1a — Shell segnaposto (implementata)

Nessun codice: solo gerarchia di scena, per tarare la **posa ergonomica** del pannello
prima di scrivere lo script di toggle.

### Gerarchia creata

```
[BuildingBlock] Camera Rig
└── TrackingSpace
    └── LeftHandAnchor
        └── LeftControllerAnchor          (anchor 6-DoF del controller sinistro)
            └── PalettePanel              (← NUOVO: contenitore, futuro target del toggle)
                └── PlaceholderVisual     (← NUOVO: cubo sottile = segnaposto visivo)
```

### `PalettePanel` (GameObject vuoto)

| Proprietà | Valore | Motivazione |
|---|---|---|
| Parent | `LeftControllerAnchor` | segue il controller sinistro (stile Tilt Brush, non world-locked) |
| `localPosition` | `(0, 0.05, 0)` | ~5 cm sopra la faccia superiore ("nera") del controller |
| `localRotation` | `(90, 0, 0)` | pannello **sdraiato, parallelo alla faccia dei pulsanti** (piano XZ dell'anchor): si legge dall'alto come una tavolozza; il lato "alto" della UI punta in avanti lungo il controller |

> Posa iniziale `(0, 0.10, -0.02)` / `(-30, 0, 0)` (pannello quasi verticale inclinato
> verso l'utente) scartata dopo il primo test: l'utente si aspetta la palette parallela
> alla superficie nera del controller, poco sopra di essa.
>
> Nota orientamento: con rot. X = +90° la normale del lato "fronte" del futuro canvas
> uGUI (−Z locale) punta verso l'alto → la UI sarà leggibile guardando il pannello
> dall'alto. Se la faccia nera reale del controller risultasse inclinata rispetto al
> piano XZ dell'anchor, correggere solo la X della rotazione (90 ± offset).

È il **contenitore**: lo script `PaletteToggle` (Fase 1b) scalerà/attiverà questo
oggetto; il contenuto UI (Fase 2-3) sarà suo figlio. I valori di posa sono il punto
di partenza da **tarare in build** (vedi §3).

### `PlaceholderVisual` (cubo primitivo)

| Proprietà | Valore | Motivazione |
|---|---|---|
| Primitive | `Cube` | visibile da ogni lato (un Quad sarebbe invisibile da dietro) |
| `localScale` | `(0.20, 0.15, 0.005)` | 20×15 cm = ingombro previsto del pannello reale, 5 mm di spessore |
| `BoxCollider` | **rimosso** | non deve collidere con la palla né coi futuri interactor |
| Material | `Assets/Materials/PalettePlaceholder.mat` | URP/Lit, grigio-blu scuro `(0.15, 0.17, 0.22)` |

Verrà **sostituito** dal canvas reale (FlatUnityCanvas + UI Set) in Fase 2.

### Asset creati

- `Assets/Materials/` (nuova cartella)
- `Assets/Materials/PalettePlaceholder.mat` — URP/Lit, baseColor `(0.15, 0.17, 0.22, 1)`

---

## 2b. Fase 1b — Script `PaletteToggle` (implementata)

`Assets/Scripts/PaletteToggle.cs`, componente su `PalettePanel`. È l'**unico
proprietario** della visibilità del pannello:

```
visibile = isOpen (toggle col tasto X) && controller sinistro in mano
```

### Funzionamento

- **Tasto X** = `OVRInput.GetDown(OVRInput.RawButton.X)`, accettato solo se il
  controller è in mano. Al toggle: breve impulso aptico (`SetControllerVibration`,
  40 ms) sul controller sinistro.
  > ⚠️ **Gotcha OVRInput**: `Button.Three` corrisponde alla X solo interrogando la
  > coppia combinata (`Controller.Touch`); con `Controller.LTouch` la mappatura è
  > per-controller e la X diventa `Button.One`. La prima versione usava
  > `GetDown(Button.Three, LTouch)` e non scattava mai. `RawButton.X` indica il
  > tasto fisico senza ambiguità.
- **Controller in mano** = `ControllerActiveState.Active` (componente del rig su
  `LeftInteractions`, `Active => Controller.IsConnected`): è lo stesso stato con cui
  il rig accende/spegne interactor e visual del controller. Posando il controller e
  passando alle mani la palette sparisce da sola; riprendendolo, riappare se era aperta.
- **Animazione**: `visibility` 0↔1 con `Mathf.MoveTowards` in `Update`
  (`animationDuration` = 0.15 s, regolabile da Inspector), applicata come
  `localScale` di `PalettePanel`.

### Dettagli di implementazione

| Scelta | Motivazione |
|---|---|
| La scala non scende mai a 0 esatto e `PalettePanel` non viene mai disattivato | lo script vive su `PalettePanel`: se disattivasse il proprio GameObject, `Update` smetterebbe di girare e il toggle morirebbe |
| Il figlio `content` (= `PlaceholderVisual`, poi il canvas) viene disattivato a `visibility == 0` | niente rendering/raycast residui dal pannello "chiuso" a scala microscopica |
| Stato iniziale `isOpen = true` | all'avvio la palette è visibile appena il controller è in mano |

### Riferimenti serializzati (cablati in scena)

| Campo | Valore |
|---|---|
| `content` | `PlaceholderVisual` |
| `leftControllerActive` | `ControllerActiveState` su `LeftInteractions` |
| `animationDuration` | `0.15` |

---

## 2c. Fase 2a — Canvas pointable (implementata)

Il cubo `PlaceholderVisual` è stato **eliminato** e sostituito da un canvas uGUI
interagibile in VR. Nessun codice nuovo: solo assemblaggio di prefab del package
`com.meta.xr.sdk.interaction` (Essentials).

### Oggetti aggiunti

```
EventSystem                       (← NUOVO, root di scena)
├── EventSystem                   (uGUI standard)
└── PointableCanvasModule         (ISDK: instrada gli eventi dei pointer ISDK ai canvas uGUI)

PalettePanel
└── PaletteCanvas                 (← NUOVO: istanza di EmptyUIBackplateWithCanvas.prefab del UISet)
    │   [PokeInteractable, RayInteractable, PointableCanvas, AudioSource,
    │    PointableCanvasUnityEventWrapper, UIThemeManager — già nel prefab]
    └── CanvasRoot                (Canvas world-space, scala 0.0005, HorizontalLayoutGroup)
        ├── UIBackplate           (sfondo arrotondato; VerticalLayoutGroup, Mask)
        │   ├── GradientEffect
        │   └── TestToggle        (← NUOVO: ToggleButton_Switch.prefab — controllo di verifica)
        └── ISDK_PokeInteraction
            └── Surface           (PlaneSurface + ClippedPlaneSurface + BoundsClipper,
                                   anchor stretch → segue la dimensione del canvas)
```

### Scelte e valori

| Cosa | Valore/Scelta | Motivazione |
|---|---|---|
| Prefab base | `UISet/Prefabs/Backplate/EmptyUIBackplateWithCanvas` | unico prefab del UISet con **tutto il ponte già montato**: PointableCanvas + PokeInteractable + RayInteractable + superfici (il `FlatUnityCanvas` di Props non ha il RayInteractable) |
| Dimensione | `sizeDelta 400×300` su `CanvasRoot` **e** `UIBackplate` | a scala 0.0005 = **20×15 cm**, l'ingombro validato in Fase 1a. Va impostata su entrambi: l'`HorizontalLayoutGroup` di CanvasRoot ha `childControlWidth/Height = false`, quindi posiziona i figli ma **non** li ridimensiona |
| `Surface` | non toccata | anchor `(0,0)-(1,1)` stretch + `RectTransformBoundsClipperDriver`: l'area interattiva segue da sola il canvas |
| `TestToggle` | figlio di **UIBackplate** (non di CanvasRoot) | i figli diretti di CanvasRoot vengono affiancati dall'HorizontalLayoutGroup come "pannelli"; il contenuto va dentro il backplate, che ha il VerticalLayoutGroup |
| `PaletteToggle.content` | ora punta a `PaletteCanvas` | il toggle col tasto X spegne/accende il canvas intero (interazione compresa) |
| `EventSystem` | GameObject root con `EventSystem` + `PointableCanvasModule` | il modulo ISDK sostituisce lo StandaloneInputModule; senza di esso i PointableCanvas non ricevono eventi. Ne serve **uno solo** per scena |

> ⚠️ **Gotcha tooling**: istanziando un prefab uGUI sotto un canvas scalato (0.0005),
> il tool MCP compensa la trasformazione world → il `TestToggle` è nato con scala
> locale 2000 e rotazione 270°. Sempre rimettere local pos/rot/scale a (0,0,0)/(1,1,1)
> dopo l'instanziazione sotto canvas.

### Come funziona l'interazione (Fase 2a = solo ray)

`RightInteractions/Interactors/Controller/ControllerRayInteractor` (già nel rig) →
`RayInteractable` del canvas → `PointableCanvas` converte il punto di contatto in
coordinate canvas → `PointableCanvasModule` lo trasforma in normali eventi uGUI
(hover/click) → il `Toggle` di TestToggle scatta come se fosse cliccato col mouse.

### Fase 2b — poke da controller: già nel rig

Il tocco diretto con la punta del controller destro passa da
`RightInteractions/Interactors/Controller and No Hand/ControllerPokeInteractor`
(`PokeInteractor` + `ControllerRef` + `ActiveStateTracker`), già registrato nel
`BestHoverInteractorGroup` di `Interactors` insieme al ray. Il gruppo arbitra
automaticamente: vicino al pannello vince il poke, a distanza il ray.
`PokeInteractable` e `Surface` del canvas erano già pronti dalla 2a → **nessuna
modifica necessaria**, solo verifica su device.

---

## 2d. Fase 3a — Riga strumenti (implementata)

Decisioni confermate dall'utente: pannello 20×15 cm, slider opacità rimandato,
8 colori rapidi (bianco, nero, rosso, arancione, giallo, verde, blu, viola),
strumenti pennello/gomma/selezione con pennello di default.

Budget di layout dentro `UIBackplate` (`VerticalLayoutGroup`: padding 24, spacing 8,
alignment UpperLeft, childControl false → ogni sezione ha dimensione esplicita):
area utile **352×252 px**; sezioni previste: strumenti 48 → colori ~110 → slider ~56.

### Gerarchia (TestToggle eliminato)

```
UIBackplate
└── ToolsRow            (352×64; HorizontalLayoutGroup spacing 16, MiddleLeft; ToggleGroup allowSwitchOff=false)
    ├── ToolBrush       (TextTileButton_IconAndLabel_Toggle, 106×64, label "Pennello", isOn=true)
    ├── ToolEraser      (idem, "Gomma", isOn=false)
    └── ToolSelect      (idem, "Selezione", isOn=false)
```

### Note di assemblaggio

- `ToggleButton_Radio` **scartato**: è il solo cerchietto 16×16 senza etichetta,
  target troppo piccolo per poke. Scelto `TextTileButton_IconAndLabel_Toggle`
  (tile 176×100 nativa) ridimensionato a **106×64** (3×106+2×16 = 350 ≤ 352).
- Dentro ogni tile disattivati `Icon`, `Space`, `Label (1)` (secondaria); resta la
  `Label` TMP principale col nome dello strumento.
- I tre `Toggle` puntano al `ToggleGroup` di ToolsRow → mutua esclusione gratis.
  Nota tooling: la property si chiama `m_Group` (serialized name), `group` non esiste
  come SerializedProperty; `m_IsOn` per lo stato.
- I controlli sono **vivi ma scollegati**: nessun `onValueChanged` cablato — il
  cablaggio a `PaletteState` è la Fase 4, come da piano.
- Budget verticale aggiornato: strumenti 64 + colori ~110 + slider ~56 + spacing 16
  = 246 ≤ 252 px utili.

---

## 2e. Fase 3b — Griglia colori + dorso anti-specchio (implementata)

### Fix "UI specchiata da dietro"

Gli shader uGUI/TMP non fanno backface culling: guardando il pannello da dietro si
vedeva l'interfaccia in trasparenza, specchiata. Fix: **`BackCover`**, un Quad
one-sided figlio di `PaletteCanvas` (pos locale `(0, 0, 0.002)`, rot `(0, 180, 0)`,
scala `0.2×0.15`), material `PalettePlaceholder.mat`, `MeshCollider` rimosso.
Essendo opaco viene disegnato prima della UI (transparent queue) e a 2 mm dietro il
piano del canvas: da dietro copre tutto, da davanti è invisibile (backface culled).

### Griglia colori

```
UIBackplate
├── ToolsRow   (Fase 3a)
└── ColorsGrid (352×100; GridLayoutGroup cell 44×44, spacing 12, FixedColumnCount=4;
    │           ToggleGroup allowSwitchOff=false)
    ├── SwatchWhite  (isOn=true)   ├── SwatchYellow
    ├── SwatchBlack                ├── SwatchGreen
    ├── SwatchRed                  ├── SwatchBlue
    └── SwatchOrange               └── SwatchPurple
```

Ogni swatch è **uGUI puro** (il UI Set non ha tile colorate): `Image` senza sprite
(quadrato pieno del colore) + `Toggle` con `transition = None` (un tint falserebbe la
percezione del colore) + figlio `Check` (`Image` inset 6 px, `raycastTarget = false`)
usato come `Toggle.graphic` → visibile solo da selezionato. Contrasto del check:
**nero α0.55** su bianco/giallo/arancione, **bianco α0.55** sugli altri.

Colori: bianco `(1,1,1)`, nero `(0,0,0)`, rosso `(1,0,0)`, arancione `(1,0.5,0)`,
giallo `(1,0.9,0)`, verde `(0,0.8,0.1)`, blu `(0,0.35,1)`, viola `(0.6,0,0.8)`.

Controlli vivi ma non cablati (Fase 4). Il bottone "custom…" per il picker arriva
in Fase 3d.

---

## 2f. Fase 3c — Slider dimensione pennello (implementata)

Terza sezione di `UIBackplate`: **`SizeSlider`** = istanza di
`UISet/Prefabs/Slider/LargeSlider_LabelsAndIcons.prefab` (root 300×40, la variante
più alta = miglior target poke). Struttura interna del prefab:

```
SizeSlider (VerticalLayoutGroup del prefab)
├── LargeSlider     (uGUI Slider; min 0, max 1, value 0.5)
├── LabelsText      (riga etichette: TextLeft │ Gap │ TextRight)
│     TextLeft  = "Dimensione"
│     TextRight = "50%"   ← segnaposto statico; in Fase 4 sarà aggiornato live
└── LabelsIcon      (riga icone, disattivata di default nel prefab)
```

Budget verticale finale: 64 (strumenti) + 100 (colori) + 40 (slider) + 16 (spacing)
= 220 ≤ 252 px utili. Lo slider uGUI standard funziona via PointableCanvas con
drag (ray) e poke; `onValueChanged` non cablato (Fase 4: dimensione pennello +
aggiornamento etichetta %).

> Fix post-verifica: lo slider mostrava un'**icona volume** dentro la maniglia
> (default del prefab UISet, pensato per demo audio) → disattivato
> `LargeSlider/Handle Slide Area/Handle/Icon`.

---

## 2g. Fase 3d — Pagina Flexible Color Picker (implementata)

Il pannello passa da 300 a **340 px di altezza (20×17 cm)** per ospitare il bottone:
`sizeDelta 400×340` su `CanvasRoot` e `UIBackplate`, `BackCover` scala `(0.2, 0.17)`.
La `Surface` di interazione segue da sola (anchor stretch).

### Struttura a due pagine

```
CanvasRoot
├── UIBackplate (pagina principale)
│   ├── ToolsRow / ColorsGrid / SizeSlider     (fasi 3a-3c)
│   └── CustomColorButton                       (← NUOVO: SecondaryButton UISet 352×40,
│                                                  label "Colore personalizzato", icona off)
├── PickerPanel (pagina picker — duplicato di UIBackplate svuotato, inattivo all'avvio)
│   ├── GradientEffect
│   ├── ColorPicker        (← NUOVO: istanza Assets/FlexibleColorPicker/FlexibleColorPicker.prefab,
│   │                         ridimensionato da 200×280 a 352×240 — layout interno proporzionale)
│   └── ClosePickerButton  (← NUOVO: duplicato del SecondaryButton, label "OK")
└── ISDK_PokeInteraction / BackCover            (condivisi: l'interazione copre entrambe le pagine)
```

### Script `PalettePages` (`Assets/Scripts/PalettePages.cs`, su `PaletteCanvas`)

Commuta le pagine: "Colore personalizzato" → mostra `PickerPanel` e nasconde
`UIBackplate`; "OK" → viceversa. I listener `onClick` sono aggiunti in `Awake` a
runtime (il cablaggio di UnityEvent persistenti via tooling è fragile); in scena
restano solo riferimenti serializzati (`mainPage`, `pickerPage`, `openPickerButton`,
`closePickerButton`). `Awake` forza anche lo stato iniziale (pagina principale).
Poiché le pagine sono figlie dell'`HorizontalLayoutGroup` di CanvasRoot e mai attive
insieme, occupano a turno la stessa posizione.

### Nota FCP

`FlexibleColorPicker` è uGUI puro → funziona via PointableCanvas come il resto.
⚠️ Da verificare su device la resa dello shader custom `FCP_Gradient` (URP/Quest,
canvas world-space): è il rischio residuo segnalato nell'analisi. Il colore scelto
(`FlexibleColorPicker.color`) sarà letto/cablato da `PaletteState` in Fase 4.

### Revisione post-verifica (2026-06-12)

Feedback utente: slider sovrapposto al bottone, picker troppo piccolo. Interventi:

1. **Overlap slider/bottone**: il prefab `LargeSlider_LabelsAndIcons` dichiara
   un'altezza root di 40 px ma il contenuto reale (etichette 16 + gap + slider 40)
   è ~64 px → il `VerticalLayoutGroup` (childControlHeight=false) spaziava col 40
   dichiarato e il contenuto traboccava. Fix: `SizeSlider.sizeDelta = 300×64`.
2. **Pannello a 400×360 px (20×18 cm)** (CanvasRoot, UIBackplate, PickerPanel;
   `BackCover` scala `0.2×0.18`).
3. **Bottone custom → cella in griglia**: eliminato il bottone full-width; ora
   `ColorsGrid` è 4×3 (352×156) e la 9ª cella è **`CustomColorCell`**: `RawImage`
   con la texture del gradiente hue di FCP (`DefGrad_H_Hor.png`, "arcobaleno" =
   colore personalizzato) + `Button` che apre il picker (ricablato in
   `PalettePages.openPickerButton`). In Fase 4 la cella mostrerà il colore custom
   scelto (overlay legato a `FlexibleColorPicker.color`).
4. **Picker semplificato e ingrandito** (352×264): disattivati `RPicker`,
   `GPicker`, `BPicker`, `SPicker`, `VPicker`, `APickerBackground`, `HexInput`
   (inutilizzabile in VR senza tastiera), `ModeDropdown`, `HVButton`, `SVButton`.
   Restano, ri-ancorati in grande: `MainPicker` (riquadro SV, top 72% full-width),
   `HPicker` (barra hue, in basso a sinistra, 20% altezza), `ColorPreview`
   (in basso a destra). Gli elementi disattivati sono riattivabili dall'Inspector.

Budget pagina principale: 24+64+8+156+8+64+24 = 348 ≤ 360 ✓.
Budget pagina picker: 24+264+8+40+24 = 360 ✓.

### Revisione 2 post-verifica (2026-06-12) — selezione esclusiva con la cella custom

Feedback utente: lo swatch di griglia restava evidenziato anche scegliendo il
colore personalizzato. Causa: la cella custom era un `Button`, estraneo al
`ToggleGroup` dei colori. Fix:

- `CustomColorCell` ora è un **`Toggle`** del gruppo di `ColorsGrid` (transition
  None, `graphic` = nuovo figlio `Check` inset come gli altri swatch, isOn=false):
  selezionandola, lo swatch attivo si spegne, e viceversa.
- ⚠️ Unity **vieta due `Selectable` sullo stesso GameObject** (Button+Toggle):
  l'apertura del picker è passata a **`OpenPickerOnClick`**
  (`Assets/Scripts/OpenPickerOnClick.cs`, `IPointerClickHandler` — non è un
  Selectable, convive col Toggle) che chiama `PalettePages.OpenPicker()`. Vantaggio:
  scatta a ogni click, anche a cella già selezionata (un Toggle già on non emette
  eventi), quindi il picker è sempre riapribile.
- `PalettePages` rifattorizzato: `OpenPicker()`/`ClosePicker()` pubblici, rimosso
  il campo `openPickerButton`; resta il listener sul bottone "OK".

**Rimandato alla Fase 4** (logica di stato): quando lo strumento attivo è gomma o
selezione, la sezione colori verrà **attenuata** (interactable off + alpha ridotta)
— non deselezionata: il colore deve restare memorizzato per il ritorno al pennello.

### Revisione 3 post-verifica (2026-06-12) — riquadro SV che non si re-tingeva

Feedback utente: nel picker il riquadro grande restava sempre rosso/nero al variare
della tinta. Causa (da `FlexibleColorPicker.cs`, `UpdateTextures`): il prefab FCP
ha di default `advancedSettings.mainStatic = true` → il **solo MainPicker** usa la
sprite statica `DefGrad_SV.png` (gradiente fisso rosso/nero) invece del material
dinamico, mentre le barre (hue inclusa) sono dinamiche. Fix: `mainStatic = false`
→ il MainPicker usa `FCP_MainPicker.mat` (shader `FCP_Gradient`) e si re-tinge con
la hue. Se su Quest il riquadro apparisse magenta/nero (shader non funzionante in
build), tornare a `mainStatic = true` e valutare un fallback. `mode = 3` (SV + barra H).

---

## 2h. Fase 4a — `PaletteState`: strumenti + attenuazione colori (implementata)

`Assets/Scripts/PaletteState.cs`, componente su `PaletteCanvas`. È l'**unica fonte
di verità** sullo stato della palette e il punto di aggancio del futuro sistema di
disegno:

```csharp
public PaletteTool Tool      { get; }   // Brush | Eraser | Select (enum PaletteTool)
public Color       Color     { get; }   // cablato in 4b (default bianco)
public float       BrushSize { get; }   // cablato in 4c (default 0.5)

public event Action<PaletteTool> ToolChanged;
public event Action<Color>       ColorChanged;       // emesso dalla 4b
public event Action<float>       BrushSizeChanged;   // emesso dalla 4c
```

Il sistema di disegno si abbonerà agli eventi senza conoscere i controlli UI
(pattern del piano originale). Listener aggiunti a runtime in `Awake`, come per
gli altri script; in scena solo riferimenti serializzati.

### Cablaggio 4a

| Campo | Riferimento |
|---|---|
| `brushToggle` / `eraserToggle` / `selectToggle` | i 3 `Toggle` di ToolsRow |
| `colorsGroup` | **nuovo** `CanvasGroup` su `ColorsGrid` |
| `colorsDimmedAlpha` | `0.4` (regolabile da Inspector) |

Comportamento: al cambio strumento `SetTool` aggiorna `Tool`, emette `ToolChanged`
e applica l'attenuazione — con gomma/selezione la sezione colori va ad alpha 0.4,
`interactable = false`, `blocksRaycasts = false` (non cliccabile, né via poke né
via ray). La **selezione del colore non viene toccata**: tornando al pennello la
griglia si riaccende con lo stesso swatch attivo.

### Fase 4b — cablaggio colori

| Campo | Riferimento |
|---|---|
| `swatchToggles` | gli 8 `Toggle` swatch (il colore viene letto dall'`Image` di ciascuno → nessuna duplicazione dei valori) |
| `customColorToggle` | `Toggle` di `CustomColorCell` |
| `colorPicker` | `FlexibleColorPicker` su `PickerPanel/ColorPicker` |
| `customColorFill` | **nuovo** figlio `ColorFill` di `CustomColorCell` (Image full-cell, raycast off, **disattivato** di default; ricreato `Check` sopra di lui per l'ordine di rendering) |

Comportamento:
- swatch selezionato → `Color` = colore dell'`Image` dello swatch → `ColorChanged`;
- cella custom selezionata → `Color` = `colorPicker.color`;
- `colorPicker.onColorChange` (live durante il drag nel picker): attiva `ColorFill`
  e lo tinge col colore corrente — da quel momento la cella mostra il colore custom
  al posto dell'arcobaleno; se la cella custom è selezionata aggiorna anche `Color`.
  Nota: il FCP emette anche all'apertura (colore iniziale), quindi la cella si
  tinge dal primo uso del picker.
- `SetColor` deduplica (nessun evento se il colore non cambia).

Nota tooling: gli array di reference (`swatchToggles`) non si impostano in blocco —
serve il path SerializedProperty per elemento (`swatchToggles.Array.data[N]`).

### Fase 4c — cablaggio slider

| Campo | Riferimento |
|---|---|
| `sizeSlider` | `Slider` su `SizeSlider/LargeSlider` |
| `sizeLabel` | `TMP_Text` su `SizeSlider/LabelsText/TextRight` |

`SetBrushSize` (da `onValueChanged`): aggiorna **sempre** l'etichetta
(`RoundToInt(value*100) + "%"`), poi deduplica e aggiorna `BrushSize` +
`BrushSizeChanged`. In `Awake` viene chiamato col valore corrente dello slider →
etichetta e stato sono allineati alla scena qualunque sia il valore iniziale
(niente più segnaposto statico).

**Fase 4 completa**: la palette è autonoma e consumabile. Il futuro sistema di
disegno dovrà solo: `var palette = FindAnyObjectByType<PaletteState>();` e
abbonarsi a `ToolChanged` / `ColorChanged` / `BrushSizeChanged` (o leggere le
proprietà `Tool` / `Color` / `BrushSize` al momento del tratto).

---

## 2i. Fase 5 — Polish: suoni UI + aptica (implementata)

Nuovo script `Assets/Scripts/PaletteFeedback.cs` su `PaletteCanvas`, agganciato a
`PointableCanvas.WhenPointerEventRaised` — un solo punto di ascolto che copre
**poke e ray, su entrambe le pagine** (i `PointerEvent` ISDK arrivano prima della
traduzione in eventi uGUI):

| Evento ISDK | Suono (clip del package ISDK) | Aptica (controller destro) |
|---|---|---|
| `Hover` (ingresso nel pannello) | `Interaction_BasicRay_Hover` (vol. 0.35) | impulso 0.15 × 15 ms |
| `Select` (press) | `Interaction_BasicPoke_ButtonPress` | impulso 0.5 × 40 ms |
| `Unselect` (release) | `Interaction_BasicPoke_ButtonRelease` | — |

- Ampiezze/volume regolabili da Inspector.
- L'`AudioSource` usato è quello già presente sul prefab UISet (`playOnAwake`
  spento; `spatialBlend` 0 = 2D, eventualmente da spazializzare in futuro).
- L'aptica è hardcoded sul **controller destro** (mano dominante che interagisce);
  il toggle X ha già la sua aptica sul sinistro da Fase 1b.
- Le clip sono referenziate direttamente dal package (`Runtime/Sample/Audio/Content/`).

---

## 2j. Primo consumer: `BrushTip` (anteprima pennello)

Dimostratore end-to-end del disaccoppiamento: `Assets/Scripts/BrushTip.cs`.

```
TrackingSpace/RightHandAnchor/RightControllerAnchor
└── BrushTip            (script; sempre attivo)
    └── TipSphere       (sfera Ø 2 cm a 6 cm davanti al controller,
                         collider rimosso, material Assets/Materials/BrushTip.mat URP/Lit)
```

- **Colore**: si abbona a `PaletteState.ColorChanged` (+ lettura iniziale in `Start`)
  e applica il colore via `MaterialPropertyBlock` (`_BaseColor`) → nessuna istanza
  di material. Non conosce nessun controllo UI: solo `PaletteState`.
- **Visibilità**: la sfera è visibile **solo a palette chiusa** — `Update` confronta
  `paletteContent.activeInHierarchy` (il `PaletteCanvas`, già unico indicatore di
  visibilità governato da `PaletteToggle`). Lo script sta sul padre sempre attivo,
  la sfera figlia viene attivata/disattivata (stesso pattern di `PaletteToggle`).
- Posa (`(0, 0, 0.06)`, scala 0.02) da tarare in build se serve.

---

## 3. Verifica della Fase 1a (build & run)

Su Quest (o simulatore con controller emulati):

1. Il pannello scuro deve apparire **sospeso ~5 cm sopra la faccia nera del controller
   sinistro**, parallelo ad essa, e seguirlo rigidamente (nessun lag, nessun drift).
2. Posa naturale: tenendo il controller davanti al petto, il pannello si legge
   dall'alto come una tavolozza, senza torcere il polso.
3. Il pannello **non** deve interferire con la palla (nessuna collisione).
4. In questa fase il pannello è **sempre visibile**, anche con controller posato /
   hand tracking: il vincolo "visibile solo con controller in mano" arriva con lo
   script della Fase 1b. Ignorare.

**Taratura**: se la posa non è comoda, i valori da ritoccare sono `localPosition` e
`localRotation` di `PalettePanel` (Inspector, anche in Play mode per provare live —
i valori provati in Play vanno poi ricopiati). Tipicamente: più alto/basso → `y`;
avanti/indietro lungo il controller → `z`; parallelismo con la faccia nera → `x`
della rotazione (90 ± offset).

---

## 4. Changelog

| Data | Modifica |
|---|---|
| 2026-06-12 | **Fase 1a.** Creati `PalettePanel` (vuoto, figlio di `LeftControllerAnchor`, pos `(0, 0.10, -0.02)`, rot `(-30, 0, 0)`) e `PlaceholderVisual` (cubo 20×15×0,5 cm, collider rimosso, material `PalettePlaceholder.mat` nuovo in `Assets/Materials/`). Nessun codice. Posa da tarare in build. |
| 2026-06-12 | **Fase 1a — revisione posa** dopo feedback utente: `PalettePanel` ora **parallelo alla faccia nera del controller** (rot `(90, 0, 0)`) e ~5 cm sopra di essa (pos `(0, 0.05, 0)`). Aggiunto requisito alla Fase 1b: palette visibile **solo con controller sinistro in mano** (la visibilità avrà un unico proprietario, lo script `PaletteToggle`: `visibile = apertaConTastoX && controllerInMano`). Posa verificata su Quest 3S. Commit `4dc2986`. |
| 2026-06-12 | **Fase 1b.** Nuovo script `Assets/Scripts/PaletteToggle.cs` su `PalettePanel`: toggle col tasto X (solo con controller in mano), visibilità vincolata a `ControllerActiveState` del rig, animazione di scala 0.15 s, impulso aptico 40 ms al toggle. Riferimenti cablati in scena (`content` = `PlaceholderVisual`, `leftControllerActive` = `LeftInteractions`). |
| 2026-06-12 | **Fase 1b — fix tasto X.** In build il toggle non scattava: `GetDown(Button.Three, LTouch)` non mappa sulla X (con `LTouch` la X è `Button.One`; `Button.Three` vale solo su `Controller.Touch`). Sostituito con `OVRInput.GetDown(OVRInput.RawButton.X)` (tasto fisico, non ambiguo). Verificato su Quest 3S. |
| 2026-06-12 | **Fase 2a.** Eliminato `PlaceholderVisual`; aggiunti `EventSystem`+`PointableCanvasModule` (root) e `PaletteCanvas` (istanza `EmptyUIBackplateWithCanvas`) sotto `PalettePanel`, ridimensionato a 400×300 px (= 20×15 cm a scala 0.0005, su CanvasRoot **e** UIBackplate). `TestToggle` (`ToggleButton_Switch`) dentro UIBackplate come controllo di verifica. `PaletteToggle.content` → `PaletteCanvas`. Interazione attesa via `ControllerRayInteractor` destro esistente; poke in Fase 2b. Verificata su Quest 3S (ray + grilletto). |
| 2026-06-12 | **Fase 2b.** Nessuna modifica: il `ControllerPokeInteractor` destro esiste già nel rig (`Interactors/Controller and No Hand/`), registrato nel `BestHoverInteractorGroup`. Corretta la ricognizione iniziale in §1. Verificata su Quest 3S: toggle azionabile sia via poke sia via ray. |
| 2026-06-12 | Importato **Flexible Color Picker** (Asset Store) in `Assets/FlexibleColorPicker/` (prefab, script, shader custom `FCP_Gradient`, sprite). Compila pulito; resa dello shader su URP/Quest da verificare alla Fase 3d. |
| 2026-06-12 | **Fase 3a.** Eliminato `TestToggle`. Nuova `ToolsRow` (352×64, HLG spacing 16 + ToggleGroup) dentro `UIBackplate` con 3 tile `TextTileButton_IconAndLabel_Toggle` ridimensionate a 106×64: Pennello (isOn), Gomma, Selezione. Icona e label secondaria disattivate, label TMP rinominate. Controlli vivi ma non cablati (Fase 4). Verificata su Quest 3S. Import TMP Essentials (richiesto dalle label TMP); rimossa la cartella `Examples & Extras`. |
| 2026-06-12 | **Fase 3b.** Fix UI specchiata vista da dietro: `BackCover` (Quad one-sided, 2 mm dietro il canvas, material PalettePlaceholder). Nuova `ColorsGrid` (GridLayoutGroup 4×2, celle 44×44, ToggleGroup) con 8 swatch uGUI (Image + Toggle transition None + Check inset come graphic): bianco (default), nero, rosso, arancione, giallo, verde, blu, viola. Verificata su Quest 3S. |
| 2026-06-12 | **Fase 3c.** Nuovo `SizeSlider` (istanza `LargeSlider_LabelsAndIcons`, 300×40) come terza sezione del backplate: slider 0–1 con value 0.5, etichette "Dimensione" / "50%" (segnaposto statico fino alla Fase 4). Verificata su Quest 3S. |
| 2026-06-12 | **Fase 3d.** Disattivata icona volume nella maniglia dello slider. Pannello allargato a 400×340 (20×17 cm). Nuovo `CustomColorButton` ("Colore personalizzato") in fondo alla pagina principale; nuova pagina `PickerPanel` (duplicato svuotato di UIBackplate) con istanza del **Flexible Color Picker** (352×240) e bottone "OK". Nuovo script `PalettePages` su `PaletteCanvas` per la commutazione (listener runtime, riferimenti serializzati). Resa shader FCP su device da verificare. |
| 2026-06-12 | **Fase 3d — revisione** dopo verifica: fix overlap slider/bottone (`SizeSlider` 300×64, il prefab dichiara 40 ma il contenuto è 64); pannello a 400×360 (20×18 cm); bottone custom sostituito da `CustomColorCell` (9ª cella griglia 4×3, RawImage gradiente hue + Button); picker semplificato (spente barre R/G/B/S/V, alpha, hex, dropdown, bottoni modalità) e ingrandito a 352×264 con `MainPicker`/`HPicker`/`ColorPreview` ri-ancorati in grande. |
| 2026-06-12 | **Fase 3d — revisione 2**: `CustomColorCell` entra nel ToggleGroup dei colori (Toggle + Check) così la selezione è esclusiva anche col colore custom; apertura picker spostata nel nuovo script `OpenPickerOnClick` (IPointerClickHandler, convive col Toggle, riapribile sempre); `PalettePages` rifattorizzato con `OpenPicker()`/`ClosePicker()` pubblici. Attenuazione sezione colori con strumento ≠ pennello rimandata alla Fase 4. |
| 2026-06-12 | **Fase 3d — revisione 3**: il riquadro SV del picker non si re-tingeva con la hue — il prefab FCP ha `advancedSettings.mainStatic = true` di default (sprite statica rosso/nero per il MainPicker). Impostato `mainStatic = false` → material dinamico `FCP_Gradient` anche per il riquadro principale. Verificato su Quest 3S: Fase 3 completa. |
| 2026-06-12 | **Fase 4a.** Nuovo script `PaletteState` su `PaletteCanvas` (enum `PaletteTool`, proprietà Tool/Color/BrushSize, eventi `ToolChanged`/`ColorChanged`/`BrushSizeChanged`): cablati i 3 toggle strumento; nuovo `CanvasGroup` su `ColorsGrid` per l'attenuazione (alpha 0.4, non interagibile) quando lo strumento non è il pennello, senza perdere la selezione colore. Verificata su Quest 3S. |
| 2026-06-12 | **Fase 4b.** Cablaggio colori in `PaletteState`: 8 swatch (colore letto dall'Image del toggle), cella custom (`Color` = `colorPicker.color`), `onColorChange` del FCP live. Nuovo overlay `ColorFill` su `CustomColorCell` (sotto il `Check`, ricreato): dal primo uso del picker la cella mostra il colore custom corrente al posto dell'arcobaleno. Verificata su Quest 3S. |
| 2026-06-12 | **Fase 4c.** Cablaggio slider in `PaletteState`: `BrushSize` + `BrushSizeChanged` da `onValueChanged`, etichetta "%" aggiornata live (e allineata in `Awake` al valore di scena). **Fase 4 completa**: palette autonoma, pronta per il sistema di disegno. Verificata su Quest 3S. |
| 2026-06-12 | **Fase 5.** Nuovo script `PaletteFeedback` su `PaletteCanvas` (eventi `PointableCanvas`): suoni UI dalle clip ISDK (hover/press/release) sull'AudioSource esistente (`playOnAwake` off) + aptica sul controller destro (hover 0.15×15 ms, press 0.5×40 ms). |
| 2026-06-12 | **Primo consumer `BrushTip`**: sfera Ø 2 cm sul controller destro (`RightControllerAnchor/BrushTip/TipSphere`, material `BrushTip.mat`) che mostra il colore attivo via `PaletteState.ColorChanged` (MaterialPropertyBlock) ed è visibile solo a palette chiusa. Dimostra il consumo a eventi senza alcun riferimento ai controlli UI. |
