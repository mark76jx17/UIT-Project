# Fill a secchiello: flood-fill raster + unione magnete + fill "col pennello" — luglio 2026

> Il riempimento (`ToolMode.Fill`) è stato riscritto attorno a un **flood-fill raster**
> stile secchiello dei programmi di disegno (e trapped-ball dell'animazione): si punta
> **dentro** un'area chiusa da delle linee e si riempie, indipendentemente da come i tratti
> sono raggruppati. Il fill inoltre **copia lo stile** (tipo pennello + spessore)
> dell'oggetto che delimita l'area ed è annullabile.
>
> | Data | 2026-07-02 → 2026-07-05 |
> |---|---|
> | Commit | `87b4e6d`, `7082a73`, `65d16ed` (+ `d0b2f76` WIP giugno) |
> | File nuovi | `Geometry/FillRegion.cs` |
> | File modificati | `Geometry/FillSurface.cs`, `Geometry/Stroke.cs`, `Geometry/BrushController.cs` |

## Perché

Il fill precedente (vedi `modifiche-disegno-giugno2026.md` §3) sapeva chiudere solo:
1. un **singolo tratto** i cui estremi erano vicini (`Stroke.FillWith`);
2. un **anello di più tratti** uniti col magnete (`Stroke.FillGroup`).

Non copriva il caso più naturale — *"ho disegnato dei muri che racchiudono uno spazio,
voglio colorare quello spazio"* — quando i muri sono **tratti separati** che non formano un
anello ordinato. Serviva un metodo che ragionasse sull'**area** e non sulla topologia dei
tratti.

## Come funziona il secchiello (`FillAt` in `BrushController`)

Il Fill è un'operazione **one-shot**: al superamento della soglia trigger scatta una volta
(`fillConsumed`), non traccia. La logica prova gli approcci dal più specifico al più generale:

1. **Tratto chiuso singolo** → `Stroke.FillWith`.
2. **Anello di gruppo** (più tratti uniti) → `Stroke.FillGroup`.
3. **Regione raster** (il nuovo secchiello) → `FillRegion.FillCellAt`.

Esiste anche una modalità **"col pennello"** (`contourRingFill`): invece di una superficie
piatta, riempie con **anelli concentrici** che seguono il contorno verso l'interno,
disegnati con lo stesso pennello dell'oggetto (`FillRegion.FindRings` + `Stroke.MakeRingFill`).

**Ricolora-in-posto:** se nel punto puntato c'era già un riempimento
(`FillRegion.CoveringFills`), il nuovo lo **sostituisce** invece di sovrapporsi (niente
z-fighting, un solo passo di undo via `PushReplace`).

## Il flood-fill raster (`FillRegion.cs`, nuovo)

Cuore della feature. `BuildMask(seed, searchRadius)`:

1. **Raccoglie i tratti vicini** al punto (`GatherRecords`, `OverlapSphereNonAlloc`, esclusi
   punti-sfera e riempimenti).
2. **Piano di best-fit** attorno al seed (somma delle normali dei triangoli seed–a–b) e base
   2D (u, v): i tratti 3D vengono proiettati su questo piano.
3. **Rasterizza i tratti** come **muri sottili (1 cella)** su una griglia (~400 celle sul lato
   lungo, tetto 640). Muri sottili di proposito: il flood 4-connesso si ferma sulla **linea
   mediana** del tratto e ne conserva gli **angoli netti** (niente più dilatazione/opening
   morfologico che li arrotondava; il tratto visibile copre poi il bordo del fill).
4. **Chiusura fessure** su due livelli:
   - **gap-extend** — le estremità libere di una linea aperta (lunga almeno `OpenSpanMin`)
     vengono **allungate** di `GapExtend` (1.5 cm) per raggiungere il contorno;
   - **bridging** — ogni estremo libero è **unito con un segmento sottile** all'estremo libero
     più vicino di un **altro** tratto entro `BridgeMax` (2 cm): una cornice fatta di tratti
     staccati resta stagna, senza ingrossare i muri (che "mangerebbero" le celle sottili).
5. **Flood** 4-connesso dal seed. Se l'allagamento **tocca il bordo** della griglia → area
   **aperta** → `null` (niente fill spuri tra oggetti che non racchiudono nulla). Se resta
   **racchiuso** → area chiusa.

Da qui divergono due usi della maschera:

- **`FindRegion`** (fill piatto): estrae i **contorni** dell'area allagata (`ContourLoops`,
  marching sui bordi tra celle dentro/fuori), li semplifica (**Douglas-Peucker ciclico**,
  `SimplifyEps`), individua **l'esterno** (area massima) e i **buchi** (loop contenuti
  nell'esterno) e li riporta nel mondo. La mesh la costruisce `FillSurface.BuildWithHoles`.
- **`FindRings`** (fill "col pennello"): calcola la **distance transform** (due passate
  chamfer) dell'area e ne estrae **iso-contorni** a passo = diametro del pennello → gli anelli
  concentrici.

### Aggancio all'oggetto (owner)

Ogni cella-muro annota il **tratto proprietario** (`owner`). Dopo il flood si guardano solo i
tratti i cui muri **toccano** l'area allagata (non tutti quelli entro il raggio di ricerca):
se a delimitarla è **un solo** `DrawnItem`, il fill entra nella sua gerarchia (`root`) e **si
muove/salva con lui**; se sono più oggetti, il fill resta **indipendente** (afferrabile a sé).

### Copia dello stile

`NearestBrush` prende **tipo pennello + raggio** del tratto più vicino al seed: il
riempimento "col pennello" e gli anelli assumono lo stile dell'oggetto riempito
(`Region.brushType`/`radius`).

## `FillSurface.cs` — triangolazione

- **`Build`** (contorno pieno): piano di Newell → proiezione 2D → **split degli anelli
  auto-intersecanti** nei loro anelli semplici (ogni "8"/∞/lobo viene riempito) → **ear
  clipping** per anello → mesh **a due facce** coi punti 3D originali (tollera contorni non
  perfettamente piani).
- **`BuildWithHoles`** (contorno + buchi): riempimento a **scanline con regola even-odd** su
  esterno + buchi → i buchi (ciambella, lettera "O", buchi annidati) restano vuoti
  automaticamente, senza "ponti" fragili. Niente più fallback a pieno con più buchi.
- **Niente collider sul fill**: un MeshCollider convesso su forme concave coprirebbe
  l'inviluppo; la forma si afferra/cancella dal **contorno** (il tratto, che ha già i collider).

## Anteprima e hover

Mentre il Fill è attivo: `UpdateFillHover` evidenzia il tratto chiuso/riempibile sotto la
punta; `UpdateFillPreview` mostra un'anteprima semitrasparente della cella che si riempirebbe.

## Parametri tarabili

- `FillRegion`: `GridTarget` (400), `MaxDim` (640), `SimplifyEps` (1 cella), `GapExtend`
  (0.015 m), `OpenSpanMin` (0.03 m), `BridgeMax` (0.02 m), `MaxRings` (60).
- `BrushController`: `regionSearchRadius`, `fillCloseThreshold`, `contourRingFill` (toggle
  fill piatto vs "col pennello").

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Test in Play/visore a carico dell'utente: riempire un'area chiusa da tratti **separati**;
  ciambella/lettera "O" (buchi vuoti); forma a "8" (entrambi i lobi); ricolora-in-posto;
  aggancio del fill all'oggetto quando l'area è delimitata da uno solo; area aperta = nessun
  fill. Da tarare `GapExtend`/`BridgeMax` se restano fessure o se salda troppo.
