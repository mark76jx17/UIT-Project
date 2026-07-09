# Gomma: cancella solo l'area coperta (dimensione dallo slider) anche sui disegni fusi — luglio 2026

> Fix del tool **Cancella** (Eraser): sui disegni collegati/fusi cancellava spesso l'**intera
> entità** invece della sola area coperta dalla punta. Ora: (1) il raggio della gomma **segue
> la dimensione scelta** (slider Size); (2) sui gruppi fusi si erode **solo la parte colpita**;
> (3) l'anteprima rossa evidenzia **solo ciò che verrà eroso**. **Elimina** (Delete) resta
> com'era: rosso sull'intero oggetto → rimuove tutto l'oggetto (annullabile).
>
> Un primo tentativo (2026-07-09 mattina) era stato ritirato: ricostruiva i gruppi in spazio
> mondo e sul visore "spostava/deformava". La causa vera era un bug preesistente di
> `EmitPiece` (vedi sotto), trovato e **verificato numericamente** con un tool diagnostico.
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificati | `Geometry/Stroke.cs`, `Geometry/BrushController.cs` |
> | File nuovi | `Drawing/Editor/EraseGroupDiagnostic.cs` (diagnostica, Editor-only) |

## Root cause n.1 — gruppo = cancellazione totale

`EraseAt` limitava la cancellazione parziale agli oggetti con **un solo** `Stroke`; un gruppo
fuso ne ha molti → ramo "cancella tutto".

## Root cause n.2 — il bug che deformava i pezzi (`EmitPiece`)

`Stroke.EmitPiece` ricostruiva i pezzi superstiti con:

```csharp
piece.transform.localScale = transform.localScale;   // ← BUG
```

I punti del pezzo sono nello spazio **locale** del tratto ma il pezzo nasce **senza genitore**:
serve la **lossyScale** (scala mondo). Con `localScale` un tratto **dentro un gruppo scalato**
(o figlio di una fusione con scala ≠ 1) veniva ricostruito **rimpicciolito e fuori posto** —
lo "si sposta/deforma" osservato sul visore. Su tratti isolati non scalati i due valori
coincidono, per questo il bug non si era mai visto prima dei gruppi.

## Cosa cambia

### 1. Raggio della gomma = dimensione scelta (`BrushController`)

Via il raggio fisso `eraseRadius = 0.03`; ora:

```csharp
float EraseRadius => StrokeSettings.FixedRadius + eraseMargin;  // margine default 0.006
```

Punta piccola = gomma di precisione (~9 mm), punta grande = gomma larga (~26 mm). La palette
già abilitava lo slider Size in modalità gomma: ora è **davvero collegato**.

### 2. `Stroke.TryEraseSphere` robusto

- **Fonte dati = `StrokeRecord`** (serializzato) invece dei campi runtime `rawPoints`
  (coincidono dopo `Finish`, ma il record sopravvive ai cloni `Instantiate`).
- **Garanzia di progresso** (`ensureProgress`): i collider di presa sono più larghi
  (≥ 22 mm) del raggio gomma minimo — si può "toccare" il tratto senza avere punti nella
  sfera. In quel caso si toglie almeno il **punto più vicino**: la gomma che tocca morde sempre.
- **Fix `lossyScale`** in `EmitPiece` (root cause n.2).

### 3. Gomma sui gruppi (`EraseAt` a tre rami + `Stroke.EraseNodeAndRebuild`)

`FindNearbyPart` individua, oltre alla radice (`DrawnItem`), la **parte** colpita: il
portatore di `StrokeRecord` più vicino al collider toccato (il singolo tratto/fill, anche
annidato); per gli anelli senza record si risale al figlio diretto della radice.

- **Oggetto semplice** (nessun sotto-oggetto fuso): comportamento storico — pezzi superstiti
  come oggetti **indipendenti** (la gomma "taglia in due").
- **Colpito un sotto-oggetto fuso** (caso comune): si nasconde **solo quel figlio**
  (`SetActive(false)`, resta nella gerarchia → undo perfetto) e i pezzi superstiti
  **rientrano nel gruppo** (figli della radice, senza `DrawnItem`).
  `PushReplace([figlio], pezzi)`. Il resto dell'entità **non viene toccato**.
- **Colpita la geometria della radice**: `EraseNodeAndRebuild` ricostruisce il nodo — pezzi
  superstiti del tratto-radice + **cloni fedeli** (`Object.Instantiate`, posa mondo
  conservata, figli inattivi potati) dei sotto-oggetti fusi — e sostituisce atomicamente:
  `PushReplace([vecchiaRadice], [nuovaRadice])`.

In tutti i casi un solo passo di undo; `Kill()` (Destroy/DestroyImmediate) rende la logica
eseguibile anche in Edit mode per la diagnostica.

### 4. Anteprima rossa solo in Elimina

`UpdateEraseHover`: il rosso resta **solo in Elimina** (lì indica correttamente "tutto
questo sparirà"). In **Cancella** l'anteprima rossa è stata **tolta del tutto** (feedback
utente 2026-07-09: sui gruppi, toccando la geometria della radice, tingeva l'intera
struttura — fuorviante per uno strumento che toglie solo l'area coperta). Lo spegnimento
del rosso residuo ora è legato a `!DeleteMode` (prima `!EraserMode`, che non ripuliva
passando da Elimina a Cancella).

## Verifica (evidenza, non solo compilazione)

**`Tools/Drawing/Test Group Erase`** (`EraseGroupDiagnostic`, Edit mode, eseguito via MCP):
costruisce due tratti fusi con posa (1,2,3), rotazione (20°,30°,10°) e **scala 1.5** — il
setup che stanava il bug lossyScale — ed erode figlio e radice verificando numericamente:

```
PASS — figlio: nessuno spostamento (dev. max 0,00000 m)
PASS — figlio: area erosa vuota (dist. min 0,04500 m)
PASS — figlio: radice intatta
PASS — radice: pezzi di A sulla polilinea / fuori dalla sfera
PASS — radice: clone di B fedele (posizione) / completo (n. punti)
[EraseDiag] TUTTI I TEST PASS
```

Compilazione: **0 errori**. Il tool resta nel progetto (Editor-only, escluso dalla build)
per regressioni future.

## Aggiornamento (2026-07-09, sera) — i pezzi STACCATI diventano indipendenti

Bug emerso al test in visore: la gomma su una parte fusa faceva rientrare **tutti** i pezzi
superstiti nel gruppo. Un pezzo *visivamente* staccato restava così *gerarchicamente* nel
blocco → **Elimina evidenziava/rimuoveva l'intera struttura** invece del solo pezzo (mentre
sui tratti singoli — es. una spirale tagliata — i pezzi erano indipendenti e Elimina li
prendeva uno a uno).

Fix: la permanenza nel blocco ora segue la **connettività fisica**, non la gerarchia. Dopo
l'erosione di una parte fusa, ogni pezzo superstite viene classificato (`AttachIfTouching` +
`TouchesStructure` in `BrushController`):

- **tocca ancora** il resto della struttura (un punto della sua polilinea entro ~5 mm dai
  collider di presa del gruppo) → rientra nel blocco (figlio della radice, senza `DrawnItem`);
- **rimasto staccato** → resta l'oggetto **indipendente** creato da `Rebuild` (col suo
  `DrawnItem`): Elimina e grab lo trattano da solo, come i pezzi di un tratto singolo.

Note tecniche:
- Il contatto si verifica **dai punti del pezzo verso i collider esistenti del gruppo** (non
  il contrario): i collider appena creati dal Rebuild entrano nel broadphase fisico solo al
  prossimo update e una query su di essi li mancherebbe.
- Un pezzo che tocca il gruppo solo **attraverso un altro pezzo appena creato** non viene
  concatenato (stesso motivo): caso raro, resta indipendente — al peggio si rifonde col grab.
- Il caso "geometria della **radice** colpita" ricostruisce ancora il gruppo come blocco
  unico (i pezzi della radice non vengono classificati): limite noto, da estendere se emerge
  nei test.
- Undo invariato: `PushReplace([parte], [tutti i pezzi])` — riattiva la parte originale e
  nasconde i pezzi, ovunque siano parentati.

Verifica: 0 errori; diagnostico `Tools/Drawing/Test Group Erase` rilanciato → TUTTI PASS
(nessuna regressione geometrica).

## Da provare sul visore (a carico dell'utente)

1. Slider Size piccolo/grande → l'area cancellata segue la dimensione.
2. Disegni fusi → la gomma toglie solo l'area coperta; il resto resta e resta **un blocco**.
3. Rosso solo sulla parte da erodere (Cancella); rosso su tutto + rimozione totale (Elimina).
4. Undo ripristina; gruppo spostato/ruotato/scalato → nessuna deformazione.
5. Parti non erodibili (riempimenti, punti, anelli) dentro un gruppo: la gomma toglie solo
   quella parte, non il gruppo.
