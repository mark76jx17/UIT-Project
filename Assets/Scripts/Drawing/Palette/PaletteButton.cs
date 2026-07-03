using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Se valorizzata, il pulsante è un TOGGLE: dopo la pressione restituisce il nuovo
        /// stato on/off, così il feedback sceglie il suono giusto (ascendente/discendente).
        /// Null = pulsante "azione" (suono di click). Impostata da PaletteController.
        /// </summary>
        public Func<bool> ToggleState;

        /// <summary>
        /// Se true, la pressione NON emette il click di default: l'azione collegata fornisce
        /// già un proprio suono (apertura menu/shortcuts), e suonarli insieme darebbe una
        /// sgradevole sovrapposizione audio. Impostata da PaletteController.
        /// </summary>
        public bool SilentPress;

        /// <summary>
        /// Registro di tutti i pulsanti attivi: lo snap-to-button del PaletteRay lo
        /// scorre invece di fare un Physics.OverlapSphere da 2 m su tutta la scena
        /// ogni frame. I pulsanti sono pochi e noti, non serve la fisica.
        /// </summary>
        public static readonly List<PaletteButton> Instances = new();

        /// <summary>Collider del pulsante, cachato per lo snap del ray.</summary>
        public Collider Col { get; private set; }

        const float DebounceSeconds = 0.3f;
        const float PressAnimSeconds = 0.05f; // durata dell'affondo "click" del pulsante

        float lastPress = -1f;
        Vector3 baseScale;
        bool hovered;

        void Awake()
        {
            baseScale = transform.localScale;
            Col = GetComponent<Collider>();
        }

        void OnEnable() => Instances.Add(this);
        void OnDisable()
        {
            Instances.Remove(this);
            hovered = false;
        }

        /// <summary>Evidenzia il pulsante quando il ray lo punta (prima di premerlo),
        /// ingrandendolo un filo. Riduce i click sbagliati su bersagli piccoli.</summary>
        public void SetHover(bool on)
        {
            if (hovered == on)
                return;
            hovered = on;
            transform.localScale = on ? baseScale * 1.10f : baseScale;
            if (on)
                UiFeedback.Instance?.Hover(); // tick leggero all'ingresso dell'hover
        }

        void OnTriggerEnter(Collider other)
        {
            // Poke con la punta del pennello O con la sonda della mano-palette (palette fissata):
            // così si può toccare da entrambi i controller.
            if (other.GetComponentInParent<BrushTip>() == null
                && other.GetComponentInParent<PalettePoke>() == null)
                return;
            // Se un menu modale (Options) è aperto, i controlli "sotto" non rispondono.
            if (!PaletteController.IsInteractable(gameObject))
                return;
            // Mentre la palette viene trascinata i bottoni "passano" sopra la punta: niente press.
            if (PaletteController.IsGrabbing)
                return;
            Press();
        }

        /// <summary>Pressione diretta (usata anche dal simulatore desktop col click).</summary>
        public void Press()
        {
            if (Time.time - lastPress < DebounceSeconds)
                return;
            lastPress = Time.time;
            OnPressed?.Invoke(); // per i toggle questo aggiorna già lo stato letto sotto
            if (ToggleState != null)
                UiFeedback.Instance?.Toggle(ToggleState());
            else if (!SilentPress)
                UiFeedback.Instance?.Press();
            StartCoroutine(PressAnim());
        }

        // Affondo visivo del pulsante alla pressione (il suono/vibrazione li dà UiFeedback).
        System.Collections.IEnumerator PressAnim()
        {
            transform.localScale = baseScale * 0.85f;
            yield return new WaitForSeconds(PressAnimSeconds);
            transform.localScale = hovered ? baseScale * 1.10f : baseScale;
        }
    }
}
