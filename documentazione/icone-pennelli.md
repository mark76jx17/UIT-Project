# Icone dei pennelli in stile Gravity Sketch + label (giugno 2026)

> Le icone dei tipi di tratto erano poco leggibili (un campione di tratto **dritto**
> orizzontale). Rifatte in stile Gravity Sketch: un breve **tratto curvo** (swoosh) che
> mostra com'è il tratto, con **label** testuale dentro il pulsante.

## Cosa cambia

- **Icone** (`BrushPreview.cs`): da campione dritto a **swoosh curvo** con senso di
  volume 3D:
  - **stroke** (Round) — tubo pieno e tondo, luce soft in alto.
  - **ribbon** — nastro piatto con **torsione** (la larghezza si stringe a metà curva,
    come se girasse di taglio) → si capisce che è piatto, non un tubo.
  - **dashed** — stesso tubo curvo **spezzato in trattini** lungo la curva.
  - (glow resta, anche se il pulsante è nascosto — vedi `modifiche-temporanee.md`.)
- **Label** (`PaletteController.BuildBrushStrip`): testo piccolo (`stroke`/`ribbon`/
  `dashed`) **in alto dentro** il pulsante, icona sotto. Pulsante ingrandito
  (`bSize 0.048 → 0.058`) per ospitare label + icona; font label `0.10` (non invadente).
- **Ordine** della barra: **naturale** → dall'alto verso il basso **stroke, ribbon,
  dashed**. Le label restano accoppiate alle icone (sono figlie del pulsante); gli
  indici delle array non cambiano (`SyncSelection` resta corretto).

## Come sono fatte le icone (BrushPreview)

Texture 128×72 generata a runtime. Una **curva centrale** (swoosh diagonale, estremi
addolciti con `sin`) campionata in 64 punti; per ogni pixel si usa la **distanza al
campione più vicino** → estremità arrotondate naturali. Da `(distanza, t)`:
- stroke: alpha entro `tubeR`, ombreggiatura più chiara in alto (volume);
- ribbon: semi-larghezza modulata da `|cos|` lungo la curva (torsione);
- dashed: alpha del tubo × pattern `Repeat(t*6)` (≈6 trattini);
- glow: core + alone gaussiani sulla distanza.

## File toccati

- `Palette/BrushPreview.cs` — riscritta `Get`: icone a tratto curvo (stroke/ribbon/dashed/glow).
- `Palette/PaletteController.cs` — `BuildBrushStrip`: pulsante più grande, label in alto
  (`BrushLabel(type)`: Round→"stroke"), icona sotto, ordine visivo invertito.

## Verifica

- Compilazione: **0 errori** (MCP refresh + console).
- Aspetto: verificato in editor con `Tools/Palette Preview/Build` + screenshot — le 3
  icone sono leggibili e distinte, label piccole in alto, ordine dal basso
  stroke/ribbon/dashed. Test finale in Play/visore a carico dell'utente.

## Parametri tarabili
- Icona: `tubeR`, `ribbonR`, `amp` (curvatura), numero trattini in `BrushPreview.cs`.
- Pulsante/label: `bSize`, `BrushLabelFont`, posizioni label/icona in `BuildBrushStrip`.
