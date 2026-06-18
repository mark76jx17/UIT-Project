using System;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Pulsante fisico della palette: si preme toccandolo con la punta del
    /// pennello (BrushTip). Niente ray/poke ISDK per ora: un trigger collider è
    /// sufficiente, autonomo e senza wiring di scena.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PaletteButton : MonoBehaviour
    {
        public Action OnPressed;

        const float DebounceSeconds = 0.3f;
        const float HapticDuration = 0.05f;

        float lastPress = -1f;
        Vector3 baseScale;

        void Awake()
        {
            baseScale = transform.localScale;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<BrushTip>() == null)
                return;
            Press();
        }

        /// <summary>Pressione diretta (usata anche dal simulatore desktop col click).</summary>
        public void Press()
        {
            if (Time.time - lastPress < DebounceSeconds)
                return;
            lastPress = Time.time;
            OnPressed?.Invoke();
            StartCoroutine(PressFeedback());
        }

        System.Collections.IEnumerator PressFeedback()
        {
            transform.localScale = baseScale * 0.85f;
            OVRInput.SetControllerVibration(0.5f, 0.5f, StrokeSettings.BrushHand);
            yield return new WaitForSeconds(HapticDuration);
            OVRInput.SetControllerVibration(0f, 0f, StrokeSettings.BrushHand);
            transform.localScale = baseScale;
        }
    }
}
