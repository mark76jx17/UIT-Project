# Pulizia ambiente MR: comfort vignette, boundary, EffectMesh, scena (giugno 2026)

> Rimozione degli orpelli visivi che il template Meta (Building Blocks) aggiunge e che in
> quest'app di disegno da fermi sono solo rumore o bug. Più la bonifica di due oggetti
> rimasti per sbaglio nella scena. Tutto emerso dai test in visore dell'utente.

## 1. Comfort vignette / tunneling della locomotion (FOV che si chiude)

**Sintomo:** muovendo l'analogico destro (qui usato per le **scorciatoie**, non per
spostarsi) il campo visivo si chiudeva con la classica vignette comfort della VR.

**Causa:** la building block di locomotion include `LocomotionTunneling` (renderer
`TunnelingEffect`) + `LocomotionComfortVignetteSetting`.

**Tentativo sbagliato (da non rifare):** disabilitare `LocomotionTunneling`. È proprio quel
componente che all'avvio mette il FOV pieno (`UserFOV=360`) e gestisce il fade; spegnerlo
**prima** che si inizializzi lascia il renderer bloccato sullo stato del prefab → **vignette
sempre chiusa**, app inutilizzabile.

**Soluzione adottata** (`DrawingRig.NeutralizeMrVisualClutter`, coroutine che aspetta **un
frame** così tutti gli `Awake/Start` sono passati): NON si disabilita `LocomotionTunneling`,
si **appiattiscono a 360° le sue curve di forza** (`RotationStrength`/`AccelerationStrength`/
`MovementStrength` = `AnimationCurve.Constant(_, _, 360)`). Così, anche quando il destro
genera un evento, il FOV richiesto è sempre pieno → l'effetto non si vede mai. In più si
disabilita `LocomotionComfortVignetteSetting` (così non riscrive curve di chiusura).

## 2. Boundary / Guardian (i "confini" colorati)

**Sintomo:** comparivano i confini dell'area di gioco.
**Soluzione** (`DrawingRig.Start`): `OVRManager.instance.shouldBoundaryVisibilityBeSuppressed
= true`. OVRManager riconcilia il flag chiamando `RequestBoundaryVisibility(Suppressed)`.
**Nota:** può essere **rifiutato** dal runtime (`Warning_BoundaryVisibilitySuppressionNotAllowed`)
se manca l'entitlement/feature OpenXR "Boundary Visibility"; in tal caso il boundary resta
visibile e va abilitata la feature nelle impostazioni Meta XR.

## 3. EffectMesh: superfici della stanza colorate (celestino)

**Sintomo:** tutte le superfici/"confini" della stanza apparivano tinte di celeste.
**Causa:** `EffectMesh` di **MR Utility Kit** genera le mesh delle superfici rilevate dalla
Scene API e applica un materiale (il celeste).
**Soluzione** (stessa coroutine): `HideMesh = true`. Spegne i `MeshRenderer` delle mesh
**già create** e, dato che `EffectMesh.CreateMesh` imposta `renderer.enabled = !hideMesh`,
nasconde anche quelle **generate più tardi**, quando MRUK finisce di caricare la stanza
(caricamento asincrono). Copre entrambi i casi di timing.

> Tutto in §1–§3 è cercato **per nome di tipo** (`FindObjectsByType<MonoBehaviour>` + match
> sul `GetType().Name`) e impostato via reflection, così non serve una dipendenza dagli
> assembly Meta (`Oculus.Interaction.*`, MRUK).

## 4. Oggetti rimasti per sbaglio nella scena

### 4a. Doppione `PalettePreview`
In `SampleScene` c'era un GameObject root **`PalettePreview`** (a `(20, 1.2, 0)`) con un
`PaletteController` e l'**intera palette "cotta"** (22 `PaletteButton`, mesh, materiali)
salvata per sbaglio — la palette vera la crea solo `DrawingRig` a runtime. Quel doppione
restava fisso in scena (visto come una palette "specchiata/strana" sotto il pavimento) ed
eseguiva pure un **secondo `PaletteController`** all'avvio. **Rimosso** l'intero sottoalbero.
NB: il tool *Tools/Palette Preview/Build* può ricrearlo — non salvare la scena dopo averlo
lanciato.

### 4b. Camere fantasma `PreviewCam` → "tutto grigio"
Il tool **Preview Shortcuts Panel** creava una `PreviewCam` (background **grigio opaco**
`{0.3, 0.3, 0.33}`, entrambi gli occhi) **non figlia di `root`**, mentre il cleanup nel
`finally` distruggeva solo `root`: ogni esecuzione lasciava una camera orfana in scena.
Finché non si è **salvata la scena**, innocue; col salvataggio si sono persistite → in
visore **3 camere grigie coprivano tutto il passthrough** ("tutto grigio").
**Fix:** eliminate le 3 `PreviewCam` orfane dalla scena **e** corretto
`ShortcutsPanelPreview.cs` perché la `PreviewCam` sia figlia di `root` (così viene distrutta
col cleanup e non se ne creano più). Vedi anche `documentazione/shortcuts-controller.md` §5.

## File toccati

- `Drawing/DrawingRig.cs` — `StartCoroutine(NeutralizeMrVisualClutter())` (vignette + EffectMesh,
  per nome/reflection) e `shouldBoundaryVisibilityBeSuppressed = true`.
- `Drawing/Editor/ShortcutsPanelPreview.cs` — `PreviewCam` resa figlia di `root`.
- `Assets/Scenes/SampleScene.unity` — rimossi `PalettePreview` (intero sottoalbero) e le 3
  `PreviewCam` orfane (modifica via editor MCP + save).

## Verifica

- Compilazione 0 errori. Scena salvata: 0 `PaletteController`/`PaletteButton` residui, solo
  le 3 camere legittime del rig OVR, nessun background grigio `{0.3,0.3,0.33}` nel file.
- Test in visore (a carico dell'utente): nessuna vignette al movimento del destro,
  nessun confine Guardian, nessuna superficie celeste, nessuna palette fantasma, passthrough
  normale (niente grigio). **Confermato funzionante dall'utente.**

> Modifica anche di **scena** (non solo codice): nel commit comparirà `SampleScene.unity`
> con un diff grande (sono soprattutto le righe di mesh-data di `PalettePreview` rimosse).
> Commit a carico dell'utente.
