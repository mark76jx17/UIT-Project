# Design: Grid come "foglio a quadretti" 3D + Line elastica — luglio 2026

> Spec approvata prima dell'implementazione (2026-07-03). La griglia a pavimento non serve
> in passthrough (il pavimento reale dà già il riferimento): la grid diventa un **foglio da
> disegno a quadretti** posizionabile nella stanza — riferimento visivo per misurare e
> allineare, e **piano di disegno** quando Line è attiva insieme. Line stessa viene rifatta:
> da snap agli assi del mondo a **linea elastica che segue il controller** (oblique incluse).

## Requisiti (dalle risposte dell'utente)

1. Grid = **riferimento visivo** (quadretti come un foglio da disegno, proiettato nel 3D).
2. Con **Grid + Line attive insieme** il foglio diventa **piano di disegno** (tratto appoggiato).
3. Foglio **posizionabile col grab**, stesso pattern della palette (highlight + grip).
4. Il foglio **sostituisce** la griglia a pavimento (niente doppio elemento).
5. I bottoni **Draw e Line restano selezionabili con Grid attiva** (nessun disable indotto).
6. **Line deve seguire il controller**: oggi `ConstrainToAxis` vincola all'asse del mondo
   dominante ricalcolato punto per punto → niente oblique e "salti" di asse a inizio tratto.

## 1. Il foglio (`GridSheet`, nuovo componente)

- Al toggle Grid compare **verticale davanti alla testa** (~0.6 m), ruotato verso l'utente
  (solo yaw, up del mondo), poi resta **fisso nella stanza**.
- Foglio ~**1.0 × 0.7 m**, **celle 5 cm**, **righe maggiori ogni 4 celle** (20 cm) più
  marcate + bordo perimetrale sottile. Tutte costanti tarabili.
- **Niente fondo**: solo righe — attraverso il foglio si vedono passthrough e disegno.
- Righe come **quad sottili in mesh** (le `MeshTopology.Lines` attuali sono hairline da 1 px,
  quasi invisibili sul visore), materiale unlit trasparente double-sided con alpha da
  `_BaseColor` (lezione del grab-highlight: le texture alpha generate a runtime falliscono
  sul device). Due materiali: righe minori (alpha bassa) e maggiori (più marcate).

## 2. Grab del foglio — stesso pattern della palette

- Avvicinando il controller-pennello al bordo: **stessa striscia bianca** che scorre sul
  contorno; **grip** trascina, rilascio fissa. Niente docking: il foglio è sempre "fissato",
  il grip lo sposta. Riaccendere il toggle lo riporta davanti all'utente.
- La **matematica pura** del highlight (perimetro arrotondato campionato + striscia a
  finestra) esce da `PaletteController` in un **helper statico `GrabRibbon`** condiviso:
  nella palette i 3 metodi (`BuildPerimeter`/`SampleContour`/`RebuildRibbon`) diventano
  deleghe, **zero cambi di comportamento** e minimo rischio di conflitti (il file è in
  lavorazione da un collaboratore).
- Mentre si trascina il foglio: presa dei tratti e disegno **sospesi** come per la palette.
  `GrabController` legge `PaletteController.SuppressBrushGrab || ReferenceGrid.SuppressBrushGrab`
  (flag separati: la palette scrive il suo ogni frame, un flag unico verrebbe sovrascritto).
- Se palette e foglio sono entrambi a portata di grip, **ha precedenza il più vicino**
  (in pratica: il foglio non aggancia se la palette è in range ed è più vicina).

## 3. Piano di disegno — solo con Grid + Line insieme

- **Grid da sola**: puro riferimento visivo, il tratto non viene toccato.
- **Grid + Line**: se la punta è **entro ~10 cm dal piano** e dentro il rettangolo del
  foglio, gli estremi della linea vengono **proiettati ortogonalmente sul piano** (offset
  ~2 mm davanti alle righe contro lo z-fighting) → si disegna "sulla carta". Fuori range,
  Line si comporta da linea elastica libera (vedi §4).
- **Latch per tratto**: un tratto iniziato sul foglio resta sul foglio fino al rilascio;
  uno iniziato lontano non viene mai catturato (stessa filosofia del latch del PaletteRay).
- Feedback: **marker** piccolo sul foglio alla posizione proiettata mentre si è in range
  (la "punta della matita sulla carta").

## 4. Line elastica (fix del comportamento attuale)

- Alla pressione del trigger si **ancora il punto di partenza**; mentre si tiene premuto la
  linea dritta **segue la punta in qualsiasi direzione** (elastico start→tip, oblique
  incluse); al rilascio si finalizza. Sostituisce `ConstrainToAxis` (che viene rimosso).
- Implementazione: con Line attiva il tratto vive come **2 punti** (start + end aggiornato
  ogni frame) invece dell'append con campionamento adattivo; serve un piccolo supporto in
  `Stroke` per aggiornare l'ultimo punto e rigenerare la mesh. Mirror: il tratto specchiato
  aggiorna gli stessi 2 punti riflessi.
- Con Grid in range (§3) i due estremi sono proiettati sul piano.

## 5. Bottoni palette

- Grid resta il toggle attuale (draw-only come da commit del collaboratore). **Attivare Grid
  non disabilita nulla**: Draw, Line e Mirror restano selezionabili (il disable draw-only
  dipende solo dallo strumento attivo — da verificare esplicitamente nel test in visore).
- Toggle off: foglio distrutto, flag reset (`OnDestroy` azzera i suppress).

## 6. File toccati

- `Geometry/ReferenceGrid.cs` — riscritto ma **stessa API statica** (`Toggle`/`Enable`/
  `Disable`/`Enabled`) → palette, flick stick e simulatore desktop invariati; espone i flag
  `SuppressBrushGrab`/`IsGrabbing` del foglio.
- `Geometry/GridSheet.cs` — **nuovo**: mesh quadretti, grab, highlight, marker.
- `Geometry/GrabRibbon.cs` — **nuovo**: helper statico perimetro+striscia (estratto).
- `Palette/PaletteController.cs` — i 3 metodi del highlight delegano a `GrabRibbon`.
- `Geometry/GrabController.cs` — legge anche il suppress del foglio (1 riga).
- `Geometry/BrushController.cs` — Line elastica al posto di `ConstrainToAxis` (rimosso) +
  proiezione sul foglio; `lineStroke`/`lineOnSheet` latchati per tratto in `BeginPress`.
- `Geometry/Stroke.cs` — `SetLineEnd` (segmento start→end ricampionato a 1 cm, mesh
  rigenerata per frame, cap di coda che segue l'estremo) e `FinishLine` (come `Finish`
  più i collider di presa lungo il segmento).
- `documentazione/grid-foglio-design.md` — questa spec (aggiornata a fine lavoro con
  l'esito, come da convenzione).

## Casi limite

- **Mancini**: ovunque `StrokeSettings.BrushHand` (come la palette).
- **Salvataggio**: il foglio è una guida, non contenuto — non entra in `DrawingStore`.
- **Editor/simulatore**: tasto G già esistente; senza visore niente grab (come la palette,
  `XRSettings.isDeviceActive`).
- **Tap con Line attiva**: un tap resta un tap (punto singolo), gestito dal ramo esistente.

## Parametri tarabili (prima stesura)

Foglio 1.0×0.7 m; cella 0.05 m; maggiore ogni 4; spessore righe ~1.5/2.5 mm; alpha
minori/maggiori ~0.18/0.35; range proiezione 0.10 m; offset anti z-fighting 0.002 m;
`GrabRange`/`HighlightRange` come la palette (0.09/0.22).

## Verifica prevista

Compilazione 0 errori; test in visore a carico dell'utente: foglio visibile/leggibile in
passthrough, grab fluido con la stessa affordance della palette, Grid+Line = righe dritte
sulla carta, Line libera = oblique che seguono il controller, Draw/Line/Mirror selezionabili
con Grid attiva, nessuna regressione su Mirror/Fill/Erase. Commit a carico dell'utente.

## Esito implementazione (2026-07-03)

Implementato tutto come da spec, compilazione **0 errori / 0 warning**. Note oltre la spec:

- **Latch sulla carta**: durante il tratto la proiezione usa `ProjectClamped` (senza vincolo
  di distanza, sempre dentro il rettangolo) così il tratto iniziato sul foglio non "scappa"
  se la punta esce dal range; `TryProject` (con range) decide solo l'ingresso a inizio tratto.
  Il foglio si usa da **entrambi i lati** (l'offset anti z-fighting segue il lato della punta).
- **Linea elastica**: durante il trascinamento il tratto è il segmento ricampionato a passo
  1 cm (tetto 100 segmenti) rigenerato ogni frame — punti reali, così gomma parziale, magnete
  e fill vedono una polilinea normale. Al rilascio `FinishLine` → lisciatura/rastrematura
  standard di `Finish` (i punti collineari restano una retta) + collider di presa lungo il
  segmento. Il cap di coda segue l'estremo e si chiama "Cap": `Finish` lo ricrea rastremato.
- **`lineStroke` latchato per tratto**: un toggle di Line a metà pressione (possibile col
  ray dell'altra mano) non cambia il tratto in corso.
- **Mutua esclusione dei grab**: la palette non aggancia se il grip sta già trascinando il
  foglio (`ReferenceGrid.IsGrabbing`); il foglio, simmetricamente, cede la precedenza quando
  la palette è a portata (`PaletteController.SuppressBrushGrab`). `PaletteRay` sospende le
  pressioni anche mentre si trascina il foglio.
- Il **tap** con Grid+Line confronta posizioni proiettate (altrimenti misurerebbe la
  distanza punta-foglio, mai un tap): resta un punto singolo.
- Il marker "matita sulla carta" è visibile con Line attiva e punta in range **anche prima
  di premere** (anteprima di dove nascerà la linea).
- **Grab solo al bordo** (feedback dal primo test in visore, 2026-07-03): la distanza di
  grab è misurata dal **contorno** del foglio, non dal rettangolo pieno — dentro il foglio
  cresce verso il centro. Così sopra la struttura disegnata sulla carta il grip resta
  libero di afferrare i tratti; il foglio si aggancia solo entro `GrabRange` dalla cornice
  (fascia di ~9 cm a cavallo del bordo, dove comunque non si disegna).
