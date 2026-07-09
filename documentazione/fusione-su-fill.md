# Fusione (magnete afferrando) anche sulla parte riempita di un disegno — luglio 2026

> Estensione del **magnete post-disegno** (fusione afferrando, vedi la logica generale in
> `undo-redo-trasformazioni.md` §"Undo dell'unione magnete"): finora, tenendo un disegno col
> grip, ci si fondeva a un altro solo **toccandone il contorno** (il tratto, che ha i
> collider). Ora ci si può fondere portando la punta anche **sopra l'area riempita** (fill)
> di un altro disegno.
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificato | `Geometry/GrabController.cs` (solo `FindMergeCandidate`) |

## Perché serviva

La superficie di riempimento **non ha collider** — rimosso di proposito (`FillSurface.cs`:
un MeshCollider convesso su forme concave coprirebbe l'inviluppo, rovinando presa e gomma; i
trigger non-convessi non sono supportati da Unity). Di conseguenza l'`OverlapSphere` del
magnete non "vedeva" la campitura: per fondere due disegni bisognava andare a centrare il
**bordo** sottile del contorno, scomodo.

## Cosa cambia (comportamento)

Tenendo il disegno A col grip e portandone la punta **sopra l'area colorata** di B:

1. B si illumina col colore "magnete" (contorno **e** riempimento insieme) + tick aptico
   "qui si aggancia" — identico feedback della fusione sul contorno.
2. Al rilascio del grip, A **resta dov'è** (posa nel mondo invariata) e diventa parte di B.
   Da lì il gruppo si afferra/sposta/ruota/scala come un unico oggetto da qualsiasi punto,
   bordo o campitura.
3. **Undo** separa A da B; **Redo** li rifonde. (Riusa `PushMerge`, invariato.)

Dettagli che ne conseguono:
- **Sopra un buco** (fill a ciambella) **non** si aggancia: `PointInFill` esclude i buchi.
- **Precedenza al contorno**: la ricerca a collider gira per prima; la campitura è un
  fallback (trasparente per l'utente, ci si aggancia comunque a B).
- Vale sia per fill **figli** di un contorno (bersaglio = il disegno-contorno) sia per fill
  **indipendenti** (bersaglio = il fill stesso).
- **Niente aggancio da lontano**: serve la prossimità alla superficie (vedi guardia sotto).

## Implementazione

Solo `GrabController.FindMergeCandidate()`, rifattorizzato:

- Le esclusioni (non l'oggetto tenuto, non imparentato con esso) sono estratte nell'helper
  `IsMergeable(root)`, condiviso dai due rami.
- Ramo 1 invariato: `OverlapSphere` sui collider dei tratti → primo `DrawnItem` valido.
- Ramo 2 nuovo (`FillOwnerAt`): se il ramo 1 non trova nulla, si scorrono i riempimenti la
  cui **area coperta** contiene la punta (`FillRegion.CoveringFills(probe, holding.gameObject)`,
  già esistente: gestisce piano e buchi) e si risale al `DrawnItem` proprietario
  (`GetComponentInParent<DrawnItem>()`). Bersaglio = quel disegno.
- **Guardia di prossimità**: si scarta il fill se la punta è oltre `mergeRadius` dai suoi
  `Renderer.bounds` (`bounds.SqrDistance(probe)`), così non ci si aggancia a una campitura
  lontana ma complanare.

Tutto il resto del magnete (evidenziazione `SetMergeHover`, rilascio con `SetParent(target,
true)` + distruzione del `DrawnItem` del figlio, `PushMerge` per l'undo) è **invariato**:
la fusione su un fill percorre esattamente lo stesso codice della fusione su un contorno.

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Test in Play/visore a carico dell'utente: tenere un disegno e portarlo sopra l'area colorata
  di un altro → glow su tutto il bersaglio + aptico; rilascio → gruppo unico, l'oggetto resta
  dov'era; Undo separa. Sopra un buco: nessun aggancio. Nessuna regressione sulla fusione sul
  contorno. Da tarare l'eventuale sensazione di prossimità (`mergeRadius`, oggi 0.06 m).
