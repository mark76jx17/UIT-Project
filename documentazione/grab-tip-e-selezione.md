# Allineamento brush tip + selezione grab con la punta (giugno 2026)

> Due ritocchi all'interazione con la mano del pennello:
> 1. la **punta** (brush tip) era percepita spostata a destra rispetto al controller;
> 2. la **selezione/grab** dei disegni usava solo l'area del controller — ora usa
>    **anche la punta**, per selezioni più precise.

## 1. Allineamento del brush tip

La punta, il cursore e il punto di disegno sono tutti posizionati a `tipOffset`
(campo di `BrushController`) rispetto all'anchor della mano. Era `(0, -0.01, 0.08)`:
x = 0 → centrato sull'asse del controller, ma all'uso risultava un po' a destra.

- **Cosa:** spostato il default a `(-0.012, -0.01, 0.08)` (≈ 1,2 cm a sinistra).
- **Nota:** muovendo `tipOffset` si spostano insieme punta, cursore, punto di disegno
  e zona di poke della palette → restano coerenti.
- **Taratura fine (consigliata):** in **Play**, seleziona il GameObject runtime
  **`Brush`** (sotto l'anchor della mano nel rig) e regola `Tip Offset` X
  nell'Inspector **dal vivo** finché la punta è centrata: i campi `[SerializeField]`
  sono modificabili durante il Play, nessun rebuild. Riporta poi qui il valore buono.

## 2. Selezione grab anche con la punta

Prima `GrabController.UpdateHover` faceva un solo `OverlapSphere` alla **posizione del
controller** (`grabRadius = 0.04`). Per oggetti piccoli/vicini era poco preciso.

- **Cosa:** aggiunta una **seconda sonda** sulla **punta del pennello**, più piccola
  (`tipProbeRadius = 0.018`). Ordine di prova:
  1. **punta** (sonda piccola, precisa) — per puntare un tratto sottile;
  2. se non aggancia nulla → **area del controller** (`grabRadius`), come da sempre.
- **Solo mano pennello:** il `DrawingRig` passa la punta (`brush.Tip`) **solo** al
  `GrabController` della mano del pennello. La mano palette resta con la sola area del
  controller (`TipProbe` null).
- Non cambia il **movimento** dell'afferrato: la presa segue comunque la mano
  (`GrabSession`). La punta serve solo a **scegliere** l'oggetto.

## File toccati

- `Geometry/BrushController.cs` — default di `tipOffset` X: `0 → -0.012`.
- `Geometry/GrabController.cs` — campo `tipProbe` + `TipProbe` (setter) e
  `tipProbeRadius`; `UpdateHover` rifattorizzato in `ProbeAt(pos, raggio)` con doppia
  sonda (punta poi controller).
- `DrawingRig.cs` — collega `brushGrab.TipProbe = brush.Tip` sulla mano del pennello.

## Da verificare in Play
- Punta centrata sul controller (tarare `tipOffset` X live se serve).
- Selezione: puntando un tratto sottile con la **punta** lo si aggancia con precisione;
  avvicinando l'area del controller funziona come prima.
- Compilazione verificata (MCP refresh + console): **0 errori**.
