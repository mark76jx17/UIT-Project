# Modifiche temporanee (da ripristinare)

> Elenco di disattivazioni/patch **temporanee** fatte su richiesta, con come annullarle.
> Tenere corto: quando una voce viene ripristinata, rimuoverla da qui.

## Glow disattivato nella barra pennelli (giugno 2026)

- **Cosa:** il **3° pulsante** della barra laterale sinistra (i 4 tipi di pennello —
  Round, Ribbon, **Glow**, Dashed) è nascosto: il pennello **Glow** non è selezionabile.
- **Perché:** richiesta dell'utente, disattivazione temporanea.
- **Dove:** `Assets/Scripts/Drawing/Palette/PaletteController.cs`, in `BuildBrushStrip`,
  set `disabled` marcato `// TEMP:` (`new HashSet<BrushType> { BrushType.Glow }`).
- **Note:** i pennelli disattivati restano nelle array `brushButtons`/`brushPreviewMats`
  (indici per tipo intatti → `SyncSelection` ok) ma sono nascosti e **non occupano uno
  slot**: la striscia si **ricompatta** sui pennelli attivi (nessun buco). La striscia è
  dimensionata sul numero di pennelli visibili. `BrushType.Glow` esiste ancora.
- **Per riabilitarlo:** togli `BrushType.Glow` dal set `disabled` in `BuildBrushStrip`.
