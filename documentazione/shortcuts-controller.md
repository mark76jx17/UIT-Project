# Scorciatoie da controller + pannello "Shortcuts" coi controller reali (giugno 2026)

> Rifinitura delle scorciatoie da controller (palette usabile a pannello chiuso) e
> **riprogettazione del pannello "View Shortcuts"**: da due colonne di testo a una
> rappresentazione con le **immagini reali** dei controller Meta Quest Touch Pro e le
> etichette collegate ai tasti tramite linee-guida. Aggiunti due tool da Editor per
> renderizzare i controller e fare l'anteprima del pannello senza indossare il visore.

## Cosa cambia

1. **Pannelli orientati verso l'utente (`FacePlayer`).** I sotto-pannelli modali
   **Options** e **Shortcuts** aprendosi di lato giacevano sul piano della palette e si
   leggevano male. Ora un piccolo `MonoBehaviour` (`FacePlayer`) li orienta verso la testa
   (`Camera.main`) a ogni frame, con uno Slerp esponenziale per togliere il jitter; punta
   subito la testa all'apertura (`OnEnable`). Tocca solo la **rotazione**: la posizione
   resta gestita da `PositionMenus`. La palette NON è influenzata.

2. **Menu Options sul tasto ☰.** L'apertura/chiusura del menu Options è stata spostata da
   A/X (mano-palette) al **tasto menu "classico"** (`OVRInput.Button.Start`), che esiste
   fisicamente solo sul **Touch sinistro** ed è quindi indipendente dalla mano dominante.
   A/X resta libero.

3. **`ControllerShortcuts.All` rimodellata.** La tabella unica passa da
   `(Hand, Group, Action, Input)` testuali a `(Hand, Btn Button, Action)`, dove `Btn` è il
   **tasto fisico** (StickClick/Up/Down/Left/Right, FaceA, FaceB, Menu). Così il pannello
   sa DOVE puntare la linea-guida. Resta l'unica fonte di verità del comportamento e del
   diagramma.

4. **Pannello "Shortcuts" ridisegnato.** Due **righe**, un controller **CENTRATO** per riga:
   - **Immagine reale** del Touch Pro (sinistro/destro) caricata da `Resources/Controllers`,
     renderizzata dall'alto sulla faccia così i tasti sono ben visibili.
   - Etichetta del **ruolo** (Palette/Brush hand) sotto al controller, che segue la mano
     dominante, così l'evidenziazione corrisponde a ciò che si tiene davvero in mano
     (`StrokeSettings.PaletteHand == LTouch`).
   - **Anelli accent** sui tasti usati + **etichette ai due lati** con linea-guida corta: i
     face button (B, Menu) stanno sul lato **esterno** (dove sono fisicamente: sinistra per
     il controller sx, destra per il dx), il blocco *Thumbstick* (click + 4 direzioni) sul
     lato **opposto**. Layout speculare tra le due righe → simmetrico, niente leader che
     attraversano il controller.
   - Testi generati da `ControllerShortcuts.All` (`ActionFor(role, Btn)`); anchor dei tasti
     sull'immagine tarati con `DebugAnchorGrid` (`ImgStick/ImgFaceB/ImgMenuLeft`).

5. **Due tool da Editor (Edit mode, niente Play).**
   - `Tools/Drawing/Bake Controller Images` (`ShortcutControllerBaker`): renderizza i modelli
     FBX reali del Touch Pro (`Packages/com.meta.xr.sdk.core/Meshes/MetaQuestTouchPro/`) a
     PNG con sfondo trasparente, camera **ortografica** e set luci key+fill+rim, via
     `RenderPipeline.SubmitRenderRequest` (URP). Output in `Assets/Drawing/Resources/Controllers/`.
   - `Tools/Drawing/Preview Shortcuts Panel` (`ShortcutsPanelPreview`): costruisce il solo
     pannello (`PaletteController.EditorBuildShortcutsPanel`, guardia `#if UNITY_EDITOR`) e
     lo renderizza in `Assets/Drawing/Previews/shortcuts.png` per iterare il layout. Flag
     `PaletteController.DebugAnchorGrid` per disegnare una griglia di calibrazione anchor.

6. **Commento corretto in `GrabController`.** Il tap di B/Y mentre si afferra = "duplica";
   il commento citava erroneamente il "redo" sull'altra mano: ora dice "Save" (vedi punto 2).

## File toccati

- `Drawing/FacePlayer.cs` — **nuovo**: billboard del pannello verso la testa.
- `Drawing/ControllerShortcuts.cs` — menu su `Button.Start` (LTouch); enum `Btn` e
  `All` rimodellata; rimossa la voce A/X=Options.
- `Drawing/Geometry/GrabController.cs` — commento "duplica" corretto.
- `Palette/PaletteController.cs`
  - `FacePlayer` aggiunto a Options e Shortcuts;
  - `BuildShortcutsPanel` riscritto a **due righe** con `BuildControllerRow`;
  - helper `ActionFor`, `Leader` (barra ruotata), `AnchorDot` (anello), anchor immagine
    `ImgStick/ImgFaceB/ImgMenuLeft`, flag `DebugAnchorGrid`, entry editor
    `EditorBuildShortcutsPanel`;
  - rimossi il vecchio `BuildControllerWidget`/`BuildShortcutColumn` e `ControllerBodyColor`.
- `Palette/ToolIcon.cs` — rimosso il controller **schematico** procedurale
  (`controller-left/right`, `Controller`, anchor), ora sostituito dalle immagini reali.
- `Drawing/Editor/ShortcutControllerBaker.cs` — **nuovo**: baker dei render controller.
- `Drawing/Editor/ShortcutsPanelPreview.cs` — **nuovo**: anteprima del pannello a PNG.
- Asset generati: `Assets/Drawing/Resources/Controllers/touch-left.png`,
  `touch-right.png` (render dei controller). Anteprima (non runtime):
  `Assets/Drawing/Previews/shortcuts.png`.

## Verifica

- Compilazione pulita (0 errori) dopo ogni passo; pannello renderizzato e ispezionato via
  `Preview Shortcuts Panel`.
- Test in Play/visore a carico dell'utente:
  - Options/Shortcuts si aprono rivolti verso l'utente, senza jitter fastidioso.
  - Il menu Options si apre/chiude col tasto **☰** del controller sinistro.
  - Nel pannello Shortcuts gli **anelli** cadono sui tasti corretti (stick / B / ☰): se la
    posizione del ☰ sul sinistro non è precisa, ritararla con `DebugAnchorGrid` e gli anchor
    `ImgMenuLeft` / `ImgFaceB` / `ImgStick`.

## Parametri tarabili

- Billboard: `FacePlayer.TurnSpeed` (più alto = si allinea più in fretta).
- Render controller: `ShortcutControllerBaker.ViewDir` (angolazione), `ModelEuler`,
  `FillPadding`, luci (`AddLight`), `Size`.
- Anteprima: `ShortcutsPanelPreview.OrthoSize`, `Width`/`Height`.
- Pannello: `size` in `BuildShortcutsPanel`; posizioni/anchor in `BuildControllerRow`
  (`ImgStick/ImgFaceB/ImgMenuLeft`, `blockW`, `labelX`, offset dei leader); colore
  `LeaderColor`.
