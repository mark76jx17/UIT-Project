# Anteprima colore in Cancella/Elimina + disattivazione senza banner sovrapposto — luglio 2026

> Due rifiniture UX richieste dopo i test con utenti:
> 1. il **checker/anteprima del colore** accanto alla ruota ora, in **Cancella/Elimina**,
>    diventa un **grigio trasparente con l'icona dello strumento** (gomma o ✕, le stesse dei
>    bottoni) — prima continuava a mostrare il colore corrente, fuorviante;
> 2. i controlli disattivati non vengono più coperti dal **pannello scuro sovrapposto**
>    ("banner" che sembrava appoggiato male sui bordi): ora si **modifica direttamente il
>    colore dei bottoni** (materiali smorzati + testi sbiaditi).
>
> | Data | 2026-07-09 |
> |---|---|
> | File modificati | `Palette/ColorPreview.cs`, `Palette/PaletteController.cs` |

## 1. Anteprima colore consapevole dello strumento (`ColorPreview`)

- **Pen/Fill**: comportamento storico — rettangolo col colore/opacità correnti, scala con Size.
- **Eraser/Delete**: rettangolo **grigio trasparente** (`EraseGray`, alpha 0.40 sulla
  scacchiera) a scala fissa, con sopra l'**icona bianca** dello strumento:
  `ToolIcon.Get("eraser")` per Cancella, `"close"` (✕) per Elimina — le stesse icone dei
  bottoni della riga strumenti, così il linguaggio visivo è coerente.
- L'icona è un quad `RoundedMesh.TexturedQuad` con materiale `PreserveDestAlpha` (niente
  "buco" nel passthrough, vedi `palette-procedurale.md` Step 12); texture scambiata solo al
  cambio strumento (`lastIconTool`), non per frame.

## 1-bis. Slider Size in gomma → grigio "gomma" (2026-07-10)

Feedback dal visore: in Cancella lo slider Size (che regola la **dimensione della gomma**)
restava tinto con **l'ultimo colore selezionato**, confondendo. Ora in Cancella/Elimina il
cuneo dello slider (`SizeSlider.Update`) prende lo **stesso grigio del checker**, così è
chiaro che lì si regola una dimensione, non un colore.

Il grigio è stato spostato in un'**unica fonte** — `StrokeSettings.EraserSwatchColor`
(`(0.62, 0.62, 0.68, 0.40)`) — usata sia da `ColorPreview` (checker) sia da `SizeSlider`.
Nota: il cuneo dello slider è opaco, quindi l'alpha non rende trasparenza (resta grigio
pieno); l'alpha conta nel checker, che sta sopra la scacchiera.

## 1-ter. See-through del checker sul passthrough (2026-07-10)

Feedback dal visore: il checker (swatch) col grigio a alpha 0.40 — e in generale ogni
colore con opacità < 1 — "bucava" l'occlusione del passthrough (si vedeva il mondo reale
attraverso l'anteprima), anche se dietro c'è la **bordatura opaca**. Causa: lo swatch è un
materiale **trasparente** e scriveva il proprio alpha nel framebuffer; su Quest è quel canale
alpha a decidere cosa nasconde il passthrough.

Fix: `BrushMaterials.PreserveDestAlpha(swatchMat)` in `ColorPreview.Build` — lo stesso
accorgimento già usato per l'icona gomma/✕: il colore si fonde normalmente (RGB) ma l'alpha
del framebuffer viene **preservato** (blend alpha Zero/One), così resta l'alpha=1 scritto
dalla bordatura opaca sotto. Niente più see-through, in gomma come con colori semi-trasparenti.

**Stesso fix sulla barra Opacity** (`AlphaSlider`, 2026-07-10): anche la rampa trasparente
davanti alla scacchiera opaca scriveva il proprio alpha e bucava il passthrough dove è quasi
trasparente (lato sinistro), sia con lo slider attivo sia sbiadito in Gomma/Elimina. Aggiunto
`PreserveDestAlpha` alla rampa: l'alpha=1 della scacchiera opaca dietro resta intatto.

## 2. Disattivazione per colore diretto (via il banner)

**Prima**: `MakeDisabledOverlay` creava un quad scuro semitrasparente sopra il gruppo
(Mirror/Grid/Line, striscia pennelli, riga Size) — percepito come sovrapposto male.

**Ora**: `SetControlGroupEnabled` oltre a spegnere i collider applica il dimming
direttamente agli elementi del gruppo:

- **Renderer** (sfondi bottoni, icone, anteprime pennello): `MaterialPropertyBlock` con
  `_BaseColor` = colore del materiale × `DimFactor` (0.35), alpha invariata. Il block sta
  "sopra" il materiale senza toccarlo: i colori veri (selezione/accent scritti dai sync)
  restano nel materiale e riappaiono rimuovendo il block (`SetPropertyBlock(null)`).
  Guardia `HasProperty(_BaseColor)` per saltare i materiali TMP.
- **Testi TMP**: `TMP_Text.alpha` = 0.35 da disabilitati, 1 da attivi.

Rimosso `MakeDisabledOverlay` e i tre overlay (`guidesDim`, `brushDim`, `sizeDim`).
La **label "Size"** (che prima era coperta dal banner ma non faceva parte del gruppo) è ora
registrata in `sizeControls`, così si sbiadisce insieme allo slider.

Nota: se lo stato di un toggle velato cambia da scorciatoia controller mentre è
disabilitato, il dim mostra il colore pre-cambio finché non si riattiva (il sync scrive nel
materiale sotto il block): impercettibile, si riallinea al ritorno su Draw.

## Verifica

- Compilazione: **0 errori**; `Preview All Panels` eseguito via MCP senza eccezioni,
  palette ispezionata: layout integro (il preview mostra lo stato Pen; dimming e icona
  gomma/✕ sono comportamenti runtime al cambio strumento).
- Test in Play/visore a carico dell'utente: selezionare Cancella → checker grigio
  trasparente con icona gomma; Elimina → icona ✕; tornare a Draw (anche toccando la ruota)
  → torna il colore. Mirror/Grid/Line e striscia pennelli fuori da Draw: bottoni scuriti e
  testi sbiaditi al posto del banner, senza artefatti ai bordi; al ritorno su Draw i colori
  (inclusi accent di selezione) tornano esatti.
