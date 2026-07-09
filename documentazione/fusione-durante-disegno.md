# Fusione all'intersezione: il tratto che attraversa una figura forma un blocco unico — luglio 2026

> Estensione dello **snap-merge durante il disegno** (magnete a inizio tratto, vedi la logica
> generale in `undo-redo-trasformazioni.md`): finora la fusione col disegno vicino veniva
> decisa **solo alla partenza** del tratto (se *iniziavi* vicino a una figura). Ora la fusione
> scatta anche se, **mentre disegni**, la linea **attraversa** una figura — il suo contorno o
> la sua area riempita. A fine tratto i due diventano un **blocco unico**.
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificati | `Geometry/BrushController.cs`, `Geometry/FillRegion.cs` |

## Comportamento

Disegnando un tratto (anche partendo dal vuoto), la **prima figura che la linea interseca**
diventa il bersaglio della fusione:

- **Contorno** — la punta passa entro `snapRadius` da una linea disegnata di un'altra figura.
- **Area riempita** — la punta passa sopra la campitura di un riempimento (anche senza
  toccarne il bordo).

Al primo aggancio: **tick aptico** di conferma (lo stesso dello snap-merge di partenza). Al
rilascio, il tratto entra nella gerarchia della figura e ne perde il `DrawnItem` → da lì il
gruppo si afferra/sposta/ruota/scala come **un solo oggetto**. L'Undo nasconde il tratto
disegnato (come già per lo snap-merge di partenza).

**Regole decise in fase di design:**
- **Prima figura toccata vince**: appena si aggancia, smette di cercare (le figure attraversate
  dopo restano separate).
- **Sopra un buco** (fill a ciambella) non si aggancia: `PointInFill` esclude i buchi.
- **Nessuno spostamento della geometria**: attraversare non deforma né salda i vertici —
  registra solo il bersaglio; la fusione è il reparenting a fine tratto.

## Implementazione

### `BrushController`

- **`BeginPress`** — dopo l'eventuale snap-merge di partenza, cattura **una volta** uno
  snapshot dei riempimenti in scena (`fillSnapshot = FillRegion.ActiveFills()`), solo se il
  magnete è attivo e non ci si è già agganciati. I fill non cambiano durante un tratto, quindi
  basta uno snapshot: si evita un `FindObjectsByType` per campione (costo su Quest).
- **`ContinuePress`** — finché `mergeTarget == null` e il magnete è attivo, ogni campione
  chiama `DetectMergeWhileDrawing(position)`. Vale anche in modalità Line (linea elastica).
- **`DetectMergeWhileDrawing`** — prova:
  1. **Contorno**: `OverlapSphereNonAlloc` su `snapRadius`, primo `DrawnItem` valido;
  2. **Area riempita**: scorre `fillSnapshot`, `FillRegion.Covers(fill, position)` (point-in-fill
     cachato), risale al `DrawnItem` proprietario.
  Il **tratto in corso e il gemello speculare sono esclusi** (`IsCurrentStroke`): man mano che
  li disegno seminano già i loro collider di presa (`Stroke.AddPoint`), quindi senza esclusione
  il tratto si aggancerebbe a se stesso. Al primo colpo: `mergeTarget = figura` + impulso aptico.
- **`EndPress`** — **invariato**: se `mergeTarget` è impostato (da partenza o da intersezione),
  il tratto finito fa `SetParent(mergeTarget, true)` e perde il `DrawnItem`.

### `FillRegion` (due helper esposti, nessuna logica nuova)

- `ActiveFills()` — i `StrokeRecord` con `isFill` attivi in scena (lo snapshot).
- `Covers(fill, point)` — wrapper pubblico del `PointInFill` privato già esistente
  (point-in-poly sul piano del fill, buchi esclusi).

## Costo

- Contorno: un `OverlapSphere` NonAlloc per campione (economico, già nell'idioma del file).
- Fill: point-in-poly su una lista **cachata** a inizio tratto (niente `FindObjectsByType` per
  campione).
- Il rilevamento **si spegne** appena ci si aggancia (`mergeTarget != null`): si paga solo
  mentre disegni nel vuoto prima di incrociare qualcosa.

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Test in Play/visore a carico dell'utente: disegna una linea che parte dal vuoto e attraversa
  una figura (linee o area colorata) → tick aptico all'aggancio, e al rilascio linea+figura si
  muovono come un solo blocco; Undo separa; sopra un buco non aggancia; la fusione a inizio
  tratto (partendo su una figura) continua a funzionare come prima; nessun aggancio a se stesso.
  Se serve, tarare `snapRadius` (sensibilità dell'intersezione).
