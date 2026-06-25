# Menu Options: posizione, modale, aspetto e handedness dinamico (giugno 2026)

> Rifinitura del sotto-pannello **Options** (aperto dal bottone "tre puntini" nella
> striscia azioni). Prima compariva **centrato sul** pannello principale, lasciava
> interagibili i controlli sotto e mostrava sempre l'etichetta fissa "Left-Handed".

## Cosa cambia

1. **Compare sopra la palette.** L'OptionsPanel ora fluttua **un po' più in alto** del
   pannello principale (centro a `y = paletteHalfH(0.24) + 0.025 + size.y/2`), con un
   piccolo stacco dal bordo superiore, invece che centrato a `y = 0`.

2. **Modale: niente interazioni "sotto".** Mentre il menu è aperto, **solo** i controlli
   figli dell'OptionsPanel rispondono; il resto della palette resta visibile ma inerte,
   così non lo si tocca per sbaglio con la punta o col ray.
   - Nuovo stato statico `PaletteController.ModalRoot` + helper
     `IsInteractable(GameObject)` (`true` se non c'è modale o se il controllo è figlio di
     `ModalRoot`).
   - `PaletteRay.IsPaletteControl` e lo snap-to-button saltano i controlli non
     interagibili; `PaletteButton.OnTriggerEnter` ignora il poke sui controlli "sotto".
   - `ModalRoot` viene tenuto allineato ogni frame in `AnimateVisibility` (e impostato
     subito in `ToggleOptionsPanel`/`CloseOptionsPanel`).

3. **Bottone di chiusura (X).** Dato che, da modale, il bottone "Options" della striscia
   azioni non è più premibile (è "sotto" il menu), il menu ha un suo **X** in alto a
   destra (`CloseOptionsPanel`). Il tasto hardware della mano-palette continua comunque a
   aprire/chiudere l'intera palette.

4. **Aspetto più curato.** Pannello più grande e arrotondato, **header** con titolo
   "Options" allineato a sinistra + **linea separatrice** accent sotto, voci spaziate.

5. **Handedness con label dinamica.** Una sola voce alterna la mano dominante; la label
   mostra la **modalità verso cui si commuta**:
   - destrorso attuale → label **"Left-Handed"**;
   - dopo la pressione (mancino, palette a destra) e riapertura → label **"Right-Handed"**.
   La voce si illumina (accent) quando il mancino è attivo. Aggiornata via `optionsSync`
   (solo al cambio di stato). `MakeLabel` ora **restituisce** il `TextMeshPro` per poterne
   aggiornare il testo.

## File toccati

- `Palette/PaletteController.cs`
  - aggiunti `ModalRoot` + `IsInteractable`;
  - `AnimateVisibility`: tiene `ModalRoot` allineato allo stato mostrato;
  - `BuildOptionsPanel`: posizione sopra, header+separatore, bottone X, voce handedness
    con label dinamica;
  - `ToggleOptionsPanel`/nuovo `CloseOptionsPanel`: gestione stato modale;
  - `MakeLabel`: ora ritorna il `TextMeshPro`.
- `Palette/PaletteButton.cs` — `OnTriggerEnter`: rispetta `IsInteractable` (poke).
- `Palette/PaletteRay.cs` — `IsPaletteControl` e snap-to-button: rispettano `IsInteractable`.

## Verifica

- Coerenza interazione: il toggle handedness emette `LeftHandedChanged` →
  `DrawingRig.ApplyHandedness` → `palette.Rebuild()`, che distrugge solo `panel`
  (l'OptionsPanel è figlio di `transform`, sopravvive) e non svuota `optionsSync`.
- Test in Play/visore a carico dell'utente: aprire Options (compare sopra), provare a
  toccare i controlli sotto (devono restare inerti), chiudere con X, e verificare che la
  label passi Left-Handed ↔ Right-Handed.

## Parametri tarabili
- Posizione/dimensione menu: `size`, lo stacco `0.025` e `paletteHalfH` in `BuildOptionsPanel`.
- Header: font titolo (`SliderLabelFont * 1.25`), spessore/larghezza `HeaderSep`.
- Voce handedness: `handCell` (dimensione/posizione), testo nelle due modalità.
