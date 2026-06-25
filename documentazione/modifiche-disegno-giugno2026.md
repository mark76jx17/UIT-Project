# Modifiche al sistema di disegno — giugno 2026

> Documento di sintesi delle modifiche fatte al sistema di disegno 3D in MR
> (`Assets/Scripts/Drawing/`). Per ogni gruppo: **cosa** è stato fatto, **perché**
> e **cosa cambia** in pratica. Obiettivo generale: rendere l'app più fluida,
> robusta e usabile, avvicinandola a Gravity Sketch / Tilt Brush.

**Stato:** un primo blocco di ottimizzazioni è già su `git` (commit `a4f538e`).
Tutto il resto (qualità disegno, fill, feedback, nuove feature) è applicato **sui
file ma non ancora committato** e **non ancora testato sul visore** — va verificato
e tarato in editor/Quest.

---

## Panoramica della palette (cosa offre l'interfaccia)

Le **modalità** vere e proprie sono **3** (Draw / Fill / Erase), combinabili con i **4
tipi di pennello** e con la **modalità spessore** (fisso o a pressione). *Mirror / Grid
/ Snap* sono **interruttori** che si possono tenere accesi insieme.

- **Strumenti (cosa fa il grilletto) — uno solo attivo alla volta**
  - **Draw** — disegna i tratti
  - **Fill** — riempie col colore corrente un contorno chiuso
  - **Erase** — cancella (parziale e annullabile)
- **Tipi di pennello — 4**
  - **Tubo (Round)**, **Nastro (Ribbon)**, **Glow**, **Tratteggiato (Dashed)**
- **Spessore — toggle "Pressure"**
  - OFF: spessore **fisso** dallo slider Size · ON: spessore dalla **pressione** del grilletto
- **Colore**
  - Ruota HSV + slider luminosità + 5 colori recenti + slider trasparenza
  - (+ **contagocce**: tieni A/X vicino a un tratto per ripescarne il colore)
- **Interruttori on/off**
  - **Pressure** (spessore da pressione) · **Mirror** (simmetria) · **Grid** (griglia di
    riferimento) · **Snap** (linee dritte vincolate agli assi)
- **Azioni**
  - **Undo / Redo** · **Save / Load** (con backup automatico) · **Clear** (svuota la scena)

---

## 1. Performance su Quest

### Cache dei materiali con chiave colore quantizzata
- **Cosa:** la cache dei materiali (`BrushMaterials`) ora arrotonda il colore a 32
  livelli per canale prima di usarlo come chiave; aggiunto `ClearCache()`.
- **Perché:** i colori arrivano da ruota/slider continui. Senza arrotondamento ogni
  tinta leggermente diversa creava un `Material` nuovo mai liberato → la memoria
  cresceva senza limite (un materiale per tratto).
- **Effetto:** il numero di materiali è proporzionale ai colori *visibilmente*
  distinti usati, non al numero di tratti. Nessun calo di qualità percepibile.

### Cap e punti su mesh sferica low-poly condivisa
- **Cosa:** i "tappi" (cap) alle estremità dei tratti e i punti singoli usano una
  sfera low-poly (~60 vertici) **condivisa**, invece della primitiva sfera di Unity
  (~515 vertici). Nuovo file `BrushMeshes.cs`.
- **Perché:** ogni tratto ha 2 cap; con la primitiva Unity raddoppiavano i vertici
  del tratto, e ogni cap era una mesh separata in memoria.
- **Effetto:** molti meno vertici e una sola mesh condivisa per tutti i cap/punti.

### Query fisiche senza garbage (NonAlloc) e collider di presa più radi
- **Cosa:** tutte le `OverlapSphere`/`Raycast` di prossimità (pennello, grab, ray
  palette) usano le varianti `NonAlloc` con buffer riusato; i collider di presa lungo
  il tratto sono uno ogni 8 campioni (prima ogni 4), con raggio leggermente maggiore.
- **Perché:** le versioni normali allocano un array a ogni frame (→ garbage collector
  → micro-scatti); i collider erano troppi (centinaia di GameObject per disegni ricchi).
- **Effetto:** meno garbage per frame e meno oggetti in scena, a parità di presa.

### `Camera.main` in cache e aggiornamenti UI solo al cambio
- **Cosa:** `PaletteController` memorizza la camera invece di chiamare `Camera.main`
  ogni frame; i colori dei pulsanti vengono riscritti solo quando lo stato cambia.
- **Perché:** `Camera.main` fa una ricerca interna a ogni chiamata; riscrivere i
  materiali ogni frame è lavoro inutile.
- **Effetto:** meno costo per frame nella palette.

> Nota: era stato aggiunto anche un "throttling" dell'upload della mesh (ricaricarla
> ogni N punti invece che a ogni punto) per i tratti lunghissimi, ma **è stato
> rimosso**: faceva crescere la linea a scatti durante il disegno. La soluzione
> definitiva (upload incrementale del solo tratto nuovo) è rimandata.

---

## 2. Qualità del disegno (pennello e tratto)

### Modalità Pressione resa effettiva e testabile
- **Cosa:** ampliato il range della pressione (frazione minima 0.25 → **0.10**); il
  cursore a riposo mostra lo spessore **massimo** raggiungibile (prima il minimo);
  i lati della mesh si calcolano dal raggio **massimo** del tratto. Nel simulatore
  desktop la pressione si varia con la **rotella mentre si disegna**.
- **Perché:** la pressione "non sembrava funzionare": in editor il trigger simulato
  era costante (spessore costante), il range era stretto e il cursore mostrava sempre
  il minimo. Inoltre un tratto che s'ingrossava restava sfaccettato (lati fissati a
  inizio tratto).
- **Effetto:** la pressione ora modula visibilmente lo spessore ed è verificabile
  anche senza visore.

### Smoothing indipendente dal frame-rate
- **Cosa:** la lisciatura di posizione e raggio del tratto non usa più un fattore
  fisso per frame, ma uno corretto con `Time.deltaTime` (riferimento 72 Hz).
- **Perché:** con un fattore fisso il tratto risultava più o meno liscio a seconda
  del frame-rate (72/90/120 Hz, editor vs visore).
- **Effetto:** resa del tratto coerente a qualsiasi frame-rate.

### Orientamento del Nastro (Ribbon) col polso
- **Cosa:** il primo "frame" della mesh del tubo può essere allineato all'alto del
  controller (`UpHint`); per il Nastro questo orienta la faccia piatta.
- **Perché:** prima l'orientamento era arbitrario → il nastro guardava in una
  direzione casuale, rendendolo poco utilizzabile.
- **Effetto:** ruotando il polso si controlla l'inclinazione del nastro.

### Tratteggio (Dashed) proporzionale allo spessore
- **Cos'è il "Dashed":** è uno dei **4 tipi di pennello** disponibili —
  **Tubo** (tratto pieno e tondo), **Nastro** (tratto piatto a nastro), **Glow**
  (tratto luminoso) e **Tratteggiato (Dashed)**. Il Dashed disegna lo stesso tubo 3D
  ma con una texture che alterna pieno/vuoto lungo la lunghezza: il tratto appare come
  una **linea tratteggiata** ( ─ ─ ─ ) invece che continua.
- **Cosa:** la frequenza dei trattini ora scala con lo spessore del tratto (`UvScale`).
- **Perché:** prima il passo del tratteggio era fisso, identico per tratti sottili e
  spessi.
- **Effetto:** trattini coerenti con la dimensione del tratto (tratto spesso = trattini
  più lunghi).

---

## 3. Riempimento (Fill)

### Niente collider sul riempimento
- **Cosa:** la superficie di riempimento non ha più un `MeshCollider` proprio.
- **Perché:** era convesso, quindi su forme concave (es. "C", stella) copriva tutto
  l'inviluppo, rendendo imprecise presa e gomma. (Un trigger non-convex non è
  supportato da Unity.)
- **Effetto:** la forma riempita si afferra/cancella dal **contorno** (il tratto, che
  ha già i suoi collider) — comportamento più prevedibile.

### Riempimento di anelli formati da più tratti
- **Cosa:** se un singolo tratto chiuso non è riempibile, il secchiello prova a
  chiudere un anello composto da **più tratti uniti col magnete** (`FillGroup`).
- **Perché:** prima il fill funzionava solo su un unico tratto i cui estremi erano
  vicini.
- **Effetto:** si possono riempire contorni disegnati in più tratti. *(L'ordine del
  contorno è euristico: da tarare; in caso di contorno aperto non fa nulla.)*

---

## 4. Manipolazione oggetti (grab a due mani)

### Rotazione completa con "roll"
- **Cosa:** la rotazione a due mani ora include la torsione attorno all'asse tra le
  mani (roll), non solo lo "swing".
- **Perché:** con `FromToRotation` non si poteva ruotare un oggetto torcendo i polsi.
- **Effetto:** manipolazione a due mani come in Gravity Sketch.

### Limite di scala
- **Cosa:** la scala a due mani è limitata al range [0.1, 10] per gesto.
- **Perché:** si poteva ridurre un oggetto a zero o ingigantirlo a dismisura.
- **Effetto:** niente più collasso/esplosione accidentale.

---

## 5. Palette: logica e pulizia

### Ray della palette su layer dedicato (fix di un bug)
- **Cosa:** tutti i controlli della palette stanno su un layer dedicato
  (`PaletteController.PaletteLayer = 30`); il `PaletteRay` interroga solo quel layer.
- **Perché:** il raycast (passato a buffer fisso per non allocare) poteva, in un
  disegno denso, riempirsi dei collider dei tratti prima di raggiungere il controllo
  della palette → palette non più cliccabile col ray.
- **Effetto:** il ray ignora del tutto i tratti, è più robusto e veloce.
- **⚠️ Da verificare:** che il layer 30 sia libero nel progetto.

### Dispatch unificato + rimozione codice morto
- **Cosa:** introdotta l'interfaccia `IPaletteControl` implementata da ruota e slider;
  il dispatch dell'input (ray e simulatore) la usa al posto di una catena di `if` per
  tipo. Eliminati i controlli **mai usati** `ColorSquare` e `HueBar`.
- **Perché:** ogni controllo era gestito in 3 punti diversi → fonte di bug (è così
  che erano sopravvissuti i due controlli morti).
- **Effetto:** aggiungere un controllo richiede solo di implementare l'interfaccia;
  meno codice e meno errori.

---

## 6. Feedback visivo e usabilità

### Messaggi a schermo (toast)
- **Cosa:** nuovo `Toast` che mostra un messaggio a video per **Salva / Carica /
  Export** (e relativi errori).
- **Perché:** queste azioni prima scrivevano solo `Debug.Log`, **invisibile sul
  visore**: non si sapeva se avessero funzionato.
- **Effetto:** conferma visibile. *(Tolti di proposito da gomma/undo/redo/svuota: lì
  l'effetto è già visibile e i messaggi ripetuti davano fastidio.)*

### Gomma con anteprima e annullabile
- **Cosa:** in modalità gomma l'oggetto sotto la punta è **evidenziato in rosso**
  prima di cancellare; la cancellazione è **annullabile**; inoltre è **parziale**
  (toglie solo la porzione toccata e ricostruisce i pezzi rimasti).
- **Perché:** prima la gomma cancellava alla cieca, l'intero oggetto, e **non era
  annullabile** (cancellazione definitiva).
- **Effetto:** cancellazione più sicura e precisa. Ha richiesto di riscrivere la
  cronologia (`StrokeHistory`) come stack di azioni Aggiungi/Cancella/Sostituisci.

### Indicatori e aiuti
- **Cosa:** icona dello strumento corrente sulla punta del pennello; anteprima del
  pennello selezionato **tinta col colore corrente** (indicatore "pennello corrente");
  **hint del magnete** (pallina dove il tratto si aggancerebbe); hover dei pulsanti
  della palette (si ingrandiscono quando puntati).
- **Perché:** non c'era modo di sapere a colpo d'occhio strumento/colore/aggancio, e
  i pulsanti reagivano solo alla pressione.
- **Effetto:** più chiarezza su cosa sta per succedere.

### Contagocce (eyedropper)
- **Cosa:** tenendo **A/X** (mano del pennello) con la punta vicino a un tratto se ne
  preleva il colore (in editor: tasto **I**).
- **Perché:** non c'era modo di riprendere un colore già usato se non dai 5 recenti.
- **Effetto:** si "pesca" il colore da qualsiasi tratto.

### Aiuti spaziali
- **Griglia (toggle "Grid"):** una griglia di linee sul **pavimento** (4×4 m, celle da
  25 cm) che si accende/spegne. È **solo un riferimento visivo, non cambia cosa
  disegni**: serve a percepire distanze, scala e orientamento mentre si disegna in
  aria, dove in passthrough non c'è un piano di riferimento. Come la griglia di
  Blender / Gravity Sketch.
- **Piano specchio:** reso più evidente (cornice luminosa) e ora
  **afferrabile/spostabile** col grip (prima era fisso dove lo attivavi).
- **Snap ad assi (toggle "Snap"):** quando è acceso, mentre disegni il tratto viene
  vincolato all'**asse del mondo più vicino** (X/Y/Z) a partire dal punto iniziale →
  si disegnano **linee dritte** invece di curve a mano libera (l'asse scelto è quello
  verso cui si muove di più la mano). È come tenere premuto Shift nei programmi 2D.
  **Non** è "snap alla griglia": blocca solo la *direzione* del tratto, non lo aggancia
  alle celle. *(Il nome "Snap" è ambiguo: valutare di rinominarlo "Dritto"/"Assi".)*
- **Clear:** pulsante che **svuota la scena**, con backup automatico.
- **Perché:** mancavano riferimenti di profondità/scala, lo specchio era fisso, non
  c'era un modo di fare linee dritte né di ripartire da zero in sicurezza.
- **⚠️ Da verificare:** la riga toggle ora ha 3 pulsanti (Mirror/Grid/Snap) nello
  spazio di 2 → le etichette potrebbero risultare strette; altezza griglia a y=0.

---

## 7. Robustezza / correttezza

- **Reset dello stato statico all'avvio** (`DrawingRig`): in editor, senza il reload
  del dominio, cronologia e cache materiali sopravvivevano tra una sessione di Play e
  l'altra, conservando riferimenti a oggetti distrutti.
- **Backup automatico** prima di Load/Clear (`drawing_backup.json`): un tocco
  accidentale non distrugge più il lavoro in modo irreversibile.
- **Guardia su salvataggio illeggibile**: un JSON corrotto non manda più in errore il
  caricamento.
- **Guardia anti-NaN** nel generatore di mesh: punti coincidenti (da JSON o
  ricampionamento) non creano più anelli degeneri.
- **Glow senza GI realtime**: l'emissione non attiva un percorso di illuminazione
  globale inutile su Quest (l'effetto arriva dal Bloom).

---

## 8. Fix di build Android (Meta XR SDK)

- **Cosa:** corretto l'errore di build *"Manifest merger failed — Namespace
  'com.oculus.Integration' is used in multiple modules: :InteractionSdk:, :OVRPlugin:"*
  rinominando il namespace dentro `InteractionSdk.aar`.
- **Perché:** bug noto di **Unity 6 + Meta XR SDK**: due librerie Android dichiarano
  lo stesso namespace, vietato da AGP 8.
- **Effetto:** la build APK passa il merge del manifest.
- **⚠️ Attenzione:** la patch è in `Library/PackageCache` (non in git): se si
  re-importa il pacchetto Meta va riapplicata. Soluzioni definitive: aggiornare il
  Meta SDK a v203+ oppure usare Unity 6000.0 LTS.

---

## 9. Nuovi comandi / scorciatoie

**Sul visore**
- Contagocce: tieni **A/X** (mano pennello) vicino a un tratto.
- Specchio: toggle **Mirror**, poi afferralo col **grip** per spostarlo.
- **Grid** / **Snap** / **Clear**: pulsanti in palette.

**Nel simulatore desktop (editor)**
- Rotella: pressione (mentre disegni) / distanza pennello (a riposo).
- **I** contagocce · **G** griglia · **P** apri/chiudi pannello · **N** nuovo (svuota)
  · **M** specchio · **F** secchiello · **E** gomma · **B** tipo pennello ·
  **Z/X** undo/redo · **F5/F9** salva/carica · **O** export OBJ.

---

## 10. Limitazioni note / da tarare in editor

- Riga toggle a 3 pulsanti: etichette potenzialmente strette.
- Dimensione/posizione dei toast (valori di partenza).
- Altezza della griglia (assume tracking a livello pavimento, y≈0).
- Fill su più tratti: ordine del contorno euristico.
- Parametri da tarare: raggio gomma parziale, densità tratteggio, range pressione.
- **Non fatti di proposito** (interni, rischiosi senza test visivo): cap integrati
  nella mesh del tubo, attenuazione delle strozzature sulle curve strette,
  triangolazione robusta su contorni auto-intersecanti.

---

## 11. Elenco file

**Nuovi**
- `Geometry/BrushMeshes.cs` — sfera low-poly condivisa per i cap e i punti dei tratti
- `Geometry/DrawingExporter.cs` — esporta la scena in formato OBJ *(committed)*
- `Geometry/ReferenceGrid.cs` — griglia di riferimento a pavimento (toggle "Grid")
- `Geometry/MirrorHandle.cs` — marcatore che rende il piano specchio afferrabile
- `Toast.cs` — messaggi a schermo (Salva / Carica / Export)
- `Palette/IPaletteControl.cs` — interfaccia comune di ruota e slider (input unificato)

**Eliminati**
- `Palette/ColorSquare.cs`, `Palette/HueBar.cs` — vecchi controlli colore mai usati
  (codice morto)

**Modificati**
- `Geometry/BrushController.cs` — il pennello: modalità **pressione**, **contagocce**,
  **gomma** con anteprima rossa e cancellazione parziale, **hint del magnete**, **snap
  ad assi** (linee dritte), **icona strumento** sulla punta, lisciatura indipendente
  dal frame-rate, query fisiche senza garbage
- `Geometry/Stroke.cs` — il singolo tratto: cap su mesh condivisa, dettaglio mesh dal
  raggio **massimo**, orientamento del **Nastro**, cancellazione **parziale**,
  riempimento di anelli fatti di **più tratti**, **tratteggio proporzionale** allo spessore
- `Geometry/TubeMesher.cs` — generatore della mesh a tubo: guardia anti-NaN,
  orientamento del Nastro, tratteggio proporzionale *(l'ottimizzazione "upload a blocchi"
  è stata aggiunta e poi rimossa: faceva crescere la linea a scatti)*
- `Geometry/BrushMaterials.cs` — materiali: cache colore senza memory-leak, Glow senza
  illuminazione globale inutile
- `Geometry/StrokeHistory.cs` — undo/redo riscritto a stack di azioni
  (Aggiungi / Cancella / Sostituisci) → **gomma annullabile**
- `Geometry/GrabController.cs` — presa: rotazione con **roll**, **limite di scala**,
  afferra anche lo **specchio**, query senza garbage
- `Geometry/Mirror.cs` — piano specchio più evidente e **spostabile**
- `Geometry/StrokeHighlight.cs` — evidenziazione **rossa** per l'anteprima della gomma
- `Geometry/DrawingStore.cs` — salvataggi: **backup automatico**, "nuovo/svuota",
  guardia su file corrotto, toast di conferma
- `StrokeSettings.cs` — aggiunto il flag dello **Snap ad assi**
- `DrawingRig.cs` — reset dello stato all'avvio, creazione del **Toast**
- `DesktopBrushSimulator.cs` — simulatore desktop: **pressione variabile** con la
  rotella, gomma annullabile, nuovi tasti (I/G/P/N), input unificato, trascinamento
  dello specchio
- `Palette/PaletteController.cs` — layer dedicato, pulsanti **Clear / Grid / Snap**,
  indicatore "**pennello corrente**", **hover** dei pulsanti, apri/chiudi pannello,
  camera in cache
- `Palette/PaletteRay.cs` — ray su **layer dedicato**, hover dei pulsanti, input unificato
- `Palette/PaletteButton.cs` — registro dei pulsanti, stato **hover**
- `Palette/{ColorWheel,AlphaSlider,BrightnessSlider,SizeSlider}.cs` — implementano
  l'interfaccia comune `IPaletteControl`
- `Palette/ToolIcon.cs` — aggiunta l'icona **cestino** (Clear) *(committed)*
