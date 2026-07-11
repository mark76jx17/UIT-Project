using System;
using TMPro;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Schermata di benvenuto al primo avvio: due bandiere (Italia/UK) per scegliere la lingua,
    /// titolo "Vuoi fare il tutorial?" e bottoni Sì/No. Le bandiere cambiano Localization.Current
    /// e aggiornano i testi in tempo reale. Sta ferma sull'ancora fissa (nessun following).
    /// </summary>
    public class TutorialWelcomePanel : MonoBehaviour
    {
        TextMeshPro title, yesLabel, noLabel;

        static readonly Color BgColor = new(0.12f, 0.12f, 0.14f);
        static readonly Color YesColor = new(0.20f, 0.42f, 0.28f);
        static readonly Color NoColor = new(0.42f, 0.24f, 0.26f);

        public void Build(Action onYes, Action onNo)
        {
            var size = new Vector2(0.34f, 0.22f);
            TutorialUi.Panel(transform, "WelcomeBg", size, BgColor);

            // Bandiere (scelta lingua).
            var flagSize = new Vector2(0.10f, 0.066f);
            TutorialUi.TexQuad(transform, "FlagIt", new Vector3(-0.08f, 0.055f, -0.004f),
                flagSize, FlagTextures.Italy(), () => SetLanguage("it"));
            TutorialUi.TexQuad(transform, "FlagEn", new Vector3(0.08f, 0.055f, -0.004f),
                flagSize, FlagTextures.UnitedKingdom(), () => SetLanguage("en"));

            // Titolo.
            title = TutorialUi.Label(transform, Localization.Get("tutorial.welcome.title"),
                new Vector3(0f, -0.02f, -0.004f), new Vector2(size.x - 0.03f, 0.055f), 0.15f);

            // Sì / No.
            TutorialUi.Button(transform, "YesBtn", new Vector3(-0.07f, -0.075f, -0.004f),
                new Vector2(0.12f, 0.04f), YesColor, () => onYes?.Invoke());
            yesLabel = ChildLabel("YesBtn", Localization.Get("confirm.yes"));

            TutorialUi.Button(transform, "NoBtn", new Vector3(0.07f, -0.075f, -0.004f),
                new Vector2(0.12f, 0.04f), NoColor, () => onNo?.Invoke());
            noLabel = ChildLabel("NoBtn", Localization.Get("confirm.no"));

            TutorialUi.SetLayer(gameObject, PaletteController.PaletteLayer);
            gameObject.SetActive(false);
        }

        TextMeshPro ChildLabel(string btnName, string text)
        {
            var btn = transform.Find(btnName);
            return btn == null ? null
                : TutorialUi.Label(btn, text, new Vector3(0f, 0f, -0.004f),
                    new Vector2(0.11f, 0.036f), 0.12f);
        }

        void SetLanguage(string code)
        {
            Localization.Current = code;
            RefreshText();
        }

        /// <summary>Riaggiorna i testi nella lingua corrente (dopo un cambio bandiera).</summary>
        public void RefreshText()
        {
            if (title != null) title.text = Localization.Get("tutorial.welcome.title");
            if (yesLabel != null) yesLabel.text = Localization.Get("confirm.yes");
            if (noLabel != null) noLabel.text = Localization.Get("confirm.no");
        }

        public void Show() => gameObject.SetActive(true);

        public void Hide() => gameObject.SetActive(false);
    }
}
