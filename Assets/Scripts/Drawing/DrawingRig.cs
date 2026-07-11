using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Bootstrap del sistema di disegno: unico componente da aggiungere in scena
    /// (su un GameObject vuoto). Trova l'OVRCameraRig e monta il pennello sulla
    /// mano destra e la palette sulla sinistra; tutto il resto (pennello, palette
    /// procedurale) è creato a runtime, quindi la scena non contiene UI di palette.
    /// </summary>
    public class DrawingRig : MonoBehaviour
    {
        [Tooltip("Per i mancini: pennello sulla mano sinistra, palette sulla destra. " +
                 "Solo valore di prima esecuzione: in-app si cambia dal menu Options e " +
                 "la scelta viene salvata (vedi StrokeSettings.LeftHanded).")]
        [SerializeField] bool leftHanded;

        // Riferimenti tenuti per riapplicare la mano dominante a runtime (toggle Options)
        // senza ricreare l'intero rig: si riagganciano pennello/palette/grab alle ancore
        // opposte e la palette rifà il layout speculare.
        Transform leftHandAnchor, rightHandAnchor, eyeAnchor;
        GameObject brushGO, paletteGO, paletteGrabGO;
        GrabController brushGrab, paletteGrab;
        PaletteController palette;

        void Start()
        {
            // Reset dello stato static: in editor con "Reload Domain" disattivato la
            // cronologia e la cache materiali sopravvivono tra le sessioni di Play e
            // conserverebbero riferimenti a oggetti distrutti. Sul device gira una
            // sola volta all'avvio ed è innocuo (niente da pulire).
            StrokeHistory.Clear();
            BrushMaterials.ClearCache();

            StrokeSettings.LoadLeftHanded(leftHanded); // ripristina la mano dominante salvata
            StrokeSettings.LoadRecentColors(); // ripristina i 5 colori recenti salvati
            Localization.Load(); // ripristina la lingua scelta per la UI

            // Su Mac/editor senza runtime XR il rig può venire disattivato dal
            // Meta SDK: in quel caso si ripiega sulla camera principale
            // (modalità desktop, pennello mosso dal simulatore).
            var rig = FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            eyeAnchor = rig != null ? rig.centerEyeAnchor
                : Camera.main != null ? Camera.main.transform : null;
            leftHandAnchor = rig != null ? rig.leftHandAnchor : null;
            rightHandAnchor = rig != null ? rig.rightHandAnchor : null;
            var brushAnchor = BrushAnchor();
            var paletteAnchor = PaletteAnchor();

            if (rig == null)
                Debug.LogWarning("[DrawingRig] OVRCameraRig non trovato: modalità desktop con Camera.main.");

            // Pulisce gli orpelli visivi MR del template (comfort vignette della locomotion +
            // EffectMesh che colora le superfici/"confini" della stanza). Vedi la coroutine.
            StartCoroutine(NeutralizeMrVisualClutter());

            // Sopprime il boundary/Guardian (i "confini" colorati che appaiono avvicinandosi ai
            // bordi dell'area): in MR/passthrough da fermi è solo rumore visivo. OVRManager
            // riconcilia questo flag chiamando RequestBoundaryVisibility.
            if (OVRManager.instance != null)
                OVRManager.instance.shouldBoundaryVisibilityBeSuppressed = true;

            brushGO = new GameObject("Brush");
            if (brushAnchor != null)
                brushGO.transform.SetParent(brushAnchor, false);
            var brush = brushGO.AddComponent<BrushController>();
            brushGrab = brushGO.AddComponent<GrabController>();
            brushGrab.Controller = StrokeSettings.BrushHand;
            brushGrab.TipProbe = brush.Tip; // selezione precisa anche con la punta del pennello
            brushGO.AddComponent<PaletteRay>().Brush = brush; // interazione a distanza con la palette

            if (paletteAnchor != null && paletteAnchor != eyeAnchor)
            {
                paletteGrabGO = new GameObject("PaletteHandGrab");
                paletteGrabGO.transform.SetParent(paletteAnchor, false);
                paletteGrab = paletteGrabGO.AddComponent<GrabController>();
                paletteGrab.Controller = StrokeSettings.PaletteHand;

                // Quando la palette è fissata nella stanza si comanda anche col secondo controller
                // (quello che la teneva): ray + poke. Il ray è attivo solo da palette fissata.
                var paletteRay = paletteGrabGO.AddComponent<PaletteRay>();
                paletteRay.Role = PaletteRay.HandRole.Palette;
                paletteRay.RequiresPlaced = true;

                // Sonda di poke sulla mano-palette: piccola sfera trigger + Rigidbody cinematico
                // (serve a generare gli OnTriggerEnter sui pulsanti), marcata PalettePoke.
                var poke = new GameObject("PalettePokeTip");
                poke.transform.SetParent(paletteGrabGO.transform, false);
                poke.transform.localPosition = new Vector3(0f, 0f, 0.03f); // poco davanti al controller
                poke.AddComponent<PalettePoke>();
                var pokeCol = poke.AddComponent<SphereCollider>();
                pokeCol.isTrigger = true;
                pokeCol.radius = 0.014f;
                poke.AddComponent<Rigidbody>().isKinematic = true;
            }

            paletteGO = new GameObject("Palette");
            if (paletteAnchor != null)
                paletteGO.transform.SetParent(paletteAnchor, false);
            palette = paletteGO.AddComponent<PaletteController>();
            palette.Brush = brush;
            palette.HandAnchor = paletteAnchor;

            // Scorciatoie da controller: attivano le funzioni della palette senza aprirla
            // (girano solo con runtime XR attivo; in desktop comanda DesktopBrushSimulator).
            var shortcuts = brushGO.AddComponent<ControllerShortcuts>();
            shortcuts.Palette = palette;
            shortcuts.Head = eyeAnchor;

            // Tutorial guidato: al primo avvio propone il welcome (lingua + Sì/No); riprendibile
            // dal menu Options. Tutta la UI è creata a runtime dal controller.
            var tutorialGO = new GameObject("Tutorial");
            var tutorial = tutorialGO.AddComponent<TutorialController>();
            tutorial.Brush = brush;
            tutorial.Palette = palette;
            tutorial.Head = eyeAnchor;
            tutorial.PaletteHand = paletteAnchor;
            tutorial.BrushHand = brushAnchor;
            Debug.Log("[Tutorial] DrawingRig created TutorialController");

            // Toggle "Left-Handed Mode" dal menu Options: riapplica le mani a runtime.
            StrokeSettings.LeftHandedChanged += ApplyHandedness;

            // I tratti usano materiali Lit: senza nemmeno una luce in scena
            // resterebbero al buio (in MR/passthrough non c'è skybox).
            if (FindAnyObjectByType<Light>(FindObjectsInactive.Exclude) == null)
            {
                var lightGO = new GameObject("DrawingLight");
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.shadows = LightShadows.None;
                lightGO.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            }

            // Toast: messaggi a schermo (Salva/Carica/Svuota/Undo…), visibili sul visore.
            if (FindAnyObjectByType<ToastController>() == null)
                new GameObject("Toast").AddComponent<ToastController>();

#if UNITY_EDITOR
            // Senza visore gli anchor restano fermi all'origine: il simulatore
            // muove il pennello col mouse e la palette viene appesa davanti
            // alla camera invece che al polso. Con un runtime XR attivo
            // (Meta XR Simulator) invece le mani sono tracciate: tutto resta
            // com'è sul device e il simulatore mouse si disattiva da solo.
            brushGO.AddComponent<DesktopBrushSimulator>();
            if (eyeAnchor != null && !UnityEngine.XR.XRSettings.isDeviceActive)
            {
                paletteGO.transform.SetParent(eyeAnchor, false);
                palette.localOffset = new Vector3(-0.22f, -0.06f, 0.55f);
                palette.localEuler = Vector3.zero;
            }
#endif
        }

        void OnDestroy() => StrokeSettings.LeftHandedChanged -= ApplyHandedness;

        // Neutralizza la comfort vignette/tunneling della locomotion Meta SENZA disabilitare
        // LocomotionTunneling: quel componente, all'avvio, è ciò che mette il FOV pieno (360°)
        // e poi gestisce il fade; spegnerlo prima che si inizializzi lasciava il renderer
        // TunnelingEffect bloccato sullo stato del prefab → vignette sempre chiusa. Invece
        // appiattisco a 360° le curve di forza (rotazione/accelerazione/movimento): così, anche
        // quando il destro genera un evento, il FOV richiesto è sempre pieno e l'effetto non si
        // vede. Cerco per NOME (niente dipendenza dall'assembly Oculus.Interaction) e aspetto un
        // frame, così tutti gli Awake/Start (incluso un eventuale *Setting che riscrive le curve)
        // sono già passati e l'override è l'ultimo a vincere.
        System.Collections.IEnumerator NeutralizeMrVisualClutter()
        {
            yield return null; // lascia inizializzare la locomotion (vignette aperta a riposo)

            var flat = AnimationCurve.Constant(0f, 1f, 360f); // 360° = FOV pieno → nessuna vignette
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null)
                    continue;
                var t = mb.GetType();
                switch (t.Name)
                {
                    case "LocomotionTunneling":
                        foreach (var prop in new[] { "RotationStrength", "AccelerationStrength", "MovementStrength" })
                        {
                            var p = t.GetProperty(prop);
                            if (p != null && p.CanWrite)
                                p.SetValue(mb, flat);
                        }
                        break;
                    case "LocomotionComfortVignetteSetting":
                        mb.enabled = false; // non far riscrivere curve di chiusura
                        break;
                    case "EffectMesh":
                        // EffectMesh (MRUK) colora le superfici della stanza (i "confini"
                        // celestini). HideMesh=true spegne i renderer già creati e, dato che
                        // CreateMesh imposta renderer.enabled = !hideMesh, nasconde anche le mesh
                        // generate più tardi quando MRUK finisce di caricare la stanza.
                        var hide = t.GetProperty("HideMesh");
                        if (hide != null && hide.CanWrite)
                            hide.SetValue(mb, true);
                        break;
                }
            }
        }

        // Ancore in base alla mano dominante. Senza rig (desktop) le ancore mani sono
        // null e la palette ripiega sulla camera, come all'avvio.
        Transform BrushAnchor() => StrokeSettings.LeftHanded ? leftHandAnchor : rightHandAnchor;

        Transform PaletteAnchor()
        {
            if (leftHandAnchor == null && rightHandAnchor == null)
                return eyeAnchor;
            return StrokeSettings.LeftHanded ? rightHandAnchor : leftHandAnchor;
        }

        // Riapplica la mano dominante a runtime (toggle "Left-Handed Mode" del menu Options):
        // riassegna i controller, riaggancia pennello/palette/grab alle ancore opposte e fa
        // rifare alla palette il layout speculare. I tratti disegnati sono oggetti del mondo
        // indipendenti, quindi non vengono toccati.
        void ApplyHandedness()
        {
            var brushAnchor = BrushAnchor();
            var paletteAnchor = PaletteAnchor();

            if (brushGrab != null)
                brushGrab.Controller = StrokeSettings.BrushHand;
            if (paletteGrab != null)
                paletteGrab.Controller = StrokeSettings.PaletteHand;

            if (brushGO != null && brushAnchor != null)
                brushGO.transform.SetParent(brushAnchor, false);
            if (paletteGrabGO != null && paletteAnchor != null && paletteAnchor != eyeAnchor)
                paletteGrabGO.transform.SetParent(paletteAnchor, false);

            if (palette != null)
            {
                palette.HandAnchor = paletteAnchor;
                palette.Rebuild(); // strisce sul lato della mano che disegna
            }
        }
    }
}
