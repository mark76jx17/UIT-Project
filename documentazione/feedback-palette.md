# Feedback palette — audio + vibrazione (giugno 2026)

> Migliora il feedback dell'interazione con la **palette**: ogni azione dà ora un
> **suono** e una **vibrazione** coerenti. Prima l'aptico era frammentato (solo
> pressione pulsanti e apri/chiudi pannello) e l'audio **non esisteva** in tutto il
> progetto.

## Cosa cambia

- **Suono** su: pressione pulsanti (azioni e toggle), **hover** dei pulsanti,
  apertura/chiusura del pannello.
- **Vibrazione** estesa e centralizzata: oltre alla pressione e all'apri/chiudi,
  ora c'è anche un **tick leggero sull'hover**.
- L'aptico prima sparso (in `PaletteButton` e `PaletteController`) è stato **accentrato**
  in un unico componente.

> Scope deciso: NON c'è feedback sul trascinamento di ruota/slider (ColorWheel,
> Brightness/Alpha/SizeSlider). Lasciato fuori di proposito; l'architettura è pronta
> per aggiungerlo se servisse.

## Sorgente audio: sintetizzata, nessun file

Nel progetto non c'è alcun asset audio. I suoni sono **generati a runtime** in codice
(`AudioClip.Create` + onda sinusoidale con inviluppo), coerente con l'approccio
procedurale già usato per mesh e icone. Vantaggi: niente file da importare/wirare,
peso trascurabile su Quest.

Due forme d'onda:
- **Blip** — inviluppo a decadimento esponenziale (attacco istantaneo, coda corta):
  per click, tick, toggle.
- **Sweep** — inviluppo a finestra sinusoidale (attacco/rilascio morbidi): per
  apri/chiudi pannello.

Convenzione tonale: frequenza **in salita** = positivo/apertura, **in discesa** =
negativo/chiusura.

## Architettura

`Palette/UiFeedback.cs` — **componente centrale** (una responsabilità: dare feedback
all'interazione UI). Singleton leggero (`UiFeedback.Instance`), vive sul GameObject
della palette; lo crea `PaletteController` in `Start` (`RequireComponent` aggiunge da
sé l'`AudioSource`). Possiede:
- i 6 `AudioClip` sintetizzati all'`Awake`;
- un `AudioSource` (`spatialBlend 0.6` → il suono "viene" dal pannello);
- i **pattern di vibrazione**, con un timer per mano spento in `Update`
  (Brush = pressione/hover, Palette = apri/chiudi).

API pubblica: `Press()`, `Toggle(bool on)`, `Hover()`, `PanelToggle(bool open)`.

**Flusso (chi chiama cosa):**
- `PaletteButton.Press()` → `Toggle(stato)` se è un toggle, altrimenti `Press()`.
- `PaletteButton.SetHover(true)` → `Hover()` (solo all'ingresso dell'hover).
- `PaletteController` (trigger sinistro o tasto **P** in editor) → `PanelToggle(isOpen)`.

Per distinguere azione vs toggle, `PaletteButton` ha un campo `ToggleState`
(`Func<bool>`): null = azione (click); valorizzato = toggle (legge il nuovo stato
on/off dopo la pressione). Lo imposta `PaletteController.MakeToggleButton`.

## Suoni e vibrazioni (valori di partenza, da tarare)

| Interazione        | Suono (f0→f1, durata, vol)      | Vibrazione (mano, dur, freq, amp) |
|--------------------|----------------------------------|-----------------------------------|
| Pressione azione   | Blip 900→700 Hz, 0.05s, 0.50     | Brush, 0.05s, 0.5, 0.50           |
| Toggle ON          | Blip 620→1040 Hz, 0.09s, 0.45    | Brush, 0.05s, 0.5, 0.55           |
| Toggle OFF         | Blip 1040→620 Hz, 0.09s, 0.45    | Brush, 0.05s, 0.5, 0.55           |
| Hover              | Blip 1600 Hz, 0.02s, 0.18        | Brush, 0.02s, 0.3, 0.20           |
| Apertura pannello  | Sweep 420→940 Hz, 0.14s, 0.40    | Palette, 0.05s, 0.4, 0.50         |
| Chiusura pannello  | Sweep 940→420 Hz, 0.14s, 0.40    | Palette, 0.05s, 0.4, 0.50         |

I parametri sono tutti raccolti in cima a `UiFeedback.cs` (`Awake` per i suoni, le
chiamate `Vibrate(...)` per l'aptico): **un unico posto da regolare**.

## File toccati

**Nuovo**
- `Palette/UiFeedback.cs` — componente di feedback (sintesi audio + pattern aptici).

**Modificati**
- `Palette/PaletteButton.cs` — campo `ToggleState`; instrada press/toggle/hover su
  `UiFeedback`; rimosso l'aptico inline; `PressFeedback` → `PressAnim` (solo l'affondo
  visivo, suono/vibrazione li dà `UiFeedback`).
- `Palette/PaletteController.cs` — crea `UiFeedback` in `Start`; apri/chiudi pannello
  ora chiama `PanelToggle` (anche dal tasto **P** in editor); marca i toggle con
  `ToggleState`; rimossa la vecchia coroutine `HapticPulse`.

## Note / da verificare in Play

- **Serve un `AudioListener` in scena** (di norma sul Center Eye dell'OVRCameraRig):
  senza, nessun suono si sente. Da confermare al primo test.
- Volume/durata/frequenze sono valori di partenza: tarare su device se troppo
  forti/deboli o fastidiosi sull'hover (l'hover scatta a ogni ingresso su un pulsante).
- In editor (simulatore) i **suoni si sentono**, la **vibrazione è no-op** senza visore.
- Compilazione verificata (MCP refresh + console): **0 errori**. Test in Play sul
  device a carico dell'utente.
