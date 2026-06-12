using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace MixedRealityProject
{
    /// <summary>
    /// Polish della palette (Fase 5): suoni UI e aptica sul controller destro,
    /// agganciati agli eventi del PointableCanvas — valgono quindi sia per il
    /// poke sia per il ray, su entrambe le pagine del pannello.
    /// Hover = ingresso del puntatore nel pannello; Select/Unselect = press/release.
    /// </summary>
    public class PaletteFeedback : MonoBehaviour
    {
        [SerializeField] PointableCanvas pointableCanvas;
        [SerializeField] AudioSource audioSource;

        [Header("Suoni")]
        [SerializeField] AudioClip pressClip;
        [SerializeField] AudioClip releaseClip;
        [SerializeField] AudioClip hoverClip;
        [SerializeField, Range(0f, 1f)] float hoverVolume = 0.35f;

        [Header("Aptica (mano dominante)")]
        [SerializeField, Range(0f, 1f)] float pressAmplitude = 0.5f;
        [SerializeField, Range(0f, 1f)] float hoverAmplitude = 0.15f;

        void OnEnable()
        {
            pointableCanvas.WhenPointerEventRaised += OnPointerEvent;
        }

        void OnDisable()
        {
            pointableCanvas.WhenPointerEventRaised -= OnPointerEvent;
        }

        void OnPointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover:
                    if (hoverClip != null)
                    {
                        audioSource.PlayOneShot(hoverClip, hoverVolume);
                    }
                    StartCoroutine(Pulse(hoverAmplitude, 0.015f));
                    break;

                case PointerEventType.Select:
                    if (pressClip != null)
                    {
                        audioSource.PlayOneShot(pressClip);
                    }
                    StartCoroutine(Pulse(pressAmplitude, 0.04f));
                    break;

                case PointerEventType.Unselect:
                    if (releaseClip != null)
                    {
                        audioSource.PlayOneShot(releaseClip);
                    }
                    break;
            }
        }

        IEnumerator Pulse(float amplitude, float duration)
        {
            OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(duration);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
    }
}
