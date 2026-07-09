# Fix: il tratto sfarfallava (colore a intermittenza) mentre si disegna — luglio 2026

> Bug segnalato dai test in visore: **mentre si traccia un tratto, la linea sembra
> "flickerare"** — il colore lampeggia tra normale e schiarito durante l'azione.
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificato | `Geometry/GrabController.cs` |

## Root cause

Non era la mesh (il `TubeMesher` è incrementale, con upload e bounds corretti): era
**l'hover del grab che agganciava a intermittenza il tratto in corso**.

Catena esatta:

1. Il tratto in costruzione ha già il suo `DrawnItem` e **semina collider di presa** man
   mano che cresce (`Stroke.AddPoint`, un collider ogni 8 campioni, raggio ≥ 22 mm).
2. `GrabController.Update` sulla mano-pennello gira **anche mentre si disegna** e fa
   l'hover con la **sonda sulla punta** (`tipProbe` — la stessa punta che sta disegnando,
   introdotta a giugno per la selezione precisa, vedi `grab-tip-e-selezione.md`).
3. Quando la sonda tocca un collider del tratto in corso → `StrokeHighlight.Set(1.2×)`
   → il tratto **si schiarisce**; la punta avanza oltre l'ultimo collider (restano
   indietro fino a ~8 campioni) → hover spento → **colore normale**; nuovo collider
   sotto la punta → di nuovo schiarito…

Risultato: colore del tratto che lampeggia per tutta la durata del gesto. Lo stesso
meccanismo schiariva a sprazzi anche i tratti *esistenti* sfiorati mentre si disegna.

## Fix (minimo, alla radice)

Mentre il pennello sta disegnando (`BrushController.IsDrawing`), la mano-pennello
**sospende hover e presa** — stesso pattern già usato per la palette e per il foglio a
quadretti (`SuppressBrushGrab`):

```csharp
if (controller == StrokeSettings.BrushHand
    && (PaletteController.SuppressBrushGrab || ReferenceGrid.SuppressBrushGrab
        || (brush != null && brush.IsDrawing))
    && holding == null)
{
    SetHover(null);
    return;
}
```

`brush` è il `BrushController` sullo stesso GameObject della mano-pennello (preso in
`Awake`; null sulla mano-palette, che non disegna). Una **presa già in corso non viene
interrotta** (`holding == null`), come per gli altri suppress.

Effetti collaterali voluti: mentre si traccia un tratto il grip non aggancia più nulla
(durante il gesto di disegno non ha senso afferrare) e i tratti esistenti sfiorati non
si illuminano più a sprazzi.

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Test in Play/visore a carico dell'utente: disegnare tratti lenti e veloci → il colore
  della linea resta stabile per tutta la durata del gesto; a trigger rilasciato l'hover
  di presa (schiarimento avvicinando il controller) torna a funzionare normalmente;
  presa e magnete invariati fuori dal disegno.
