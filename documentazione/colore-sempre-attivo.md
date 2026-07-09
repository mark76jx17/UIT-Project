# Zona colore e Pressure sempre attivi + riattivazione fluida del pennello — luglio 2026

> Feedback dai test con utenti: l'**overlay di disattivazione** (la velatura scura sui
> controlli non pertinenti allo strumento attivo, vedi `tool-delete-e-disable-tasti.md`)
> **confondeva** quando copriva la zona colore e Pressure. Ora l'overlay resta **solo su
> Mirror / Grid / Line** (e sulla striscia pennelli e Size, non citate dal feedback);
> la **zona colore** (ruota, luminosità, recenti, opacità) e il toggle **Pressure** sono
> **sempre attivi**. Al posto del blocco: scegliere un colore mentre si è in Gomma/Elimina
> **riattiva il pennello** in modo fluido.
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificati | `Palette/PaletteController.cs`, `StrokeSettings.cs` |

## Comportamento

- **Mirror / Grid / Line**: come prima — in Fill/Erase/Delete si velano e non rispondono
  (sono guide del disegno, con un altro strumento non hanno senso).
- **Zona colore e Pressure**: sempre toccabili, nessuna velatura, in qualsiasi strumento.
- **Riattivazione del pennello**: se lo strumento è **Gomma o Elimina** e l'utente sceglie un
  colore — ruota, slider luminosità, swatch recenti, o contagocce — lo strumento torna
  **Draw** automaticamente. Toccare il colore esprime l'intenzione di disegnare: non serve
  più premere prima il pulsante Draw.
- In **Fill** non si cambia strumento: lì il colore serve al secchiello.

### Perché è "fluida"

Il cambio passa da `StrokeSettings.Tool`, che tutta la UI già osserva per sincronizzarsi
solo-al-cambio: il pulsante **Draw si illumina** (`SyncSelection`), le velature rimaste
(Mirror/Grid/Line, pennelli, Size) **si riattivano** (`SyncToolControlAvailability`),
l'**icona sulla punta del pennello** passa alla matita (`BrushController.lastIconTool`).
Nessun pop-up, nessun passaggio manuale.

## Implementazione

### `StrokeSettings` (il cuore, un punto solo)

`SetHSV` (ruota + luminosità) e `SetColor` (recenti + contagocce) chiamano il nuovo
`ReactivatePen()`:

```csharp
static void ReactivatePen()
{
    if (Tool == ToolMode.Eraser || Tool == ToolMode.Delete)
        Tool = ToolMode.Pen;
}
```

Centralizzato qui copre **tutte** le vie di input senza toccarle: poke, ray della palette,
simulatore desktop, contagocce.

### `PaletteController` (rimozione del blocco)

- Rimossi `colorControls`/`colorDim` (lista, overlay, registrazioni dei 5 controlli colore,
  blocco in `SyncToolControlAvailability`): la zona colore non viene mai disabilitata.
- Rimossi `pressureDim` e la registrazione di Pressure tra i controlli draw-only.
- `guidesDim` (Mirror/Grid/Line), `brushDim` (striscia pennelli) e `sizeDim` (Size, attivo
  per Pen+Eraser) restano come prima.

## Verifica

- Compilazione: **0 errori**; `Tools/Drawing/Preview All Panels` eseguito (via MCP) senza
  eccezioni — 6 PNG generati, palette ispezionata: layout integro (ruota, Pressure, Size,
  Mirror/Grid/Line, Draw/Fill/Erase/Delete, strisce laterali).
- Test in Play/visore a carico dell'utente: in Gomma/Elimina la zona colore NON si vela;
  toccare ruota/recenti riporta su Draw (pulsante illuminato, icona punta a matita, guide
  riattivate); in Fill scegliere il colore NON cambia strumento; contagocce in Gomma →
  torna Draw col colore prelevato; Mirror/Grid/Line ancora velati fuori da Draw.
