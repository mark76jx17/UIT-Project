# Room Scale & Ricostruzione dell'Ambiente (MRUK)

> Documento tecnico — Stato dell'implementazione del *Scene Understanding* / room scale nel
> progetto. Pensato per consentire il **riuso** e la **reimplementazione da zero** della feature.
>
> | Campo | Valore |
> |---|---|
> | Data | 2026-06-09 |
> | Branch | `Palette` |
> | Unity | `6000.4.6f1` (Unity 6) |
> | Meta XR SDK | `201.0.0` (core, interaction.ovr, platform) |
> | MR Utility Kit | `com.meta.xr.mrutilitykit` 201.0.0 |
> | Render pipeline | URP 17.4 |
> | XR runtime | OpenXR 1.16 + Oculus/Meta |
> | Scena | `Assets/Scenes/SampleScene.unity` |

---

## 1. Obiettivo

Far sì che, al primo avvio dell'app su Meta Quest, l'ambiente reale dell'utente
(pavimento, pareti, soffitto, mobili) venga **ricostruito in Unity** e usato come
**geometria di collisione**. Risultato concreto richiesto: la palla fisica (`Sphere`)
non deve "volare via" ma deve essere contenuta dalla stanza reale, sostituendo la
precedente soluzione *naive* basata su un piano fittizio.

Punto chiave di progetto: **non implementiamo noi lo scanning**. La mappatura della
stanza è un servizio di sistema di Horizon OS (Space Setup). Il nostro compito è
**leggere** i dati già catturati e trasformarli in geometria/collider utilizzabili.

---

## 2. Background tecnico-scientifico

### 2.1 Lo stack di Scene Understanding di Meta

Tre livelli, dal più basso al più alto:

1. **Spatial Anchors / Tracking (OpenXR + Insight)**
   Il visore Quest usa SLAM (*Simultaneous Localization and Mapping*) tramite le camere
   di tracking (Insight). Gli *spatial anchor* sono pose 6-DoF ancorate al mondo reale e
   ri-localizzate ad ogni sessione, in modo che i contenuti virtuali restino fissi
   rispetto all'ambiente fisico anche dopo riavvii.

2. **Scene Model / Scene API**
   Quando l'utente esegue **Space Setup** dal sistema (Impostazioni → Realtà fisica /
   Space Setup), Horizon OS salva **on-device** un *Scene Model*: un grafo di
   *scene anchor* dotati di:
   - **geometria** — `MRUKAnchor.PlaneRect` (piani 2D: pareti, pavimento, soffitto,
     superfici) e/o `VolumeBounds` (volumi 3D: mobili, scatole);
   - **etichette semantiche** — `FLOOR`, `CEILING`, `WALL_FACE`, `TABLE`, `COUCH`,
     `DOOR_FRAME`, `WINDOW_FRAME`, `STORAGE`, `BED`, `SCREEN`, `LAMP`, `PLANT`,
     `WALL_ART`, `INVISIBLE_WALL_FACE`, `GLOBAL_MESH`, `OTHER`.
   Lo Scene Model **persiste** tra le sessioni e tra le app: una volta mappata, la
   stanza è disponibile a qualsiasi app autorizzata.

3. **MR Utility Kit (MRUK)**
   Toolkit Unity di alto livello sopra la Scene API. Espone una gerarchia C# comoda
   (`MRUK` singleton → `MRUKRoom` → `MRUKAnchor`) e una serie di *tool* pronti
   (generazione mesh/collider, raycast, ricerca di spawn position, ecc.). È il livello
   che usiamo.

### 2.2 World Locking

`MRUK.EnableWorldLock = true` mantiene allineato l'origine di tracking Unity con gli
anchor del mondo reale, compensando il drift dello SLAM. Senza world lock, la
geometria virtuale "scivola" rispetto alla stanza nel tempo. È essenziale per la
mixed reality in passthrough.

### 2.3 Perché i collider e non solo la mesh visibile

In passthrough l'utente **vede già** la sua stanza reale tramite le camere. Non serve
renderizzare la geometria della stanza: serve solo la sua **rappresentazione fisica**.
Per questo generiamo mesh con collider ma con il rendering disattivato
(`hideMesh = true`). La palla collide con muri/pavimento invisibili, l'utente vede la
collisione "contro la parete vera". Questo è il meccanismo che sostituisce il piano naive.

---

## 3. Architettura implementata nella scena

```
SampleScene
├── [BuildingBlock] Camera Rig        (OVRCameraRig — fornito dai Building Block Meta)
├── [BuildingBlock] Passthrough       (passthrough abilitato)
├── [BuildingBlock] Hand Tracking L/R (+ ISDK Hand Grab Interaction)
├── Sphere                            (oggetto di gioco fisico + afferrabile)
│     ├── Rigidbody         (useGravity, collisionDetectionMode = Continuous)
│     ├── SphereCollider
│     └── Oculus.Interaction.Grabbable
├── MRUK                              (← AGGIUNTO)
│     ├── MRUK               (singleton: carica lo Scene Model)
│     └── SceneAccessManager (← script custom: permesso + trigger di caricamento)
└── EffectMesh                        (← AGGIUNTO)
      └── EffectMesh          (genera i collider della stanza)
```

### 3.1 Componente `MRUK`

Singleton che carica lo Scene Model e popola `MRUK.Instance.Rooms`. Configurazione
applicata (divergente dal prefab di default del package):

| Proprietà | Valore | Motivazione |
|---|---|---|
| `EnableWorldLock` | `true` | allineamento stabile virtuale/reale (vedi §2.2) |
| `SceneSettings.LoadSceneOnStartup` | **`false`** | il caricamento è pilotato da `SceneAccessManager` **dopo** aver ottenuto il permesso, per evitare un primo tentativo fallito |
| `SceneSettings.DataSource` | **`Device`** (0) | carica la stanza reale dal dispositivo |

> ⚠️ **Nota sul fallback editor.** Il prefab originale del package usava
> `DataSource = DeviceWithPrefabFallback` (2) con una lista di `RoomPrefabs` demo.
> Impostando la proprietà annidata `SceneSettings` via tooling, i `RoomPrefabs` sono
> stati azzerati. Sul **device** è ininfluente (la stanza si legge dall'hardware e il
> caricamento è esplicito via `LoadSceneFromDevice`). In **editor**, senza visore/Link,
> non esiste una stanza demo: per testare in editor occorre ripristinare
> `DataSource = DeviceWithPrefabFallback` e assegnare un room prefab
> (`Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/...`).

Enum di riferimento (`Meta.XR.MRUtilityKit.MRUK.SceneDataSource`):

```
Device = 0                    // solo dispositivo reale
Prefab = 1                    // solo prefab (test editor)
DeviceWithPrefabFallback = 2  // dispositivo, con prefab come fallback in editor
```

### 3.2 Componente `EffectMesh`

Genera la geometria (mesh + collider) a partire dagli anchor della stanza, in ascolto
sugli eventi di MRUK (`SceneLoadedEvent` / `RoomCreatedEvent`). Configurazione:

| Proprietà | Valore | Motivazione |
|---|---|---|
| `Colliders` | **`true`** | crea i `MeshCollider`/collider sulle superfici → fisica |
| `hideMesh` | **`true`** | nessun rendering: in passthrough si vede la stanza vera |
| `SpawnOnStart` | `true` | si registra agli eventi MRUK e genera al caricamento |
| `Labels` | `442367` (bitmask: tutte) | include pavimento, pareti, soffitto e volumi |
| `MeshMaterial` | `RoomBoxEffects.mat` | usato solo se `hideMesh = false` |

`Labels` è una *flags enum* (`MRUKAnchor.SceneLabels`); `442367` è il set completo di
default. Per fare collidere la palla con solo pavimento+pareti+soffitto si può
restringere a `FLOOR | CEILING | WALL_FACE`.

### 3.3 Script `SceneAccessManager` (`Assets/Scripts/SceneAccessManager.cs`)

Responsabilità: ottenere il permesso runtime **USE_SCENE** e poi avviare il caricamento
della stanza, con fallback automatico a Space Setup se la stanza non è mai stata mappata.

```csharp
using UnityEngine;
using UnityEngine.Android;
using Meta.XR.MRUtilityKit;

namespace MixedRealityProject
{
    public class SceneAccessManager : MonoBehaviour
    {
        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => LoadRoom();
                callbacks.PermissionDenied  += _ =>
                    Debug.LogWarning("[SceneAccessManager] USE_SCENE denied: the room model is unavailable.");
                Permission.RequestUserPermission(ScenePermission, callbacks);
                return;
            }
#endif
            LoadRoom();
        }

        async void LoadRoom()
        {
            if (MRUK.Instance == null)
            {
                Debug.LogError("[SceneAccessManager] No MRUK instance found in the scene.");
                return;
            }
            // requestSceneCaptureIfNoDataFound: true -> apre Space Setup se la stanza
            // non è mai stata catturata su questo dispositivo.
            var result = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true);
            Debug.Log($"[SceneAccessManager] Room load result: {result}");
        }
    }
}
```

Note di design:
- Il blocco di richiesta permesso è dietro `#if UNITY_ANDROID && !UNITY_EDITOR`: in
  editor non esiste il sistema di permessi Android, quindi si passa diretti a `LoadRoom`.
- `PermissionCallbacks` (namespace `UnityEngine.Android`) gestisce in modo asincrono
  l'esito del dialog di sistema: il caricamento parte **solo dopo** la concessione.
- `LoadSceneFromDevice` è asincrono (`Task<LoadDeviceResult>`). Il parametro
  `requestSceneCaptureIfNoDataFound: true` è ciò che rende il "primo avvio = mappa la
  stanza" automatico: se MRUK non trova dati, invoca internamente
  `OVRScene.RequestSpaceSetup()` (flusso nativo di Horizon OS).

### 3.4 La `Sphere` (oggetto fisico)

Già presente in scena con `Rigidbody` (gravità attiva), `SphereCollider` e
`Oculus.Interaction.Grabbable` (afferrabile con le mani). Unica modifica:

| Proprietà | Prima | Dopo | Motivazione |
|---|---|---|---|
| `Rigidbody.collisionDetectionMode` | `Discrete` (0) | `Continuous` (1) | evita il *tunneling*: a velocità elevata un rigidbody Discrete può attraversare un muro sottile in un singolo step fisico |

---

## 4. Permessi Android

`AndroidManifest.xml` (`Assets/Plugins/Android/AndroidManifest.xml`) dichiara già:

```xml
<uses-permission android:name="com.oculus.permission.USE_SCENE" />
<uses-permission android:name="com.oculus.permission.USE_ANCHOR_API" />
```

`USE_SCENE` è una *runtime permission*: la **dichiarazione nel manifest è necessaria
ma non sufficiente** — va richiesta anche a runtime (lo fa `SceneAccessManager`).
Senza permesso concesso, MRUK ritorna `LoadDeviceResult.NoScenePermission` e
`Rooms` resta vuota.

---

## 5. Flusso runtime (sequenza completa)

```
App start
  │
  ├─ SceneAccessManager.Start()
  │     │
  │     ├─ [Android] permesso USE_SCENE concesso? ── no ─► RequestUserPermission()
  │     │                                                      │ (dialog di sistema)
  │     │                                                      ▼
  │     │                                              PermissionGranted ─► LoadRoom()
  │     └─ sì ─────────────────────────────────────────────► LoadRoom()
  │
  ├─ MRUK.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true)
  │     │
  │     ├─ Scene Model presente? ── no ─► OVRScene.RequestSpaceSetup()
  │     │                                    │ (l'utente mappa la stanza:
  │     │                                    │  pavimento, pareti, soffitto)
  │     │                                    ▼
  │     │                                  ricarica Scene Model
  │     └─ sì ──────────────────────────────► popola MRUK.Instance.Rooms
  │
  ├─ MRUK emette SceneLoadedEvent / RoomCreatedEvent
  │     ▼
  ├─ EffectMesh genera mesh + Collider per ogni anchor (hideMesh=true)
  │
  └─ La Sphere (Rigidbody Continuous) collide con pavimento/pareti reali
        ► non vola via — contenuta dalla stanza
```

---

## 6. Procedura di reimplementazione da zero

Prerequisiti: progetto Unity 6 con Meta XR SDK Core e MR Utility Kit installati,
OVRCameraRig in scena (o i Building Block Camera Rig + Passthrough), build target Android.

1. **Manifest** — assicurarsi che `Assets/Plugins/Android/AndroidManifest.xml` contenga
   `<uses-permission android:name="com.oculus.permission.USE_SCENE" />`.
   (Di norma `OVRProjectSetup` lo aggiunge automaticamente quando MRUK è in progetto.)

2. **MRUK** — aggiungere in scena il prefab
   `Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab`. Impostare:
   - `EnableWorldLock = true`
   - `SceneSettings.LoadSceneOnStartup = false`
   - `SceneSettings.DataSource = Device` (o `DeviceWithPrefabFallback` per test in editor,
     assegnando un room prefab).

3. **EffectMesh** — aggiungere il prefab
   `Packages/com.meta.xr.mrutilitykit/Core/Tools/EffectMesh.prefab`. Impostare:
   - `Colliders = true`
   - `hideMesh = true`
   - `Labels` = tutte (oppure `FLOOR | CEILING | WALL_FACE` per le sole superfici).

4. **Script** — creare `Assets/Scripts/SceneAccessManager.cs` (vedi §3.3) e aggiungerlo
   a un GameObject in scena (qui è sul GameObject `MRUK`).

5. **Oggetto fisico** — sull'oggetto che deve essere contenuto dalla stanza: aggiungere
   `Rigidbody` (con gravità) e un `Collider`; impostare
   `collisionDetectionMode = Continuous`.

6. **Build & deploy** su Quest. Al primo avvio, concedere il permesso e (se richiesto)
   completare Space Setup. Verificare nei log: `Room load result: Success`.

---

## 7. Verifica e troubleshooting

| Sintomo | Causa probabile | Rimedio |
|---|---|---|
| `Room load result: NoScenePermission` | permesso non concesso | controllare il dialog runtime / `USE_SCENE` nel manifest |
| `Rooms` vuota, nessun Space Setup | `requestSceneCaptureIfNoDataFound` non attivo | usare il default `true` in `LoadSceneFromDevice` |
| La palla attraversa i muri | `collisionDetectionMode = Discrete` o muri sottili | impostare `Continuous`; alzare gli iteration count del solver |
| Si vede la mesh grigia della stanza | `hideMesh = false` | impostare `hideMesh = true` |
| La stanza "scivola" nel tempo | world lock disattivo | `EnableWorldLock = true` |
| Nessuna stanza in editor | `DataSource = Device` senza hardware | usare `DeviceWithPrefabFallback` + room prefab |

---

## 8. Riferimenti (sorgenti del package, per approfondire)

- `Library/PackageCache/com.meta.xr.mrutilitykit@.../Core/Scripts/MRUK.cs`
  — `SceneDataSource`, `Rooms`, `LoadSceneFromDevice`, eventi.
- `.../Core/Scripts/MRUK.Shared.cs` — controllo permesso `USE_SCENE`, chiamata a
  `OVRScene.RequestSpaceSetup()`.
- `.../Core/Scripts/EffectMesh.cs` — generazione mesh/collider, gestione `Labels`.
- `Library/PackageCache/com.meta.xr.sdk.core@.../Scripts/OVRAnchor/OVRScene.cs`
  — `RequestSpaceSetup()`.

---

## 9. Changelog

| Data | Modifica |
|---|---|
| 2026-06-09 | Stesura iniziale. Aggiunti `MRUK`, `EffectMesh` (collider, hideMesh), script `SceneAccessManager`; `Sphere.Rigidbody` impostato su Continuous. Rimossa la dipendenza dal piano fittizio. |
