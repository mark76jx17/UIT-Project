# Undo/Redo di spostamento, rotazione e scala (e unione magnete) — luglio 2026

> La cronologia undo/redo (`StrokeHistory`) è stata estesa e corretta per coprire anche le
> **manipolazioni con la presa a due mani** (spostamento, rotazione con roll, scala/stretch)
> e l'**unione magnete**, non più solo disegno e cancellazione. In più è stato invertito lo
> scambio delle **icone** Undo/Redo che erano rovesciate.
>
> | Data | 2026-07-04 |
> |---|---|
> | Commit | `5128562` (undo su rotazione/stretch/traslazione), `f00c910` (fix undo-redo), `d094658` (inverti icone) |
> | File | `Geometry/StrokeHistory.cs`, `Geometry/GrabController.cs`, `Palette/PaletteController.cs` |

## Il modello: la history è uno stack di AZIONI

`StrokeHistory` non tiene tratti ma **azioni**, ognuna reversibile in avanti (redo/esegui) e
all'indietro (undo). I tipi di azione:

| Push | Cosa registra | Undo | Redo |
|---|---|---|---|
| `PushGroup` / `Push` | disegno (uno o più oggetti mostrati) | nasconde gli `added` | li rimostra |
| `PushErase` | cancellazione (oggetti già nascosti dal chiamante) | rimostra i `removed` | li rinasconde |
| `PushReplace` | gomma parziale / ricolora fill (rimossi + pezzi nuovi) | rimostra i vecchi, nasconde i nuovi | viceversa |
| `PushMerge` | **unione magnete** di un oggetto sotto un altro | separa (child → oldParent, ripristina il `DrawnItem`) | riunisce (child → newParent, toglie il `DrawnItem`) |
| `PushTransform` | **spostamento/rotazione/scala** di un oggetto afferrato | posa "prima" | posa "dopo" |

Gli oggetti **non si distruggono** finché restano annullabili (undo/redo = semplice
`SetActive`); vengono liberati solo quando l'azione esce dal tetto `MaxUndo` (200) o su `Clear`.

## Undo delle trasformazioni (il fix principale)

Il problema: **spostare/ruotare/scalare** un oggetto con la presa a due mani non era
annullabile (la history non registrava le pose). Risolto in `GrabController`/`GrabSession`:

- **All'inizio della presa** (nascita della sessione, prima mano) `CaptureGrabStart()`
  fotografa la posa **pre-presa** (posizione + rotazione in mondo, scala locale).
- **Al rilascio finale** (ultima mano che lascia) `PushTransformIfMoved()` confronta la posa
  attuale con quella catturata e, se è cambiata oltre una piccola soglia anti-jitter
  (0.1 mm / 0.1° / scala), chiama `StrokeHistory.PushTransform(target, posBefore, rotBefore,
  scaleBefore)`. La posa "dopo" è quella corrente del target.
- Il passaggio **due mani → una mano** (`Recapture`) **non** riaggiorna `grabStart`: così
  l'undo torna sempre alla posa **prima dell'intera presa**, non a uno stato intermedio.
- Un semplice **afferra-e-rilascia senza spostamento non consuma** un passo di undo.

`ApplyPose` (in `StrokeHistory`) riapplica la posa "prima" (undo) o "dopo" (redo) con
`SetPositionAndRotation` + `localScale`; ritorna `false` se il target è stato distrutto (l'azione
viene saltata, non blocca la catena).

> Le pose sono in spazio **mondo** (posizione + rotazione), robuste a un'eventuale unione
> magnete annullata prima di questa azione; la scala è locale.

## Undo dell'unione magnete

Al rilascio, se un candidato all'unione è evidenziato, `GrabController.ReleaseHolding` fa il
reparenting (`child.SetParent(mergeTarget)`, il `DrawnItem` del figlio viene distrutto perché
ora il "vero" oggetto è la radice del gruppo) e registra `PushMerge(child, oldParent,
mergeTarget)`. `ApplyMerge` inverte/riapplica: undo separa e **ripristina il `DrawnItem`**,
redo riunisce e lo **ritoglie**. Se il bersaglio dell'unione è stato distrutto, il redo non
si può rifare (`return false`).

## Robustezza della catena undo/redo

`Undo()`/`Redo()` **scorrono** lo stack saltando le azioni che non agiscono più (target
distrutto): `ApplyReverse`/`ApplyForward` combinano `SetActive` + `ApplyMerge` + `ApplyPose`
con un OR **non short-circuit** (`|`) e ritornano `true` se **qualcosa** ha agito; se un'azione
non tocca più nulla si passa alla successiva senza consumare l'input dell'utente. Push di una
nuova azione **azzera il ramo redo** (`ClearRedo`, che distrugge gli oggetti `added` orfani dei
redo scartati, lasciando in scena i `removed` che sono arte visibile).

## Icone Undo/Redo invertite

Le icone dei due pulsanti in palette erano **scambiate** (la freccia "indietro" faceva redo e
viceversa). Corretto l'accoppiamento icona→azione nella striscia azioni (`PaletteController`).

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Test in Play/visore a carico dell'utente: sposta/ruota (con roll)/scala un oggetto → **Undo**
  torna alla posa iniziale, **Redo** la riapplica; presa a due mani poi una sola → Undo torna
  al pre-presa; unione magnete → Undo separa; freccia "indietro" = Undo, "avanti" = Redo.
