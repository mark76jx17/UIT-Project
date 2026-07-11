using System;
using TMPro;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Piccoli costruttori UI per i pannelli del tutorial (card, welcome). Isolati dal
    /// PaletteController: usano solo helper pubblici (RoundedMesh, BrushMaterials, PaletteButton)
    /// così il tutorial resta autonomo. Tutti i pannelli sono opachi (occludono il passthrough).
    /// </summary>
    public static class TutorialUi
    {
        /// <summary>Sfondo pannello arrotondato opaco.</summary>
        public static GameObject Panel(Transform parent, string name, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            float corner = Mathf.Min(0.02f, Mathf.Min(size.x, size.y) * 0.4f);
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(size.x, size.y, corner);
            go.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(color, opaque: true);
            return go;
        }

        /// <summary>Etichetta TextMeshPro con font garantito (senza, sarebbe invisibile) e
        /// auto-fit: il testo si rimpicciolisce per stare nel box. I font della UI sono in
        /// unità mondo e MOLTO piccoli (cfr. SliderLabelFont = 0.13 in PaletteController):
        /// usare valori come 1-2 rende i glifi enormi e il testo va a capo a ogni lettera.</summary>
        public static TextMeshPro Label(Transform parent, string text, Vector3 pos, Vector2 box,
            float fontSize, TextAlignmentOptions align = TextAlignmentOptions.Center, bool autoFit = true)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.color = Color.white;
            tmp.alignment = align;
            tmp.enableAutoSizing = autoFit;
            tmp.fontSize = fontSize;
            if (autoFit)
            {
                tmp.fontSizeMax = fontSize;
                tmp.fontSizeMin = fontSize * 0.35f; // si restringe per testi lunghi / lingue lunghe
            }
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.rectTransform.sizeDelta = box;
            // AddComponent può lasciare font/material non inizializzati → accedere a fontMaterial
            // lancerebbe e abortirebbe la build. Garantisco un font valido (come fa PaletteController).
            if (tmp.font == null)
                tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }

        /// <summary>Bottone premibile (poke con la punta o click del ray) su layer palette.</summary>
        public static GameObject Button(Transform parent, string name, Vector3 pos, Vector2 size,
            Color color, Action onPress)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            float corner = Mathf.Min(0.012f, Mathf.Min(size.x, size.y) * 0.4f);
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.Rect(size.x, size.y, corner);
            go.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(color, opaque: true);
            AttachButton(go, size, onPress);
            return go;
        }

        /// <summary>Quad con texture (es. bandiera); premibile se onPress != null.</summary>
        public static GameObject TexQuad(Transform parent, string name, Vector3 pos, Vector2 size,
            Texture2D tex, Action onPress)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var mat = BrushMaterials.CreateUnlit(Color.white, opaque: true);
            mat.mainTexture = tex;
            mat.SetTexture("_BaseMap", tex); // URP Unlit usa _BaseMap
            go.AddComponent<MeshRenderer>().material = mat;
            if (onPress != null)
                AttachButton(go, size, onPress);
            return go;
        }

        /// <summary>Immagine NON premibile con sfondo trasparente (es. PNG del controller):
        /// trasparente + PreserveDestAlpha così le zone vuote mostrano il pannello scuro sotto
        /// invece di "bucare" l'occlusione del passthrough.</summary>
        public static GameObject Image(Transform parent, string name, Vector3 pos, Vector2 size, Texture2D tex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().mesh = RoundedMesh.TexturedQuad(size.x, size.y);
            var mat = BrushMaterials.CreateUnlit(Color.white, opaque: false);
            mat.mainTexture = tex;
            mat.SetTexture("_BaseMap", tex);
            BrushMaterials.PreserveDestAlpha(mat);
            go.AddComponent<MeshRenderer>().material = mat;
            return go;
        }

        /// <summary>Anello opaco (evidenzia un tasto senza coprirlo).</summary>
        public static GameObject Ring(Transform parent, string name, Vector3 pos,
            float outer, float inner, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().mesh = TutorialMeshes.Ring(outer, inner);
            go.AddComponent<MeshRenderer>().material = BrushMaterials.CreateUnlit(color, opaque: true);
            return go;
        }

        // Collider trigger + Rigidbody cinematico (per generare gli OnTriggerEnter) + PaletteButton.
        static void AttachButton(GameObject go, Vector2 size, Action onPress)
        {
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(size.x, size.y, 0.02f);
            go.AddComponent<Rigidbody>().isKinematic = true;
            go.AddComponent<PaletteButton>().OnPressed = onPress;
        }

        /// <summary>Imposta il layer (palette) su tutto l'albero, così ray/poke funzionano.</summary>
        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform)
                SetLayer(c.gameObject, layer);
        }
    }
}
