# Grab & placement della palette: sgancio dalla mano e fissaggio nella stanza — giugno 2026

> La palette può essere **staccata dalla mano e fissata in un punto della stanza** (stile Meta
> Quest quando sposti un pannello): avvicinando il controller-pennello ai bordi della palette,
> questi si **evidenziano**; tenendo il **grip** della mano-pennello la si **trascina** e
> rilasciando la si **fissa**; col **trigger della mano-palette** la si **riaggancia** alla mano.

## Macchina a stati (`PaletteController`)

`enum PlaceMode { Docked, Grabbing, Placed }`, campo `placeMode`.

- **Docked** (default): `LateUpdate` la fa fluttuare sopra la mano e guardare la testa (comportamento storico).
- **Grabbing**: mentre tieni il grip della mano-pennello, `LateUpdate` applica alla palette la
  posa del controller-pennello (presa a una mano: offset pos/rot catturato all'aggancio).
- **Placed**: fissa nello spazio, orientamento mantenuto; `LateUpdate` non la tocca.

### Transizioni
- **Docked/Placed → Grabbing**: grip mano-pennello premuto **entro `GrabRange`** dai bordi
  (`BeginGrab`). All'aggancio fa `transform.SetParent(null, true)` → spazio mondo, così da Placed
  non segue più la mano.
- **Grabbing → Placed**: grip rilasciato (`grip <= GripRelease`). Resta dov'è.
- **Placed → Docked**: **trigger mano-palette** (`Redock`): `SetParent(HandAnchor)` e riprende il
  follow. Il trigger della mano-palette è **contestuale**: apre/chiude la palette quando è
  agganciata, la riaggancia quando è fissata.

## Indicatore di grab (affordance)

**Una sola striscia** bianca che scorre lungo il **contorno arrotondato** del pannello, centrata
su una finestra (`HlWindow`) attorno al punto più vicino al controller. Passando da lato ad angolo
**segue la curva**, quindi la transizione lato↔angolo è **continua/smooth** (niente salto tra forme
diverse, problema della versione precedente). Centrata sulla **linea del bordo** → non invade le
strisce laterali (sta nel gap) né i bottoni interni.

- `BuildPerimeter` campiona il contorno (4 lati + 4 archi di raggio `PanelCorner`) in un loop
  ordinato di (posizione, normale uscente, arc-length cumulativo).
- `UpdateHighlight` ogni frame trova il campione più vicino al controller (spazio locale del
  pannello, pre-scala) e ricostruisce la striscia (`RebuildRibbon`) per la finestra
  `[s0 − HlWindow/2, s0 + HlWindow/2]`, campionando il contorno con `SampleContour` (mesh dinamica,
  ~30 vertici, ricostruita ogni frame: costo trascurabile).
- Alpha pilotato dalla vicinanza: `t = InverseLerp(HighlightRange, GrabRange, dist)` → invisibile a
  `HighlightRange = 0.22`, pieno a `GrabRange = 0.09` e mentre si afferra; **1.0** agganciabile,
  0.65 in avvicinamento.

Materiale unlit trasparente a doppia faccia con `GlowTexture` **piena** (alpha 1 fino al 70% dello
spessore, sfuma solo a bordi/estremità → **solida e ben visibile**, non "solo trasparente").
Spessore `HlThick = 0.012`. `DistanceToPanel` usa `panelBgRenderer.bounds.ClosestPoint` (AABB del
fondo). Impulso aptico leggero entrando nel raggio di presa e su grab/rilascio/re-dock (`PulseBrush`).

> Verificato a render in editor (tool di debug temporaneo, poi rimosso) in 3 posizioni — angolo,
> lato, transizione — prima del test in visore.

## Interazione quando è fissata (poke + ray, entrambi i controller)

Da fissata nella stanza la palette si comanda anche col **secondo controller** (quello che la
teneva), sia in **poke** sia con il **ray**:
- **Ray**: `PaletteRay` è generalizzato con un `Role` (Brush/Palette), controller risolto
  dinamicamente (segue la mano dominante). Una seconda istanza sulla mano-palette
  (`Role = Palette`, `RequiresPlaced = true`) è attiva **solo quando la palette è fissata**
  (`PaletteController.Placed`); appesa alla mano resterebbe inattiva (niente pressioni accidentali).
  La mano-palette non disegna → niente `SuppressDrawing`.
- **Poke**: `PaletteButton.OnTriggerEnter` accetta la punta del pennello (`BrushTip`) **o** una
  sonda `PalettePoke` montata sulla mano-palette (sfera trigger + Rigidbody cinematico, in
  `DrawingRig`). Sempre attiva (con la palette appesa alla propria mano non la si tocca comunque).
- **Conflitto trigger mano-palette risolto**: da fissata il trigger della mano-palette serve sia a
  ri-agganciare sia a cliccare un controllo col ray. Il ray-palette pubblica
  `PaletteRay.PaletteHandOnPalette`; `Redock` scatta **solo se il ray non sta puntando la palette**
  (altrimenti il trigger clicca il controllo).

## Retro solido (no see-through)

Da fissata nella stanza ci si gira attorno: i pannelli sono quad a **faccia singola**, quindi da
dietro erano invisibili (si vedeva attraverso). Fix in `BrushMaterials.MakeOpaque`: i materiali
**opachi** della UI ora sono a **doppia faccia** (`_Cull = Off`). Le facce posteriori scrivono
comunque `alpha = 1`, quindi occludono il passthrough → la palette è solida da ogni lato. (Tocca
solo il percorso opaco: icone/segmento trasparenti restano a faccia singola.)

## Coordinamento col grab dei tratti

Il grip della mano-pennello serve già a **afferrare i tratti** (`GrabController`). Per evitare
conflitti, `PaletteController.SuppressBrushGrab` (statico) viene messo a `true` quando il
controller-pennello è entro `GrabRange` o sta trascinando la palette. Il `GrabController` della
**sola mano-pennello** lo legge a inizio `Update`: se è `true` e non sta già tenendo un tratto,
salta hover/presa dei tratti (così il grip muove la palette, non i tratti). Una presa di tratto
**già in corso** non viene interrotta. Resettato in `OnDestroy`.

## Solo su device

`UpdatePlacement` gira solo con `XRSettings.isDeviceActive` (in editor la palette resta
agganciata/pinned). In edit mode niente `Update`/grab (nessuno script è `ExecuteAlways`).

## File toccati

- `Palette/PaletteController.cs` — stato `PlaceMode` + `Placed` (statico); `UpdatePlacement`/
  `BeginGrab`/`Redock`/`DistanceToPanel`/`UpdateHighlight`/`RebuildRibbon`/`SampleContour`/
  `BuildPerimeter`/`HideHighlight`/`GlowTexture`/`PulseBrush`; `LateUpdate` per i tre stati; trigger
  mano-palette contestuale (re-dock solo se il ray non punta la palette); striscia `GrabHighlight`
  (mesh dinamica sul contorno) + `panelBgRenderer` in `BuildPanel`; `SuppressBrushGrab` (statico) +
  reset in `OnDestroy`.
- `Palette/PaletteRay.cs` — `Role` Brush/Palette, controller dinamico, `RequiresPlaced`,
  `PaletteHandOnPalette` (statico); origine/disegno disaccoppiati da `Brush`.
- `Palette/PaletteButton.cs` — poke accettato da `BrushTip` **o** `PalettePoke`.
- `Geometry/PalettePoke.cs` — **nuovo**: marker della sonda poke della mano-palette.
- `Geometry/GrabController.cs` — la mano-pennello salta la presa tratti se `SuppressBrushGrab`.
- `Geometry/BrushMaterials.cs` — `MakeOpaque` a doppia faccia (`_Cull = Off`): retro solido.
- `DrawingRig.cs` — sulla mano-palette: `PaletteRay` (Role Palette, RequiresPlaced) + sonda
  `PalettePokeTip` (sfera trigger + Rigidbody cinematico + `PalettePoke`).

## Parametri tarabili
`HighlightRange` (0.22), `GrabRange` (0.09), `GripPress`/`GripRelease` (0.55/0.35); striscia:
`HlThick`/`HlWindow`/`HlWindowSegs`/`PanelCorner` + alpha in `UpdateHighlight` + pienezza in
`GlowTexture`; durate `PulseBrush`.

## Verifica
Compilazione 0 errori; preview statici invariati (highlight inattivo a riposo → nessuna regressione
di layout). **Da provare nel visore** (a carico dell'utente): glow morbido su bordo/angolo vicino
(L sugli angoli, barra sui lati), bianco/trasparente/vicino; grip = trascina; rilascio = fissa;
trigger mano-palette nel vuoto = riaggancia, su un controllo = clicca; da fissata, poke **e** ray
con entrambi i controller; nessuna interferenza con la presa dei tratti lontano dalla palette.
Commit a carico dell'utente.
