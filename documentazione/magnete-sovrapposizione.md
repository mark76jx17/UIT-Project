# Magnete di precisione: fusione col grab anche per sovrapposizione delle strutture — luglio 2026

> Miglioria al **magnete afferrando** (fusione post-disegno, vedi `fusione-su-fill.md` e
> `undo-redo-trasformazioni.md`): prima il bersaglio dell'unione veniva rilevato solo con
> una **sonda attorno alla mano/punta** (`mergeRadius` = 6 cm) — tenendo un oggetto grande,
> se il suo *bordo* toccava un altro disegno lontano dalla mano non scattava nulla. Ora
> basta che le due strutture **si sovrappongano anche di poco**, ovunque: il bersaglio si
> evidenzia (viola) e il **rilascio del grip conferma** l'unione.
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificato | `Geometry/GrabController.cs` |

## Comportamento

Mentre tieni un disegno col grip:

1. **Sonda mano/punta** (esistente, immediata): un disegno entro 6 cm dalla mano è candidato.
2. **NUOVO — sovrapposizione strutture**: se una qualsiasi parte dell'oggetto tenuto tocca
   (anche appena, tolleranza +4 mm) una parte di un altro disegno, quel disegno diventa il
   candidato — indipendentemente da dove sta la mano.

In entrambi i casi: il candidato si illumina **viola** ("qui si aggancia") + tick aptico;
**rilasciando il grip i due si fondono** in un blocco unico (annullabile con Undo, invariato).

## Implementazione

Tutta in `GrabController`:

- **`BeginOverlapScan`** (alla presa): fotografa i **collider di presa** dell'oggetto tenuto
  (`GetComponentsInChildren<Collider>` — non cambiano durante la presa).
- **`ScanHeldOverlap`** (ogni frame, come fallback della sonda in `UpdateMergeCandidate`):
  esamina i collider dell'oggetto tenuto **a lotti round-robin** (`ScanPerFrame = 24` per
  frame → **costo costante** anche su disegni con centinaia di collider). Per ogni collider:
  `OverlapSphereNonAlloc` sul suo bounds (+ `OverlapMargin` 4 mm) cercando collider il cui
  `GrabRoot` sia un **altro** `DrawnItem` valido (`IsMergeable`: non il tenuto, non
  imparentato).
- **Isteresi anti-sfarfallio**: il bersaglio si **aggancia subito** al primo contatto, ma si
  **sgancia solo quando un intero ciclo di scansione** non trova più contatti (con la
  scansione a lotti, un singolo frame "senza contatto" non basta a far lampeggiare il viola).
- **`EndOverlapScan`** (al rilascio): pulizia dello stato.

Il resto del flusso — evidenziazione `SetMergeHover`, priorità della sonda diretta, unione
al rilascio con `PushMerge` (undo) — è invariato.

## Limiti noti

- La sovrapposizione è rilevata sui **collider di presa** (sfere ≥ 22 mm lungo i tratti, un
  po' più larghe del tratto visivo): è la stessa granularità di presa/gomma, adatta alla
  "piccola sovrapposizione" percepita.
- I collider dell'oggetto tenuto sono fotografati **alla presa**: se durante la presa l'altra
  mano disegna e fonde qualcosa nell'oggetto tenuto, i nuovi collider entrano nella scansione
  solo alla presa successiva (caso raro, nessun malfunzionamento).
- Con oggetti molto ricchi lo sgancio del viola può tardare di qualche frame (durata di un
  ciclo di scansione): voluto, è l'isteresi.

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Test in Play/visore a carico dell'utente: afferra un disegno grande e avvicinane il **bordo
  lontano dalla mano** a un altro disegno → appena si toccano, l'altro si illumina di viola;
  allontana → il viola si spegne (senza lampeggi); rilascia in sovrapposizione → fusione in
  blocco unico; Undo separa. Verificare anche che la sonda ravvicinata (mano vicino al
  bersaglio) funzioni come prima e che il frame-rate resti stabile tenendo disegni ricchi.
