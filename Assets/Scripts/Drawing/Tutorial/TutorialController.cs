using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Regia del tutorial guidato: al primo avvio propone il welcome (lingua + Sì/No), poi guida
    /// i 5 step del nucleo mostrando cartello + alone sul bersaglio e avanzando quando lo step è
    /// completato (polling dello stato pubblico). Interrompibile (Esci) e riprendibile/ricominciabile
    /// dal menu Options. Stato persistito in TutorialProgress.
    /// </summary>
    public class TutorialController : MonoBehaviour
    {
        // Iniettati da DrawingRig.
        public BrushController Brush;
        public PaletteController Palette;
        public Transform Head;
        public Transform PaletteHand;
        public Transform BrushHand;

        // Istanza per le voci del menu Options (partial di PaletteController).
        public static TutorialController Instance { get; private set; }

        TutorialCard card;
        TutorialHighlighter highlighter;
        TutorialWelcomePanel welcome;

        // Ancora FISSA nella stanza: le schermate del tutorial ci stanno agganciate e NON seguono
        // controller/testa. Posata davanti all'utente all'avvio, poi congelata.
        Transform roomAnchor;
        bool posed;

        List<TutorialStep> steps;
        int index;
        bool running;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            Localization.LanguageChanged -= OnLanguageChanged;
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            try
            {
                Debug.Log($"[Tutorial] Start begin (Head={(Head != null ? Head.name : "null")}, " +
                          $"PaletteHand={(PaletteHand != null ? PaletteHand.name : "null")})");

                // Ancora fissa nella stanza (posata al primo frame valido, vedi LateUpdate).
                roomAnchor = new GameObject("TutorialRoomAnchor").transform;
                roomAnchor.SetParent(transform, false);

                card = new GameObject("TutorialCard").AddComponent<TutorialCard>();
                card.transform.SetParent(roomAnchor, false);
                card.OnSkip = Advance;
                card.OnExit = ExitTutorial;
                card.Build();
                Debug.Log("[Tutorial] card built");

                highlighter = new GameObject("TutorialHighlighter").AddComponent<TutorialHighlighter>();
                highlighter.transform.SetParent(transform, false);

                welcome = new GameObject("TutorialWelcome").AddComponent<TutorialWelcomePanel>();
                welcome.transform.SetParent(roomAnchor, false);
                welcome.Build(OnWelcomeYes, OnWelcomeNo);
                Debug.Log("[Tutorial] welcome built");

                Localization.LanguageChanged += OnLanguageChanged;

                // Il welcome compare a OGNI avvio dell'app (richiesta esplicita): niente gate sul
                // flag "proposed". Chi non lo vuole preme No e usa l'app; al riavvio ricompare.
                ShowWelcome();
                Debug.Log($"[Tutorial] ShowWelcome done (welcome active={welcome.gameObject.activeSelf})");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Tutorial] Start FAILED: " + e);
            }
        }

        // ---- Welcome ----

        void OnWelcomeYes()
        {
            TutorialProgress.Proposed = true;
            welcome.Hide();
            StartFromBeginning();
        }

        void OnWelcomeNo()
        {
            TutorialProgress.Proposed = true;
            welcome.Hide();
        }

        // ---- API pubblica per il menu Options ----

        /// <summary>Avvia il tutorial dallo step 1.</summary>
        public void StartFromBeginning()
        {
            FreezeAnchorInFront(); // fissa le schermate davanti a dove guardi ORA
            steps = TutorialStep.CoreSteps(Brush, Palette);
            TutorialProgress.State = TutorialState.InProgress;
            EnterStep(0);
        }

        /// <summary>Riprende dallo step salvato.</summary>
        public void Resume()
        {
            FreezeAnchorInFront();
            steps = TutorialStep.CoreSteps(Brush, Palette);
            TutorialProgress.State = TutorialState.InProgress;
            EnterStep(Mathf.Clamp(TutorialProgress.Step, 0, steps.Count - 1));
        }

        // Posa l'ancora davanti allo sguardo attuale e la congela: da qui le schermate sono fisse.
        void FreezeAnchorInFront()
        {
            var h = Head != null ? Head : (Camera.main != null ? Camera.main.transform : null);
            if (h != null && roomAnchor != null)
            {
                Vector3 fwd = h.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f)
                    fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 pos = h.position + fwd * 0.6f;
                roomAnchor.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - h.position));
            }
            posed = true;
        }

        /// <summary>Mostra di nuovo la schermata di benvenuto.</summary>
        public void ShowWelcome()
        {
            Palette?.SetOpen(false); // al primo avvio (e quando si ripropone) la palette parte chiusa
            welcome.RefreshText();
            welcome.Show();
        }

        // ---- Macchina a stati ----

        void EnterStep(int i)
        {
            index = i;
            steps[i].Enter?.Invoke();
            TutorialProgress.Step = i;
            ConfigureCard(steps[i]);
            card.SetSkipVisible(i < steps.Count - 1); // niente "Salta step" sull'ultimo
            card.Show();
            running = true;
        }

        // Imposta immagine/anello/testo e ancora della card per lo step (senza rieseguire Enter,
        // così è richiamabile anche al cambio lingua senza azzerare gli snapshot).
        void ConfigureCard(TutorialStep step)
        {
            if (step.Hand != CoachHand.None)
            {
                bool physLeft = step.Hand == CoachHand.Brush
                    ? StrokeSettings.LeftHanded : !StrokeSettings.LeftHanded;
                var tex = ControllerHint.Image(physLeft);
                var a = step.Button == CoachButton.Grip
                    ? ControllerHint.Grip(physLeft) : ControllerHint.Trigger(physLeft);
                // Didascalia: controller destro/sinistro (si specchia coi mancini) + nome tasto.
                string side = Localization.Get(physLeft ? "tutorial.ctrl.left" : "tutorial.ctrl.right");
                string btn = Localization.Get(step.Button == CoachButton.Grip
                    ? "tutorial.btn.grip" : "tutorial.btn.trigger");
                card.SetStep(Localization.Get(step.TitleKey), tex, a, true, side + "\n" + btn);
            }
            else
            {
                card.SetStep(Localization.Get(step.TitleKey), null, default, false);
            }
        }

        void Advance()
        {
            if (!running)
                return;
            index++;
            if (index >= steps.Count)
                Finish();
            else
                EnterStep(index);
        }

        void Finish()
        {
            running = false;
            TutorialProgress.State = TutorialState.Completed;
            highlighter.Hide();
            StartCoroutine(ShowDoneThenHide());
        }

        IEnumerator ShowDoneThenHide()
        {
            card.SetStep(Localization.Get("tutorial.done"), null, default, false);
            card.SetSkipVisible(false); // messaggio finale: niente "Salta step"
            card.Show();
            yield return new WaitForSeconds(2.5f);
            card.Hide();
        }

        void ExitTutorial()
        {
            running = false;
            TutorialProgress.State = TutorialState.Paused; // lo step corrente è già salvato
            card.Hide();
            highlighter.Hide();
        }

        void OnLanguageChanged()
        {
            if (running && steps != null)
                ConfigureCard(steps[index]);
        }

        void Update()
        {
            if (!running || steps == null)
                return;

            var step = steps[index];
            // L'anello nel mondo serve solo agli step di poke sulla palette (senza tasto controller);
            // per gli step con immagine del controller l'evidenziazione è sulla card.
            if (step.Hand == CoachHand.None)
            {
                var tgt = step.Target?.Invoke();
                if (tgt != null)
                    highlighter.Show(tgt, step.HighlightRect);
                else
                    highlighter.Hide();
            }
            else
            {
                highlighter.Hide();
            }

            if (step.IsComplete != null && step.IsComplete())
            {
                UiFeedback.Instance?.Press(); // feedback di avanzamento step
                Advance();
            }
        }

        // Finché non è "congelata" (welcome ancora aperto), l'ancora segue la testa così il welcome
        // è SEMPRE davanti a te (evita che parta fuori dalla visuale). Al "Sì"/avvio da menu si
        // congela (FreezeAnchorInFront) e da lì le schermate degli step restano fisse nella stanza.
        void LateUpdate()
        {
            if (roomAnchor == null || posed)
                return;
            var h = Head != null ? Head : (Camera.main != null ? Camera.main.transform : null);
            if (h == null)
                return;
            Vector3 fwd = h.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
                fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = h.position + fwd * 0.6f;
            roomAnchor.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - h.position));
        }
    }
}
