using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace MixedRealityProject
{
    /// <summary>
    /// Mostra/nasconde la palette con il tasto X del controller sinistro.
    /// Unico proprietario della visibilità del pannello:
    /// visibile = aperta con il tasto X && controller sinistro in mano.
    /// </summary>
    public class PaletteToggle : MonoBehaviour
    {
        [Tooltip("Radice visiva della palette, disattivata quando il pannello è nascosto.")]
        [SerializeField] GameObject content;

        [Tooltip("ControllerActiveState del lato sinistro del rig: Active solo con controller in mano.")]
        [SerializeField] ControllerActiveState leftControllerActive;

        [Tooltip("Durata dell'animazione di apertura/chiusura, in secondi.")]
        [SerializeField] float animationDuration = 0.15f;

        bool isOpen = true;
        float visibility = 1f; // 0 = nascosta, 1 = visibile

        void Update()
        {
            bool inHand = leftControllerActive != null && leftControllerActive.Active;

            // RawButton.X identifica il tasto fisico X: Button.Three mappa sulla X
            // solo interrogando la coppia combinata (Controller.Touch), non LTouch.
            if (inHand && OVRInput.GetDown(OVRInput.RawButton.X))
            {
                isOpen = !isOpen;
                StartCoroutine(HapticPulse());
            }

            float target = isOpen && inHand ? 1f : 0f;
            visibility = Mathf.MoveTowards(
                visibility, target, Time.deltaTime / Mathf.Max(animationDuration, 0.01f));

            // La scala resta > 0: questo GameObject non viene mai disattivato,
            // altrimenti l'Update smetterebbe di girare e il toggle morirebbe.
            transform.localScale = Vector3.one * Mathf.Max(visibility, 0.0001f);

            bool contentVisible = visibility > 0f;
            if (content != null && content.activeSelf != contentVisible)
            {
                content.SetActive(contentVisible);
            }
        }

        IEnumerator HapticPulse()
        {
            OVRInput.SetControllerVibration(1f, 0.5f, OVRInput.Controller.LTouch);
            yield return new WaitForSeconds(0.04f);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        }
    }
}
