using System;
using TMPro;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Cartello del tutorial. Due modalità:
    /// - step con TASTO: banner grande vicino al controller, con immagine del controller
    ///   (destro/sinistro) e un ANELLO sul tasto da premere.
    /// - step di POKE sulla palette (colore/spessore): banner piccolo di solo testo, posizionato
    ///   SOPRA la palette (così non finisce dietro il pannello aperto). L'anello sta nel mondo.
    /// Sempre con "Salta step" / "Esci"; orientato verso la testa.
    /// </summary>
    public class TutorialCard : MonoBehaviour
    {
        public Action OnSkip, OnExit;

        TextMeshPro label;
        TextMeshPro btnNameLabel; // nome del tasto (Grilletto/Grip), in ambra, sotto l'immagine
        GameObject bgLarge, bgSmall, imageGO, ringGO, skipBtn, exitBtn;
        Renderer imageRend;
        Material ringMat;

        static readonly Color BgColor = new(0.12f, 0.12f, 0.14f);
        static readonly Color BtnColor = new(0.25f, 0.28f, 0.34f);
        static readonly Color RingColor = new(1f, 0.82f, 0.20f); // ambra "premi qui"
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        static readonly Vector2 ImgSlotPos = new(0f, 0.105f);
        static readonly Vector2 ImgSize = new(0.13f, 0.13f);

        public void Build()
        {
            bgLarge = TutorialUi.Panel(transform, "CardBgLarge", new Vector2(0.30f, 0.36f), BgColor);
            bgSmall = TutorialUi.Panel(transform, "CardBgSmall", new Vector2(0.30f, 0.15f), BgColor);

            imageGO = TutorialUi.Image(transform, "Ctrl",
                new Vector3(ImgSlotPos.x, ImgSlotPos.y, -0.004f), ImgSize, null);
            imageRend = imageGO.GetComponent<Renderer>();
            ringGO = TutorialUi.Ring(transform, "Ring", Vector3.zero, 0.028f, 0.020f, RingColor);
            ringMat = ringGO.GetComponent<Renderer>().material;

            // Controller (destro/sinistro) + nome del tasto, in ambra su 2 righe, tra immagine e istruzione.
            btnNameLabel = TutorialUi.Label(transform, "", new Vector3(0f, 0.0f, -0.004f),
                new Vector2(0.27f, 0.055f), 0.10f);
            btnNameLabel.color = RingColor;

            label = TutorialUi.Label(transform, "", new Vector3(0f, -0.075f, -0.004f),
                new Vector2(0.265f, 0.05f), 0.15f);

            skipBtn = TutorialUi.Button(transform, "SkipBtn", new Vector3(-0.068f, -0.135f, -0.004f),
                new Vector2(0.12f, 0.042f), BtnColor, () => OnSkip?.Invoke());
            LabelChild(skipBtn, Localization.Get("tutorial.skip"));

            exitBtn = TutorialUi.Button(transform, "ExitBtn", new Vector3(0.068f, -0.135f, -0.004f),
                new Vector2(0.12f, 0.042f), BtnColor, () => OnExit?.Invoke());
            LabelChild(exitBtn, Localization.Get("tutorial.exit"));

            TutorialUi.SetLayer(gameObject, PaletteController.PaletteLayer);
            gameObject.SetActive(false);
        }

        void LabelChild(GameObject btn, string text) =>
            TutorialUi.Label(btn.transform, text, new Vector3(0f, 0f, -0.004f),
                new Vector2(0.11f, 0.038f), 0.14f);

        /// <summary>Configura lo step: testo, immagine controller (o null) e ancora del tasto
        /// sull'immagine (x→destra, y→giù, 0..1).</summary>
        public void SetStep(string text, Texture2D controller, Vector2 buttonAnchor, bool showImage,
            string buttonName = null)
        {
            bgLarge.SetActive(showImage);
            bgSmall.SetActive(!showImage);
            imageGO.SetActive(showImage);
            ringGO.SetActive(showImage);

            bool showName = showImage && !string.IsNullOrEmpty(buttonName);
            btnNameLabel.gameObject.SetActive(showName);
            if (showName)
                btnNameLabel.text = buttonName;

            if (showImage)
            {
                imageRend.material.mainTexture = controller;
                imageRend.material.SetTexture("_BaseMap", controller);
                ringGO.transform.localPosition = new Vector3(
                    ImgSlotPos.x + (buttonAnchor.x - 0.5f) * ImgSize.x,
                    ImgSlotPos.y + (0.5f - buttonAnchor.y) * ImgSize.y,
                    -0.008f);
            }

            // Layout diverso: banner grande (con immagine) o piccolo di solo testo (font più grande).
            label.text = text;
            label.fontSize = showImage ? 0.15f : 0.20f;
            label.fontSizeMax = label.fontSize;
            label.rectTransform.sizeDelta = showImage ? new Vector2(0.265f, 0.05f) : new Vector2(0.27f, 0.06f);
            label.transform.localPosition = new Vector3(0f, showImage ? -0.075f : 0.028f, -0.004f);

            float btnY = showImage ? -0.14f : -0.045f;
            skipBtn.transform.localPosition = new Vector3(-0.068f, btnY, -0.004f);
            exitBtn.transform.localPosition = new Vector3(0.068f, btnY, -0.004f);
        }

        /// <summary>Mostra/nasconde "Salta step" (inutile sull'ultimo step). Quando è nascosto,
        /// "Esci" si centra.</summary>
        public void SetSkipVisible(bool visible)
        {
            skipBtn.SetActive(visible);
            var p = exitBtn.transform.localPosition;
            p.x = visible ? 0.068f : 0f;
            exitBtn.transform.localPosition = p;
        }

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        // La card sta ferma sull'ancora fissa (nessun following): qui solo il respiro dell'anello.
        void LateUpdate()
        {
            if (ringGO != null && ringGO.activeSelf && ringMat != null)
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 5f);
                var c = RingColor * pulse; c.a = 1f;
                ringMat.SetColor(BaseColorId, c);
            }
        }
    }
}
