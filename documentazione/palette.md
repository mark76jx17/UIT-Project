# Palette — Menu ancorato al controller sinistro

> Documento tecnico — Stato dell'implementazione della feature *Palette*: pannello UI
> ancorato alla mano non dominante (sinistra), interagibile con la mano dominante
> (destra) via poke o ray. Pensato per consentire il **riuso** e la **reimplementazione
> da zero** della feature.
>
> | Campo | Valore |
> |---|---|
> | Data | 2026-06-12 |
> | Branch | `Palette` |
> | Unity | `6000.4.6f1` (Unity 6) |
> | Meta XR SDK | `201.0.0` (core, interaction.ovr, platform) |
> | Render pipeline | URP 17.4 |
> | Scena | `Assets/Scenes/SampleScene.unity` |

---

## 1. Obiettivo e design

Metafora della **tavolozza del pittore** (pattern Tilt Brush, vedi
[case study UX](https://blog.hamaluik.ca/posts/vr-ux-case-study-tilt-brush/)):

- la **mano non dominante (sinistra)** porta la palette, ancorata al controller —
  il menu è sempre raggiungibile perché attaccato alla mano, non world-locked;
- la **mano dominante (destra)** seleziona: **poke** (tocco diretto) o
  **ray + grilletto** (a distanza);
- interfacce 2D (uGUI) disposte nello spazio 3D.

### Piano di sviluppo (fasi)

| Fase | Contenuto | Stato |
|---|---|---|
| 1a | Shell: `PalettePanel` segnaposto ancorato al controller sinistro, posa da tarare | ✅ fatto (2026-06-12) |
| 1b | Script `PaletteToggle`: tasto X mostra/nasconde, animazione scala, aptica, **visibile solo con controller sinistro in mano** | da fare |
| 2 | Interazione: `PointableCanvasModule` + `ControllerPokeInteractor` destro + canvas pointable | da fare |
| 3 | Contenuto: swatch colori, Flexible Color Picker, slider dimensione/opacità, toggle strumenti | da fare |
| 4 | `PaletteState`: stato (colore, dimensione, strumento) esposto con eventi C#, disaccoppiato dal futuro sistema di disegno | da fare |
| 5 | Polish: aptica hover/press, suoni UI, test simulatore + Quest 3S | da fare |

### Asset previsti

1. **Meta UI Set** — `com.meta.xr.sdk.interaction` (Essentials) `201.0.0`, già nel progetto:
   `Runtime/Sample/Objects/UISet/Prefabs/` (Backplate, Button, ContextMenu, Dialog,
   DropDown, Patterns, Slider, TextInputField, Tooltip).
2. **FlatUnityCanvas** — `Runtime/Sample/Objects/Props/FlatUnityCanvas.prefab` +
   `PointableCanvasModule`: rende un canvas uGUI interagibile in VR (poke/ray/scroll).
   Il canvas curvo è overkill per un pannello palmare.
3. **[Flexible Color Picker](https://assetstore.unity.com/packages/tools/gui/flexible-color-picker-150497)**
   (Asset Store) — picker HSV uGUI; su un PointableCanvas funziona in VR senza codice
   custom. **Da importare manualmente** dal Package Manager (My Assets). Serve in Fase 3.

### Stato della scena rilevante (rilevato il 2026-06-12)

- Il rig `[BuildingBlock] OVRInteractionComprehensive` supporta già **mani e controller**
  con switching automatico (`ControllerActiveState` / `HandActiveState`).
- Sul lato destro esiste già `RightInteractions/Interactors/Controller/ControllerRayInteractor`
  (ray + grilletto): il fallback a distanza della Fase 2 è già coperto.
- **Manca** un `ControllerPokeInteractor` sotto `Interactors/Controller` (solo ray) e
  **manca** il `PointableCanvasModule` in scena → entrambi da aggiungere in Fase 2.
- ⚠️ Il toggle con `OVRInput.Button.Three` (tasto X) funziona **solo con i controller**:
  in modalità hand-tracking la palette non sarà apribile (eventuale gesto in futuro).

---

## 2. Fase 1a — Shell segnaposto (implementata)

Nessun codice: solo gerarchia di scena, per tarare la **posa ergonomica** del pannello
prima di scrivere lo script di toggle.

### Gerarchia creata

```
[BuildingBlock] Camera Rig
└── TrackingSpace
    └── LeftHandAnchor
        └── LeftControllerAnchor          (anchor 6-DoF del controller sinistro)
            └── PalettePanel              (← NUOVO: contenitore, futuro target del toggle)
                └── PlaceholderVisual     (← NUOVO: cubo sottile = segnaposto visivo)
```

### `PalettePanel` (GameObject vuoto)

| Proprietà | Valore | Motivazione |
|---|---|---|
| Parent | `LeftControllerAnchor` | segue il controller sinistro (stile Tilt Brush, non world-locked) |
| `localPosition` | `(0, 0.05, 0)` | ~5 cm sopra la faccia superiore ("nera") del controller |
| `localRotation` | `(90, 0, 0)` | pannello **sdraiato, parallelo alla faccia dei pulsanti** (piano XZ dell'anchor): si legge dall'alto come una tavolozza; il lato "alto" della UI punta in avanti lungo il controller |

> Posa iniziale `(0, 0.10, -0.02)` / `(-30, 0, 0)` (pannello quasi verticale inclinato
> verso l'utente) scartata dopo il primo test: l'utente si aspetta la palette parallela
> alla superficie nera del controller, poco sopra di essa.
>
> Nota orientamento: con rot. X = +90° la normale del lato "fronte" del futuro canvas
> uGUI (−Z locale) punta verso l'alto → la UI sarà leggibile guardando il pannello
> dall'alto. Se la faccia nera reale del controller risultasse inclinata rispetto al
> piano XZ dell'anchor, correggere solo la X della rotazione (90 ± offset).

È il **contenitore**: lo script `PaletteToggle` (Fase 1b) scalerà/attiverà questo
oggetto; il contenuto UI (Fase 2-3) sarà suo figlio. I valori di posa sono il punto
di partenza da **tarare in build** (vedi §3).

### `PlaceholderVisual` (cubo primitivo)

| Proprietà | Valore | Motivazione |
|---|---|---|
| Primitive | `Cube` | visibile da ogni lato (un Quad sarebbe invisibile da dietro) |
| `localScale` | `(0.20, 0.15, 0.005)` | 20×15 cm = ingombro previsto del pannello reale, 5 mm di spessore |
| `BoxCollider` | **rimosso** | non deve collidere con la palla né coi futuri interactor |
| Material | `Assets/Materials/PalettePlaceholder.mat` | URP/Lit, grigio-blu scuro `(0.15, 0.17, 0.22)` |

Verrà **sostituito** dal canvas reale (FlatUnityCanvas + UI Set) in Fase 2.

### Asset creati

- `Assets/Materials/` (nuova cartella)
- `Assets/Materials/PalettePlaceholder.mat` — URP/Lit, baseColor `(0.15, 0.17, 0.22, 1)`

---

## 3. Verifica della Fase 1a (build & run)

Su Quest (o simulatore con controller emulati):

1. Il pannello scuro deve apparire **sospeso ~5 cm sopra la faccia nera del controller
   sinistro**, parallelo ad essa, e seguirlo rigidamente (nessun lag, nessun drift).
2. Posa naturale: tenendo il controller davanti al petto, il pannello si legge
   dall'alto come una tavolozza, senza torcere il polso.
3. Il pannello **non** deve interferire con la palla (nessuna collisione).
4. In questa fase il pannello è **sempre visibile**, anche con controller posato /
   hand tracking: il vincolo "visibile solo con controller in mano" arriva con lo
   script della Fase 1b. Ignorare.

**Taratura**: se la posa non è comoda, i valori da ritoccare sono `localPosition` e
`localRotation` di `PalettePanel` (Inspector, anche in Play mode per provare live —
i valori provati in Play vanno poi ricopiati). Tipicamente: più alto/basso → `y`;
avanti/indietro lungo il controller → `z`; parallelismo con la faccia nera → `x`
della rotazione (90 ± offset).

---

## 4. Changelog

| Data | Modifica |
|---|---|
| 2026-06-12 | **Fase 1a.** Creati `PalettePanel` (vuoto, figlio di `LeftControllerAnchor`, pos `(0, 0.10, -0.02)`, rot `(-30, 0, 0)`) e `PlaceholderVisual` (cubo 20×15×0,5 cm, collider rimosso, material `PalettePlaceholder.mat` nuovo in `Assets/Materials/`). Nessun codice. Posa da tarare in build. |
| 2026-06-12 | **Fase 1a — revisione posa** dopo feedback utente: `PalettePanel` ora **parallelo alla faccia nera del controller** (rot `(90, 0, 0)`) e ~5 cm sopra di essa (pos `(0, 0.05, 0)`). Aggiunto requisito alla Fase 1b: palette visibile **solo con controller sinistro in mano** (la visibilità avrà un unico proprietario, lo script `PaletteToggle`: `visibile = apertaConTastoX && controllerInMano`). |
