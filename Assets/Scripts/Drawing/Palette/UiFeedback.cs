using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Feedback centralizzato per l'interazione con la palette: suono + vibrazione.
    /// I suoni sono SINTETIZZATI a runtime (nel progetto non c'è alcun file audio):
    /// brevi blip/sweep generati con un'onda sinusoidale e un inviluppo. La vibrazione
    /// usa OVRInput con un timer per mano (Brush = pressione/hover dei pulsanti,
    /// Palette = apri/chiudi pannello). Accentra l'aptico che prima era sparso tra
    /// PaletteButton e PaletteController.
    ///
    /// Singleton leggero: vive sul GameObject della palette, lo crea PaletteController
    /// in Start (RequireComponent aggiunge da sé l'AudioSource). In editor i suoni si
    /// sentono comunque; la vibrazione è un no-op senza visore.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class UiFeedback : MonoBehaviour
    {
        public static UiFeedback Instance { get; private set; }

        const int SampleRate = 44100;

        AudioSource source;
        AudioClip click, toggleOn, toggleOff, hover, panelOpen, panelClose;

        // Vibrazione: un impulso "in corso" per ciascuna delle due mani usate dalla palette.
        struct Pulse { public float time, freq, amp; }
        Pulse brush, palette;

        void Awake()
        {
            Instance = this;

            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.6f; // un filo spazializzato: "viene" dal pannello
            source.dopplerLevel = 0f;

            // Blip a decadimento esponenziale per click/tick/toggle; sweep morbidi (finestra
            // sinusoidale) per apri/chiudi pannello. Frequenze in salita = "positivo/apertura",
            // in discesa = "negativo/chiusura".
            click      = Blip("uiClick",      900f,  700f, 0.05f, 0.50f);
            toggleOn   = Blip("uiToggleOn",   620f, 1040f, 0.09f, 0.45f);
            toggleOff  = Blip("uiToggleOff", 1040f,  620f, 0.09f, 0.45f);
            hover      = Blip("uiHover",     1600f, 1600f, 0.02f, 0.18f);
            panelOpen  = Sweep("uiPanelOpen", 420f,  940f, 0.14f, 0.40f);
            panelClose = Sweep("uiPanelClose", 940f, 420f, 0.14f, 0.40f);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ---- API pubblica (chiamata da PaletteButton e PaletteController) ----

        /// <summary>Pressione di un pulsante "azione" (strumento, pennello, Undo/Save/…).</summary>
        public void Press()
        {
            source.PlayOneShot(click);
            Vibrate(ref brush, StrokeSettings.BrushHand, 0.05f, freq: 0.5f, amp: 0.50f);
        }

        /// <summary>Pressione di un toggle: suono ascendente (on) o discendente (off).</summary>
        public void Toggle(bool on)
        {
            source.PlayOneShot(on ? toggleOn : toggleOff);
            Vibrate(ref brush, StrokeSettings.BrushHand, 0.05f, freq: 0.5f, amp: 0.55f);
        }

        /// <summary>Hover: il ray inizia a puntare un pulsante (tick leggero, niente "click").</summary>
        public void Hover()
        {
            source.PlayOneShot(hover);
            Vibrate(ref brush, StrokeSettings.BrushHand, 0.02f, freq: 0.3f, amp: 0.20f);
        }

        /// <summary>Apertura/chiusura del pannello (mano palette).</summary>
        public void PanelToggle(bool open)
        {
            source.PlayOneShot(open ? panelOpen : panelClose);
            Vibrate(ref palette, StrokeSettings.PaletteHand, 0.05f, freq: 0.4f, amp: 0.50f);
        }

        // ---- Vibrazione: timer per mano, la spegne Update ----

        void Vibrate(ref Pulse p, OVRInput.Controller hand, float duration, float freq, float amp)
        {
            // Se due impulsi si sovrappongono sulla stessa mano, tieni quello più intenso.
            if (p.time <= 0f || amp >= p.amp)
            {
                p.freq = freq;
                p.amp = amp;
            }
            p.time = Mathf.Max(p.time, duration);
        }

        void Update()
        {
            Step(ref brush, StrokeSettings.BrushHand);
            Step(ref palette, StrokeSettings.PaletteHand);
        }

        void Step(ref Pulse p, OVRInput.Controller hand)
        {
            if (p.time <= 0f)
                return;
            OVRInput.SetControllerVibration(p.freq, p.amp, hand);
            p.time -= Time.deltaTime;
            if (p.time <= 0f)
                OVRInput.SetControllerVibration(0f, 0f, hand);
        }

        // ---- Sintesi audio (nessun file: AudioClip costruiti a runtime) ----

        // Inviluppo a decadimento esponenziale → attacco istantaneo, coda corta: "click".
        static AudioClip Blip(string name, float f0, float f1, float dur, float vol)
        {
            int n = Mathf.Max(1, (int)(SampleRate * dur));
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-6f * t);
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2.0 * System.Math.PI * f / SampleRate;
                data[i] = Mathf.Sin((float)phase) * env * vol;
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Inviluppo a finestra (0 → 1 → 0): attacco e rilascio morbidi, per gli sweep del pannello.
        static AudioClip Sweep(string name, float f0, float f1, float dur, float vol)
        {
            int n = Mathf.Max(1, (int)(SampleRate * dur));
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Sin(Mathf.PI * t);
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2.0 * System.Math.PI * f / SampleRate;
                data[i] = Mathf.Sin((float)phase) * env * vol;
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
