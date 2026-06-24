# SketchXR — Miglioramenti implementati

> Documento tecnico dei 5 miglioramenti aggiunti al sistema di disegno, con
> motivazione accademica (collegata ai paper del corso) e indicazione di quale
> sezione del paper SketchXR questi rafforzano.
>
> | Data implementazione | 2026-06-23 |
> |---|---|
> | File modificati | BrushController.cs, GrabController.cs, TubeMesher.cs, Stroke.cs, PaletteRay.cs |
> | File nuovi | DrawingExporter.cs |
> | File aggiornati (funzionalità minori) | DrawingStore.cs, DesktopBrushSimulator.cs |

---

## 1. Haptic Feedback — Impulsi Tattili

### Cosa fa

Tre tipi di impulso aptico sul controller del pennello (mano destra):

| Evento | Durata | Frequenza | Ampiezza | Significato |
|---|---|---|---|---|
| Inizio tratto normale | 40 ms | 0.4 | 0.5 | Conferma che il disegno è iniziato |
| Inizio tratto con snap-merge | 80 ms | 0.8 | 0.8 | Il tratto si è agganciato a uno esistente |
| Fine tratto | 40 ms | 0.3 | 0.4 | Conferma che il tratto è stato salvato |

Sul controller del grab (entrambe le mani):

| Evento | Durata | Note |
|---|---|---|
| Presa oggetto (grip) | 50 ms | Impulso deciso, conferma afferratura |
| Rilascio oggetto | 30 ms | Impulso morbido, oggetto lasciato |

### Come funziona (codice)

`BrushController` e `GrabController` hanno un **timer aptico**: ogni frame se `hapticTimer > 0` si chiama `OVRInput.SetControllerVibration(freq, amp, controller)` e si decrementa il timer; quando scade si chiama con (0, 0) per fermarla.

```csharp
// BrushController — chiamata all'inizio del tratto
HapticPulse(hapticStrokeDuration, frequency: 0.4f, amplitude: 0.5f);

// snap-merge: impulso più lungo e intenso
HapticPulse(hapticSnapDuration, frequency: 0.8f, amplitude: 0.8f);
```

I parametri `hapticStrokeDuration` (default 40 ms) e `hapticSnapDuration` (default 80 ms) sono esposti nell'Inspector Unity → facilmente regolabili senza ricompilare.

### Riferimento accademico

- **Vi et al. (INTERACT 2019)**, UX guideline #7: *"Compelling XR experience"* — feedback tattile aumenta il senso di presenza e riduce l'ambiguità su azioni come lo snap-merge.
- **Vi et al.**, UX guideline #9: *"Feedback & consistency"* — ogni azione deve avere una risposta coerente; gli impulsi differenziati (breve vs lungo) codificano semantica diversa.
- **ch4-VR-AR (Grimm 2022)**: il feedback aptico è classificato come output discreto, complementare al feedback visivo.

### Sezione del paper rafforzata

§4.1 (Brush Drawing), §4.5 (Object Manipulation) — aggiungere una frase su "haptic pulse on stroke start/end and snap-merge (40–80 ms, frequency-coded by event type)".

---

## 2. Campionamento Adattivo alla Velocità

### Cosa fa

La distanza minima tra due campioni della stroke cambia in base alla velocità istantanea del controller:

| Velocità controller | Distanza minima campioni | Effetto |
|---|---|---|
| Bassa (< ~0.05 m/s) | 10 mm (`adaptiveMinSlow`) | Pochi punti: la mano è quasi ferma, campionare di più è rumore |
| Alta (> ~0.5 m/s) | 2 mm (`adaptiveMinFast`) | Molti punti: il gesto è veloce, serve fedeltà geometrica |
| Intermedia | interpolazione lineare | Transizione continua |

Prima: distanza fissa a 5 mm (campo `minSampleDistance`, non più usato in `ContinuePress`).

### Come funziona (codice)

In `BrushController.ContinuePress`:

```csharp
float speed = Vector3.Distance(position, prevPosition) / Mathf.Max(Time.deltaTime, 1e-4f);
float adaptiveMin = Mathf.Lerp(adaptiveMinSlow, adaptiveMinFast,
                                Mathf.Clamp01(speed / adaptiveSpeedMax));
prevPosition = position;

if (Vector3.Distance(smoothed, current.LastPoint) >= adaptiveMin)
    current.AddPoint(smoothed, currentRadius);
```

I tre parametri (`adaptiveSpeedMax`, `adaptiveMinSlow`, `adaptiveMinFast`) sono nell'Inspector. `minSampleDistance` rimane in Inspector come fallback di riferimento ma non è più usato attivamente da `ContinuePress`.

### Riferimento accademico

- **Reading 7 (Output)**: la qualità geometrica di uno stroke dipende dalla densità dei campioni rispetto alla velocità di movimento — più campioni dove la curva cambia più velocemente.
- **StrokeSmoothing.cs (Catmull-Rom)**: il ricampionamento a fine tratto avviene comunque; il campionamento adattivo riduce il numero di punti grezzi in ingresso senza sacrificare dettaglio dove conta.

### Sezione del paper rafforzata

§3.5 (Stroke Geometry & Mesh) — aggiungere: *"sampling distance adapts inversely to controller velocity (2–10 mm range), reducing redundant points at low speed while preserving geometric fidelity at high speed"*.

---

## 3. Mesh a 32-bit + LOD Adattivo per Sezioni Tubo

### 3a — Indici a 32 bit (TubeMesher.cs)

**Prima**: `Mesh.indexFormat` di default → 16 bit → limite a ~64 000 vertici → tratti lunghi venivano troncati silenziosamente.

**Dopo**: nel costruttore di `TubeMesher`:

```csharp
mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
```

Limite pratico elevato a ~1 000 000 vertici (costante `MaxVertices`). Il formato UInt32 è supportato da tutti i GPU dei Meta Quest (OpenGL ES 3.0+/Vulkan).

### 3b — LOD adattivo (Stroke.cs)

**Prima**: numero di lati del tubo fisso: 8 per Round/Glow/Dashed, 2 per Ribbon.

**Dopo**: basato sul raggio iniziale del tratto (`startRadius`, memorizzato in `Begin()`):

| Raggio iniziale | Lati (MeshSides) | Motivo |
|---|---|---|
| < 3 mm | 6 | Tratto sottile: la differenza visiva tra 6 e 8 lati è impercettibile |
| 3–8 mm | 8 | Range standard |
| > 8 mm | 12 | Tratto spesso: 8 lati crea sfaccettature visibili a distanza ravvicinata |

```csharp
int MeshSides
{
    get
    {
        if (brushType == BrushType.Ribbon) return 2;
        if (startRadius < 0.003f) return 6;
        if (startRadius > 0.008f) return 12;
        return 8;
    }
}
```

Il valore è fisso per tutta la durata del tratto (impostato in `Begin()`, riusato in `Finish()` per la mesh smoothed).

### Riferimento accademico

- **Reading 7 (Output)**: il livello di dettaglio geometrico deve essere proporzionale alla dimensione percepita dell'oggetto — tratti piccoli non beneficiano di alta tassellazione.
- **TubeMesher (§3.5 paper)**: la formula S(i, θⱼ) = p̃ᵢ + r(i)(cos θⱼ·Nᵢ + sin θⱼ·Bᵢ) con j ∈ {0, …, MeshSides−1} — il numero di lati ora varia.

### Sezione del paper rafforzata

§3.5 (Stroke Geometry & Mesh) — aggiungere: *"the number of polygonal cross-section sides adapts to stroke radius (6/8/12), reducing vertex count for thin strokes while maintaining roundness for thick ones. Mesh indices use 32-bit format, removing the previous 64K vertex limit"*.

---

## 4. Export OBJ (DrawingExporter.cs)

### Cosa fa

Esporta tutta la scena disegnata come file **Wavefront OBJ** standard, leggibile da Blender, Maya, Cinema 4D, Unreal Engine, importatori Unity, ecc.

- Percorso di default: `[Application.persistentDataPath]/drawing.obj`
- Su Quest: `/sdcard/Android/data/com.xxx.sketchxr/files/drawing.obj` (copiabile via ADB)
- In editor: `~/AppData/LocalLow/[company]/[project]/drawing.obj` (Windows) o `~/Library/Application Support/[company]/[project]/drawing.obj` (macOS)

Ogni `DrawnItem` e tutti i suoi figli (cap, fill surface) diventano oggetti OBJ separati. I vertici sono in **world space** con handedness corretta (flip X per la conversione Unity→OBJ). Le normali sono trasformate correttamente via `TransformDirection`.

### Come si attiva

- **Simulatore desktop (editor)**: tasto `O`
- **Da codice**: `DrawingStore.ExportOBJ()` (chiama `DrawingExporter.ExportToFile()`)
- **Percorso custom**: `DrawingExporter.ExportToFile("/path/to/output.obj")`

### Come funziona (codice)

`DrawingExporter.ExportToOBJ()` itera su tutti i `DrawnItem` attivi, ne raccoglie i `MeshFilter` (inclusi figli), e costruisce una stringa OBJ accumulando:
1. `v x y z` per ogni vertice in world space
2. `vn nx ny nz` per ogni normale
3. `f a//a b//b c//c` per ogni triangolo (con inversione dell'ordine b/c per correggere il winding dopo il flip X)

L'indice parte da 1 (OBJ è 1-based) e si accumula tra oggetti.

### Riferimento accademico

- **DrawingStore (§3.3 Persistence)**: l'export OBJ estende la persistenza al di là del formato proprietario JSON — *"scenes can be exported as standard OBJ meshes for use in external 3D pipelines (Blender, Maya, game engines)"*.
- **Ledo et al. (CHI 2018)**: l'interoperabilità con pipeline standard è un criterio di "technical evaluation" per toolkit HCI.

### Sezione del paper rafforzata

§3.3 (Persistence & Serialization) — aggiungere un paragrafo su OBJ export. §5 (Conclusion/Future Work) — menzione dell'interoperabilità con pipeline 3D standard.

---

## 5. PaletteRay Snap-to-Button (Raycast Redirection)

### Cosa fa

Quando il ray della palette non colpisce direttamente nessun controllo, il sistema cerca se c'è un **PaletteButton** entro un cono di **5° (default)** attorno alla direzione del ray. Se trovato, il ray scatta verso quel pulsante e l'interazione procede come se fosse stato colpito direttamente.

Questo riduce gli errori di selezione su pulsanti piccoli (icone 44 mm) o a distanza, senza modificare il comportamento su slider e color picker (solo PaletteButton).

### Come funziona (codice)

`PaletteRay.TryHitPalette` ora ha due fasi:
1. **Raycast diretto** (comportamento precedente): cerca il controllo palette più vicino lungo il ray esatto.
2. **Snap-to-button** (nuovo): se il raycast diretto non trova nulla, `TrySnapToPaletteButton` usa `Physics.OverlapSphere` per trovare tutti i `PaletteButton` vicini, calcola l'angolo tra il ray e il vettore verso il centro di ogni pulsante, sceglie quello con angolo minore entro `snapAngleDeg`, e rilancia un raycast diretto verso quel pulsante.

```csharp
bool TrySnapToPaletteButton(Vector3 origin, Vector3 dir, out RaycastHit result)
{
    result = default;
    float bestAngle = snapAngleDeg;  // default 5°
    Collider bestCol = null;

    var cols = Physics.OverlapSphere(origin, maxDistance, ~0, QueryTriggerInteraction.Collide);
    foreach (var col in cols)
    {
        if (col.GetComponent<PaletteButton>() == null) continue;
        float angle = Vector3.Angle(dir, col.bounds.center - origin);
        if (angle < bestAngle) { bestAngle = angle; bestCol = col; }
    }

    if (bestCol == null) return false;
    var snapDir = (bestCol.bounds.center - origin).normalized;
    return Physics.Raycast(origin, snapDir, out result, maxDistance, ~0, QueryTriggerInteraction.Collide)
           && result.collider == bestCol;
}
```

Il parametro `snapAngleDeg` (default 5°) è nell'Inspector. Portarlo a 0° disabilita lo snap.

### Differenza rispetto a Gabel et al.

Gabel et al. (SUI 2024) usa una **redirection continua** (il ray devia gradualmente mentre si avvicina al target). La nostra implementazione è più semplice: **snap discreto solo in assenza di hit diretto**. Questo è appropriato per la palette (bersagli fissi, non in movimento) e ha costo computazionale trascurabile.

### Riferimento accademico

- **Gabel et al. (SUI 2024)**: *"both redirection techniques perform significantly better than classic handray"* per la selezione di target piccoli. Citare in §4.2 (Palette Interaction): *"palette ray interaction employs assistive snap-to-button redirection within a 5° angular cone (inspired by Gabel et al., SUI 2024), reducing selection errors on small targets"*.
- **Fitts' Law**: la difficoltà di selezione scala con 1/larghezza_target; lo snap riduce effettivamente la difficoltà aumentando il "bersaglio effettivo".

### Sezione del paper rafforzata

§4.2 (Palette Interaction) — aggiungere descrizione dello snap con citazione Gabel SUI 2024.

---

## Riepilogo file modificati

| File | Modifica |
|---|---|
| `Assets/Scripts/Drawing/Geometry/BrushController.cs` | Haptic feedback (inizio/fine/snap), campionamento adattivo |
| `Assets/Scripts/Drawing/Geometry/GrabController.cs` | Haptic feedback (grab/release) |
| `Assets/Scripts/Drawing/Geometry/TubeMesher.cs` | 32-bit index format, MaxVertices → 1M |
| `Assets/Scripts/Drawing/Geometry/Stroke.cs` | LOD adattivo MeshSides (6/8/12), startRadius |
| `Assets/Scripts/Drawing/Geometry/DrawingExporter.cs` | **NUOVO** — export OBJ |
| `Assets/Scripts/Drawing/Geometry/DrawingStore.cs` | Aggiunto metodo `ExportOBJ()` |
| `Assets/Scripts/Drawing/Palette/PaletteRay.cs` | Snap-to-button (raycast redirection) |
| `Assets/Scripts/Drawing/DesktopBrushSimulator.cs` | Tasto O → export OBJ |

## Note per il paper

Tutti e 5 i miglioramenti sono citabili con i paper del corso:
- **Haptic** → Vi et al. INTERACT 2019 (#7, #9)
- **Campionamento adattivo** → Reading 7 (Output), §3.5
- **32-bit + LOD** → Reading 7, TubeMesher §3.5
- **OBJ export** → Ledo CHI 2018 (technical evaluation), §3.3
- **PaletteRay snap** → Gabel SUI 2024, §4.2
