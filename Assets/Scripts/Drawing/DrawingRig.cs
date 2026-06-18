using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Bootstrap del sistema di disegno: unico componente da aggiungere in scena
    /// (su un GameObject vuoto). Trova l'OVRCameraRig e monta il pennello sulla
    /// mano destra e la palette sulla sinistra. Disabilita anche, se presenti,
    /// i GameObject della vecchia palette UI (PalettePanel + sfera BrushTip
    /// sul controller destro) e l'eventuale componente PaletteState legacy:
    /// così la nuova palette procedurale può convivere in scena senza dover
    /// editare manualmente la gerarchia esistente.
    /// </summary>
    public class DrawingRig : MonoBehaviour
    {
        [Tooltip("Per i mancini: pennello sulla mano sinistra, palette sulla destra.")]
        [SerializeField] bool leftHanded;

        [Tooltip("Disabilita PalettePanel e BrushTip della vecchia palette UI al primo avvio.")]
        [SerializeField] bool disableLegacyPalette = true;

        void Start()
        {
            StrokeSettings.BrushHand = leftHanded ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            StrokeSettings.PaletteHand = leftHanded ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            StrokeSettings.LoadRecentColors(); // ripristina i 5 colori recenti salvati

            if (disableLegacyPalette)
                DisableLegacyPalette();

            // Su Mac/editor senza runtime XR il rig può venire disattivato dal
            // Meta SDK: in quel caso si ripiega sulla camera principale
            // (modalità desktop, pennello mosso dal simulatore).
            var rig = FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            var eyeAnchor = rig != null ? rig.centerEyeAnchor
                : Camera.main != null ? Camera.main.transform : null;
            var brushAnchor = rig != null ? (leftHanded ? rig.leftHandAnchor : rig.rightHandAnchor) : null;
            var paletteAnchor = rig != null ? (leftHanded ? rig.rightHandAnchor : rig.leftHandAnchor) : eyeAnchor;

            if (rig == null)
                Debug.LogWarning("[DrawingRig] OVRCameraRig non trovato: modalità desktop con Camera.main.");

            var brushGO = new GameObject("Brush");
            if (brushAnchor != null)
                brushGO.transform.SetParent(brushAnchor, false);
            var brush = brushGO.AddComponent<BrushController>();
            brushGO.AddComponent<GrabController>().Controller = StrokeSettings.BrushHand;
            brushGO.AddComponent<PaletteRay>().Brush = brush; // interazione a distanza con la palette

            if (paletteAnchor != null && paletteAnchor != eyeAnchor)
            {
                var paletteGrabGO = new GameObject("PaletteHandGrab");
                paletteGrabGO.transform.SetParent(paletteAnchor, false);
                paletteGrabGO.AddComponent<GrabController>().Controller = StrokeSettings.PaletteHand;
            }

            var paletteGO = new GameObject("Palette");
            if (paletteAnchor != null)
                paletteGO.transform.SetParent(paletteAnchor, false);
            var palette = paletteGO.AddComponent<PaletteController>();
            palette.Brush = brush;
            palette.HandAnchor = paletteAnchor;

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

        /// <summary>
        /// La scena pushata su git contiene la vecchia palette UI (PaletteToggle
        /// + PaletteState + PaletteFeedback su un Canvas) e una sfera BrushTip
        /// di anteprima colore sul controller destro: tutto sostituito dalla
        /// palette procedurale e dal BrushController della nuova logica di
        /// disegno. Disabilito le radici, non li cancello, così se serve si
        /// possono riattivare a mano senza ri-cablare i riferimenti nella scena.
        /// </summary>
        void DisableLegacyPalette()
        {
            var legacyToggle = FindAnyObjectByType<PaletteToggle>(FindObjectsInactive.Include);
            if (legacyToggle != null)
                legacyToggle.gameObject.SetActive(false);

            // La vecchia sfera BrushTip (anteprima colore) è un Renderer figlio
            // del controller destro: il namespace è quello di root, non Drawing.
            var legacyTip = FindAnyObjectByType<MixedRealityProject.BrushTip>(FindObjectsInactive.Include);
            if (legacyTip != null)
                legacyTip.gameObject.SetActive(false);
        }
    }
}
