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
