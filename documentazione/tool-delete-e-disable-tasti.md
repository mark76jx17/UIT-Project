# Strumento "Delete" + disabilitazione controlli in base allo strumento attivo — giugno/luglio 2026

> Due lavori collegati sulla gestione degli **strumenti** (`ToolMode`): (1) un nuovo strumento
> **Delete** che cancella l'intero oggetto toccato, distinto dalla **gomma** (che è parziale);
> (2) la **disabilitazione automatica** dei controlli della palette non pertinenti allo
> strumento attivo (es. i pennelli e Mirror/Grid/Line non hanno senso in Fill/Erase/Delete).
>
> | Data | 2026-06-29 → 2026-07-03 |
> |---|---|
> | Commit | `7ca412d` (Delete + gestione tasti Draw/Fill/Erase/Delete), `ac864bb`, `f448cce`, `02fba0b` |
> | File | `StrokeSettings.cs`, `Geometry/BrushController.cs`, `Palette/PaletteController.cs`, `ControllerShortcuts.cs`, `DesktopBrushSimulator.cs` |

## 1. I quattro strumenti (`ToolMode`)

```csharp
public enum ToolMode { Pen, Fill, Eraser, Delete }
```

- **Pen** — disegna tratti.
- **Fill** — secchiello (vedi `fill-secchiello.md`).
- **Eraser** — gomma: cancellazione **parziale** e annullabile (toglie solo la porzione
  toccata, ricostruisce i pezzi rimasti).
- **Delete** — **nuovo**: cancella **l'intero** oggetto toccato in un colpo.

### Delete vs Eraser (`BrushController`)

`DeleteAt(position)` trova l'oggetto sotto la punta (`FindNearbyItem`), lo **nasconde**
(`SetActive(false)`) e registra `StrokeHistory.PushErase` → **annullabile** con Undo. È più
diretto della gomma: nessuna ricostruzione parziale, si toglie tutto l'oggetto. Come la gomma,
evidenzia in rosso l'oggetto puntato prima di cancellare (`UpdateEraseHover`).

Nel simulatore desktop lo strumento si cicla; da controller la scorciatoia X cicla tutti e 4
(`ControllerShortcuts`: `Tool = (ToolMode)(((int)Tool + 1) % 4)`), con toast del nome. Nota:
esiste anche il **"Delete all"** (svuota tutto) sotto pressione prolungata di B/Y (~1.5 s,
`DeleteHoldSeconds`, solo se non stai afferrando) — è un'altra cosa dal tool Delete. Da
2026-07-10 l'hold non svuota subito ma apre la **conferma Sì/No** della palette
(`PaletteController.OpenConfirmClear`); vedi `undo-clear-totale.md` per conferma +
annullabilità dello svuota-tutto.

## 2. Disabilitazione controlli per strumento (`SyncToolControlAvailability`)

Ogni frame la palette abilita/disabilita **gruppi di controlli** in base allo strumento
attivo, così non si tocca per sbaglio un controllo che non ha effetto:

| Gruppo | Abilitato per | Contenuto |
|---|---|---|
| `sizeControls` | **Pen, Eraser** | slider spessore (il raggio vale anche per la gomma) |
| `alphaControls` | **Pen, Fill** | slider Opacity + label (l'opacità serve solo col colore) |
| `penOnlyControls` | **solo Pen** | Pressure, Mirror/Grid/Line (draw-only), guide |
| `brushOnlyControls` | **solo Pen** | striscia dei 4 tipi di pennello |

> **Zona colore sempre attiva.** Ruota/luminosità/recenti NON sono più in un gruppo
> disabilitabile: restano attivi in ogni strumento, perché toccarli in Gomma/Elimina
> riattiva il pennello (`StrokeSettings.ReactivatePen`). Lo **slider Opacity** invece è
> stato staccato in `alphaControls` (2026-07-10): in Gomma/Elimina non ha effetto utile e
> toccarlo non riattiva il pennello (imposta solo `Alpha`), quindi si sbiadisce e non è
> toccabile.

Meccanismo (`SetControlGroupEnabled`): abilita/disabilita **i collider** di tutto il
sotto-albero del controllo (così ray e poke non lo raggiungono) e **smorza direttamente
l'aspetto** dei controlli (materiali via `MaterialPropertyBlock` × `DimFactor`, testi TMP
via `alpha`) — non più un overlay scuro sovrapposto (vedi `anteprima-colore-e-dim-diretto.md`).
Lo switch scatta **solo al cambio** di stato (campi `lastSizeEnabled`/`lastAlphaEnabled`/…),
non ogni frame.

I controlli si registrano al gruppo alla costruzione (`RegisterPenOnlyControl`, ecc.); i
toggle Mirror/Grid/Line passano da `MakeIconToggleButton` che li marca automaticamente
pen-only.

> Complementare al **modale Options** (vedi `menu-options.md`): `IsInteractable(go)` /
> `ModalRoot` gestiscono l'inerzia dei controlli "sotto" un menu aperto; `SyncToolControlAvailability` gestisce l'inerzia dei
> controlli non pertinenti allo strumento. `PaletteButton`,
> `PaletteRay` e lo snap-to-button consultano `IsInteractable` prima di agire.

## 3. Pulsanti tool in palette

La riga strumenti ha **Draw** e **Fill** come celle piene, mentre **Erase** e **Delete**
condividono una cella allargata divisa in due (`BuildEraseDeleteButtons`): ognuno imposta il
proprio `ToolMode` e si illumina (accent) quando attivo (`SyncSelection` colora
pen/fill/eraser/delete). Delete usa l'icona `close` (la ✕ procedurale), Erase l'icona `eraser`.

## Aggiornamento (2026-07-09) — evidenziazione di Elimina con isteresi

Feedback dal visore: l'eliminazione era precisa ma il **rosso spariva allontanandosi appena**
dalla punta (l'hover usava il raggio secco `snapRadius` = 2,5 cm) e non era chiaro quando il
bersaglio fosse rilevato. Fix in `BrushController`:

- **Isteresi**: l'aggancio del rosso avviene entro `snapRadius` (preciso, come prima), ma una
  volta evidenziato il bersaglio **resta agganciato** finché la punta è entro
  `deleteHoverExit` (default **6 cm**, in Inspector) — niente rosso che va e viene ai bordi.
- **Tick aptico all'aggancio** (come per il magnete): si *sente* quando il bersaglio è rilevato.
- **Coerenza rosso→azione**: `DeleteAt` elimina **esattamente ciò che è evidenziato**
  (`eraseHover`), anche se l'isteresi lo tiene agganciato poco oltre il raggio d'ingresso:
  quello che è rosso è quello che sparisce.

### Fix (2026-07-10) — il rosso "flashava" e spariva subito

Al test in visore il rosso appariva e svaniva quasi subito nonostante l'isteresi. Causa:
**conflitto tra due evidenziazioni sullo stesso oggetto**. Il rosso viene applicato **una
volta sola** all'aggancio (poi `UpdateEraseHover` esce finché il bersaglio non cambia); ma
sulla stessa mano gira anche l'**hover di presa** del `GrabController` (schiaritura 1.2×),
che scrive sullo **stesso `MaterialPropertyBlock`**: entrando lo sovrascriveva e — peggio —
uscendo dal suo raggio (più stretto dei 6 cm dell'isteresi) faceva `Clear()`, spegnendo il
rosso senza che il `BrushController` se ne accorgesse (bersaglio ancora agganciato → nessuna
riapplicazione).

Fix: il rosso ha **priorità**. `BrushController` espone `ActiveEraseHover` (statico,
sincronizzato da una property su `eraseHover`) e `GrabController.SetHover` **non tocca**
(né `Set` né `Clear`) l'oggetto attualmente rosso. Limite noto (raro): afferrando col grip
l'oggetto rosso, o candidandolo al magnete viola mentre si tiene altro, quelle tinte
prevalgono — sono azioni esplicite dell'utente.

## Verifica

- Compilazione: **0 errori** (MCP refresh + console); pannelli ispezionati coi tool di preview
  (`Preview All Panels`) — la riga mostra Draw/Fill/Erase/Delete.
- Test in Play/visore a carico dell'utente: Delete cancella l'intero oggetto ed è annullabile;
  passando a Fill/Erase/Delete i gruppi non pertinenti si spengono (overlay scuro, collider
  off) e tornano attivi su Draw; il pulsante attivo è evidenziato.
