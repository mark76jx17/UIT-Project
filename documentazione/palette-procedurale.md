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
auto-esplicativi al posto delle label lunghe. Pensato in 4 sotto-step (4a–4d).

> ⚠️ **Stato effettivo (riallineamento doc 2026-06-24).** Solo **4a, 4c e 4d** sono
> realmente nel codice committato (`0c9a7ec`). Il cuore dello **Step 4b — lo swap del
> picker da `ColorWheel`/`BrightnessSlider` a `ColorSquare`/`HueBar` — NON è stato
> applicato a `PaletteController`**: il pannello attivo costruisce ancora ruota colori +
> slider luminosità (vedi *§4.1 Stato corrente*). Questa sezione era stata scritta come se
> 4b fosse completo; il testo qui sotto è ora corretto per descrivere ciò che gira davvero.

### 4a — Materiali opachi (fix trasparenza) ✅ applicato
`BrushMaterials.CreateUnlit(color, opaque=false)`: nuova variante **opaca** (`MakeOpaque`:
`_Surface=0`, `ZWrite=1`, blend One/Zero, queue Geometry). `MakeRounded` ora crea sfondi
opachi di default (`opaque=true`) → pannello, striscia, bottoni, separatori, swatch non si
vedono più "attraverso" sul passthrough e il sorting è risolto dal **depth** invece che dalle
render queue trasparenti. `EmptySwatch` da bianco α0.15 a grigio scuro opaco (slot vuoto
leggibile anche senza alpha).

### 4b — ColorSquare + HueBar ❌ NON applicato (codice orfano)
Erano stati **creati** i componenti `ColorSquare` (quadrato Saturazione×Valore, texture
rigenerata al cambio tinta) e `HueBar` (barra tinta orizzontale), entrambi opachi, col tocco
del `BrushTip` (`OnTriggerStay`) o mouse/raggio (`PressAt`). **Ma `PaletteController.BuildPanel()`
non li istanzia**: continua a usare `ColorWheel` + `BrightnessSlider`. Di conseguenza:
- `Assets/Scripts/Drawing/Palette/ColorSquare.cs` e `HueBar.cs` sono **codice morto** (referenziati
  solo in via difensiva da `PaletteRay`/`DesktopBrushSimulator`, rami che non scattano mai perché
  i componenti non esistono in scena).
- `ColorWheel.cs` e `BrightnessSlider.cs` **NON** sono stati eliminati (la doc precedente diceva
  di sì): sono i picker realmente attivi.
- Il pannello **non** è stato alzato a `0.64` m: resta `0.26 × 0.43`.

### 4c — Slider orizzontali auto-esplicativi ✅ applicato
- `AlphaSlider`: rampa del colore corrente (trasparente→pieno) sopra una **scacchiera opaca**
  di sfondo → la trasparenza si legge come scacchiera, niente più see-through sul passthrough.
- `SizeSlider`: riscritto come **cuneo** (sottile→spesso) tinto col colore corrente → mostra
  lo spessore a colpo d'occhio. Entrambi si aggiornano col colore live.
- Rimosse le label testuali "Transparency", "Size", "Recent"; swatch recenti distribuiti su
  5 celle piene (`Split(5)`). Font `SectionFont` non più usato → rimosso.

### 4d — Rifinitura e pulizia ✅ parziale
- `PaletteRay` (raggio) e `DesktopBrushSimulator` (mouse simulatore) gestiscono **sia** il
  picker attivo (`ColorWheel`/`BrightnessSlider`) **sia** quello orfano (`ColorSquare`/`HueBar`):
  i rami per i nuovi componenti sono presenti ma inerti finché 4b non viene applicato.
- `SetQueue` e `SectionFont` inutilizzati: **rimossi** ✅.
- Eliminazione di `ColorWheel.cs`/`BrightnessSlider.cs`: **non fatta** (sono ancora i picker attivi).

### 4.1 Stato corrente della palette (verificato sul codice, 2026-06-24)
Il pannello realmente costruito da `PaletteController.BuildPanel()`:
- **Pannello principale** `0.26 × 0.43` m, opaco (`PanelColor`), angoli arrotondati.
- **Riga colore**: `ColorWheel` (ruota HSV, Ø ~0.10) + `BrightnessSlider` verticale a destra,
  con label **"Bright"** a fianco della barra.
- **Recenti**: 5 swatch su `Split(5)` (persistono in `PlayerPrefs`).
- **`AlphaSlider`** orizzontale (rampa su scacchiera opaca) con label **"Opacity"** a sinistra.
- Separatore → **toggle `Pressure`** + **`SizeSlider`** (cuneo, label **"Size"** a sinistra) → separatore.
- **Strumenti** Draw / Fill / Erase (`Split(3)`, solo testo: `ShowButtonIcons = false`).
- **Toggle `Mirror`**.
- **Striscia pennelli** (`BuildBrushStrip`) a sinistra: 4 tipi con anteprima del tratto.
- **Striscia azioni** (`BuildActionStrip`) a destra: Undo / Redo / Save / Load (solo icone).
- Apertura/chiusura animata col trigger della mano-palette; materiali tutti opachi.

### 4.2 Lavoro residuo (da decidere col prossimo step)
Lo Step 4b è una scelta aperta, **non** una regressione:
1. **Completare 4b** — cablare `ColorSquare`+`HueBar` in `BuildPanel`, alzare il pannello,
   poi eliminare `ColorWheel.cs`/`BrightnessSlider.cs`; **oppure**
2. **Restare sulla ruota** — eliminare i file orfani `ColorSquare.cs`/`HueBar.cs` e i rami
   inerti in `PaletteRay`/`DesktopBrushSimulator`.

In entrambi i casi sparisce la duplicazione dei due picker. Finché non si decide, la palette
funziona con ruota + slider luminosità.

### Step 5 — label degli slider (2026-06-24)
Richiesta utente: rendere comprensibili i tre slider (luminosità / spessore / trasparenza)
con etichette **piccole ma leggibili** che non ostacolino l'uso. Il pannello è verticalmente
pieno (la riga `Mirror` arriva al bordo inferiore), quindi le label sono state messe **a fianco**
degli slider, dentro le righe esistenti, senza rubare spazio né coprire la zona di tocco:
- **"Opacity"** e **"Size"** a sinistra dei rispettivi slider orizzontali (lo slider si accorcia
  di ~0.055 m, resta ampiamente usabile; `AlphaRow`/`SizeRow` con `Left(0.055)` + `Fill()`).
- **"Bright"** a destra della barra verticale di luminosità (spazio prima vuoto nella riga colore).
- Nuovo font `SliderLabelFont = 0.13` (contro `ButtonFont 0.20`), testo bianco, allineato a sinistra.

Testo in inglese per coerenza con Draw/Fill/Erase/Pressure/Mirror (cambiabile in italiano
banalmente). Compila pulito; **verificato in anteprima editor** (`Tools/Palette Preview` +
screenshot): le tre label sono presenti e leggibili, gli slider restano liberi. **Da verificare
su device** la leggibilità della dimensione font.

### Step 6 — ruota colori: zoom e sfondo opaco (2026-06-24)
Due problemi sulla `ColorWheel` (feedback utente):
1. **Zoom di prossimità troppo ampio** — avvicinando il controller la ruota si ingrandiva fino
   a `ZoomMax = 1.9` e sbordava sulla barra di luminosità, coprendo i colori. Prima ridotto a
   `1.35`, poi (feedback utente) reso **geometrico**: lo zoom massimo non è più una costante ma
   è **calcolato in `BuildPanel`** come `distanza dal centro ruota al bordo più vicino del
   pannello / raggio base`. Così la ruota smette di ingrandirsi **esattamente quando l'angolo del
   suo quadrato raggiunge l'angolo del pannello**. Con il layout attuale (ruota a `0.062` m dai
   bordi sinistro/superiore, raggio `0.05`) il limite vale **≈1.24**, passato a
   `ColorWheel.Build(diameter, background, maxZoom)`; `ZoomMax` costante rimosso. Il valore resta
   corretto anche se cambiano dimensioni del pannello o posizione della ruota.
2. **"Quadrato" trasparente attorno alla ruota** — la ruota usava `CreateUnlit(Color.white)`
   **trasparente**; la texture del disco ha alpha 0 negli angoli del quad → in editor gli angoli
   mostravano lo skybox (sembravano un quadrato chiaro), in passthrough mostravano il mondo reale.
   Ora la ruota è **opaca** (`opaque: true`) e `GenerateTexture(size, background)` dipinge gli
   angoli del **colore del pannello** (`PanelColor`, passato da `BuildPanel`): il bordo del disco
   è anti-aliasato fondendolo con lo sfondo invece che con la trasparenza, `alpha = 1` ovunque.
   Risultato: nessun quadrato, niente see-through sul passthrough, disco che "galleggia" sul pannello.
   (Lo `BrightnessSlider` era già `opaque: true`, nessun problema analogo.)

Compila pulito; **verificato in anteprima editor**: il quadrato attorno alla ruota è sparito.
**Da verificare su device**: entità dello zoom in interazione reale e assenza di see-through.

### Step 7 — rifinitura angoli pannello + margine zoom (2026-06-24)
Feedback utente dopo prova:
- **Angoli del pannello più smussati**: raggio del `MainPanel` da `0.020` a **`0.030`** in
  `BuildPanel` (gli altri controlli/strisce invariati).
- **Zoom massimo un filo più basso**: aggiunto un margine `const float wheelZoomMargin = 0.92f`
  (commento `// REGOLA QUI`) al limite geometrico → `wheelMaxZoom = edgeDist / raggio * 0.92`
  (≈1.24 → ≈1.14), così la ruota si ferma poco prima di toccare l'angolo del pannello.

Compila pulito; angoli più morbidi verificati in anteprima editor.

### Step 8 — ruota su disco invece che quadrato (2026-06-24)
Feedback utente: zoomando, **l'angolo del quadrato della ruota "sbucava" oltre il bordo
arrotondato del pannello** (dente scuro brutto). Causa: la ruota era disegnata su un quad
quadrato (`TexturedQuad`); anche con gli angoli color pannello, a zoom ~1.14 l'angolo finiva
~0.005 m fuori dal raccordo del pannello. Limitare lo zoom per evitarlo lo avrebbe ridotto a
~1.06 (quasi nullo).

Fix alla radice: nuova mesh **`RoundedMesh.TexturedDisc(diameter)`** (ventaglio circolare con
UV, centro→(0.5,0.5), bordo→cerchio inscritto della texture). `ColorWheel.Build` la usa al
posto di `TexturedQuad`: **niente angoli → niente da sbucare**, e lo zoom attuale (≈1.14)
resta perché il cerchio entra interamente nel pannello arrotondato (verificato in anteprima
scalando la ruota a 1.14: disco tutto dentro, angolo del pannello intatto). `GenerateTexture`
invariata (il bordo del disco sfuma sul colore del pannello). Il `BoxCollider` resta quadrato
(area di tocco), ininfluente sul visivo.

### Step 9 — poke a segmento invece che a punto (2026-06-24)
Richiesta utente: con il poke l'utente interagisce tramite la **pallina** (`BrushTip`) davanti al
controller; se spinge il controller **oltre** la pallina, questa esce dal controllo (sottile in
profondità) e l'interazione si perde → frustrazione. Voluto: una zona di interazione **a forma di
segmento (invisibile)** tra la pallina e il controller.

Fix in `BrushController.Awake`: il collider del `BrushTip` passa da **`SphereCollider`** (raggio
0.012, un punto) a **`CapsuleCollider`** lungo l'asse Z locale (avanti dal controller), che va
dalla pallina **indietro verso il controller** per `pokeReach` metri (nuovo campo, default
**0.07**; `0` = vecchio comportamento a punto). Così, oltrepassando la pallina, parte della
capsula resta dentro il controllo e `OnTriggerEnter`/`OnTriggerStay` continuano a scattare.

Note:
- Slider e ruota calcolano il valore da **X/Y** della pallina (`PressAt(Tip.position)`): la
  profondità Z non li influenza, quindi il valore scelto resta corretto anche oltrepassando.
- La pallina **visibile** (`BrushCursor`) e `Tip.position` (usata per disegno/erase/fill e per lo
  zoom di prossimità) **non cambiano**: cambia solo la forma del collider invisibile di poke.
- Compila pulito. **Da verificare in Play/device**: che oltrepassando la pallina i controlli
  rispondano ancora e che `pokeReach` (0.07) sia una lunghezza comoda.

### Step 10 — anteprima del tratto + riorganizzazione riga colore (2026-06-24)
Richiesta utente: spostare lo slider luminosità più a destra con la label **sopra**, estendere
un po' la palette, e nella zona liberata mettere un **rettangolo di anteprima** che assuma
colore/dimensione/opacità del colore scelto, per capire con cosa si disegna.

- **Pannello più alto**: `panelSize.y` da `0.43` a `0.46`; riga colore da `0.10` a `0.12`.
- **Riga colore riorganizzata**: ruota a sinistra (`Left(0.10)`), colonna luminosità a destra
  (`Right(0.055)`) con la barra in basso e la label **"Bright" sopra** (centrata), anteprima al
  centro nella zona liberata (`Fill()`).
- **Nuovo componente `ColorPreview`** (`Assets/Scripts/Drawing/Palette/ColorPreview.cs`): una
  **scacchiera opaca** con sopra un **rettangolo arrotondato trasparente** che assume in tempo
  reale `StrokeSettings.Color` (colore + alpha) e **scala con `Size01`** (min↔max). Così mostra
  insieme colore, trasparenza (letta sulla scacchiera) e dimensione del pennello. Lo stato è
  applicato sia in `Update` (live) sia in `Build` (così è corretto anche nell'anteprima editor,
  dove `Update` non gira).

Compila pulito; layout verificato in anteprima editor (swatch bianco di default su scacchiera,
luminosità a destra con label sopra). **Da verificare in Play/device**: che l'anteprima segua
colore/alpha/size in tempo reale e che le proporzioni siano comode.

### Step 11 — luminosità a 3 fermate (nero · colore · bianco) (2026-06-24)
Richiesta utente: lo slider luminosità deve seguire la convenzione **nero in basso, colore pieno
al centro, bianco in alto** (prima era nero→colore, senza bianco).

Modello colore (`StrokeSettings`) rivisto:
- `Val` ora è la **luminosità a 3 fermate** in [0,1]: `0` nero, `0.5` colore pieno, `1` bianco
  (default `0.5`). Non è più la V di HSV.
- `BaseColor` non è più un campo ma una **proprietà calcolata**: `Val ≤ 0.5` → `lerp(nero, puro, Val·2)`,
  `Val > 0.5` → `lerp(puro, bianco, (Val−0.5)·2)`, dove `PureColor = HSVToRGB(Hue, Sat, 1)` (nuova
  proprietà = colore scelto sulla ruota a piena luminosità).
- `SetHSV` non scrive più `BaseColor`; `SetColor` (recenti) imposta tinta+saturazione e `Val=0.5`
  (luminosità neutra). La ruota continua a passare la `Val` corrente quando si sceglie tinta/sat,
  quindi cambiare colore non resetta la luminosità.

`BrightnessSlider` riscritto: gradiente **nero→colore→bianco** generato in codice (3 fermate) e
**rigenerato quando cambia la tinta** (campo `texture` riusato; niente più tint via `_BaseColor`).
Compila pulito; lo slider renderizza (in editor, colore di default bianco → nero→bianco).
**Da verificare in Play**: con un colore scelto, basso=nero, centro=colore, alto=bianco; e che
`AlphaSlider`/`SizeSlider`/`ColorPreview` (che usano `BaseColor`) seguano la luminosità.

### Step 11b — la RUOTA si schiarisce/scurisce con la luminosità (2026-06-24)
Seconda parte della richiesta. **Chiarimento utente**: per "i colori della palette" si intendeva
la **ruota dei colori**, non l'interfaccia. Quindi un primo tentativo che tingeva pannello/bottoni
(helper `Themed`/`ApplyTheme`/`RegisterThemed`) è stato **annullato** (PaletteController tornato
com'era), e l'effetto è stato spostato sulla ruota.

Implementazione (`ColorWheel`): la texture del disco riflette la **luminosità corrente** (`Val`)
con la stessa modulazione del colore disegnato — nero (`Val 0`) ↔ colore pieno (`Val 0.5`) ↔
bianco (`Val 1`). `GenerateTexture(size, background, val, reuse)` applica per pixel
`lerp(nero, puro, val·2)` / `lerp(puro, bianco, (val−0.5)·2)`; `Regenerate()` rigenera (riusando
la `Texture2D`) e in `Update` scatta **solo quando `Val` cambia** (cioè muovendo la luminosità o
scegliendo un recente). Texture ridotta a `160` px per alleggerire la rigenerazione.

Così, abbassando la luminosità la ruota diventa scura (fino a nera), alzandola schiarisce (fino a
bianca), restando coerente con anteprima e tratto. Compila pulito; ruota a colori pieni a `Val 0.5`
verificata in anteprima. **Da verificare in Play**: ruota che scurisce/schiarisce con lo slider.
**Nota**: a `Val 1` (ruota bianca) il pomello bianco si confonde — eventualmente dargli un colore
di contrasto in uno step successivo se dà fastidio.

### Step 12 — fix see-through icone bottoni laterali (2026-06-24)
Richiesta utente: togliere il see-through dagli altri bottoni delle strisce laterali (anteprime
pennello a sinistra, icone Undo/Redo/Save/Load a destra).

**Causa radice.** Lo shader URP/Unlit usa un blend **separato per l'alpha**:
`Blend [_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]`. Il materiale trasparente di
default ha alpha blend **`One/Zero`**, quindi un quad icona trasparente **sovrascrive** l'alpha
del framebuffer con il proprio (≈0 nelle zone trasparenti dell'icona). Su Quest è il canale alpha
a decidere l'occlusione del passthrough: alpha 0 = buco = see-through, **anche se il bottone sotto
è opaco** (il suo alpha 1 viene sovrascritto dall'icona).

**Fix.** Nuovo `BrushMaterials.PreserveDestAlpha(material)` che imposta l'alpha blend a
**`Zero/One`** (`_SrcBlendAlpha`/`_DstBlendAlpha`): l'icona **preserva** l'alpha di destinazione
(l'1 opaco del bottone) invece di sovrascriverlo; il colore (glifo/anteprima) continua a fondersi
normalmente. Applicato in `PaletteController.MakeTexQuad`, quindi vale per **tutte** le
anteprime pennello e le icone azione. Funziona con qualsiasi colore del bottone (anche selezionato
= accent), senza bisogno di rendere opache le texture.

Compila pulito; il fix riguarda l'alpha del passthrough → **non visibile in editor**, da
verificare sul device. (Stessa causa potenziale su `AlphaSlider`/`ColorPreview`, che però sono
trasparenti **di proposito** su scacchiera: lì il buco mostrerebbe passthrough invece della
scacchiera — se in VR si nota, applicare lo stesso `PreserveDestAlpha`.)
