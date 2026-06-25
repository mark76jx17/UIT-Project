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
        [Tooltip("Per i mancini: pennello sulla mano sinistra, palette sulla destra.")]
        [SerializeField] bool leftHanded;

        void Start()
        {
            // Reset dello stato static: in editor con "Reload Domain" disattivato la
            // cronologia e la cache materiali sopravvivono tra le sessioni di Play e
            // conserverebbero riferimenti a oggetti distrutti. Sul device gira una
            // sola volta all'avvio ed è innocuo (niente da pulire).
            StrokeHistory.Clear();
            BrushMaterials.ClearCache();

            StrokeSettings.BrushHand = leftHanded ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            StrokeSettings.PaletteHand = leftHanded ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            StrokeSettings.LoadRecentColors(); // ripristina i 5 colori recenti salvati

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
    }
}
