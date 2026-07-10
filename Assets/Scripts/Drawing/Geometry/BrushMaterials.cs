using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Factory/cache di materiali URP per i tratti, per (colore, tipo pennello).
    /// - Round/Ribbon: Lit standard (trasparente se alpha &lt; 1).
    /// - Glow: Lit con emissione HDR — brilla davvero se nel volume post-process
    ///   c'è il Bloom, altrimenti appare auto-illuminato.
    /// - Dashed: texture a bande generata in codice, ripetuta lungo la V del
    ///   tubo (le UV mettono in V i metri percorsi: tiling 50 = un tratteggio
    ///   ogni 2 cm).
    /// </summary>
    public static class BrushMaterials
    {
        static readonly Dictionary<(Color, BrushType), Material> cache = new();
        static Texture2D dashTexture;

        // I colori arrivano da slider/ruota HSV continui: senza quantizzazione ogni
        // tinta leggermente diversa creerebbe un Material distinto mai liberato, e la
        // cache crescerebbe senza limite (un materiale per tratto). Arrotondando a 32
        // livelli per canale la cache resta proporzionale ai colori VISIBILMENTE
        // distinti usati, non al numero di tratti — differenza impercettibile a occhio.
        const float ColorLevels = 32f;

        static Color Quantize(Color c) => new(
            Mathf.Round(c.r * ColorLevels) / ColorLevels,
            Mathf.Round(c.g * ColorLevels) / ColorLevels,
            Mathf.Round(c.b * ColorLevels) / ColorLevels,
            Mathf.Round(c.a * ColorLevels) / ColorLevels);

        public static Material Get(Color color, BrushType type = BrushType.Round)
        {
            // Round e Ribbon condividono lo stesso materiale.
            if (type == BrushType.Ribbon)
                type = BrushType.Round;

            color = Quantize(color);
            if (cache.TryGetValue((color, type), out var cached))
                return cached;

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.4f);

            bool transparent = color.a < 0.999f;

            switch (type)
            {
                case BrushType.Glow:
                    material.EnableKeyword("_EMISSION");
                    // Su Quest URP non c'è GI realtime: marcare l'emissione come
                    // RealtimeEmissive non serve a nulla (e accenderebbe un percorso GI
                    // inutile). L'effetto "brilla" arriva dal Bloom nel Volume
                    // post-process; qui basta l'emissione HDR sul materiale.
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    material.SetColor("_EmissionColor", (Color)(new Vector4(color.r, color.g, color.b, 1f) * 2.5f));
                    break;
                case BrushType.Dashed:
                    material.SetTexture("_BaseMap", DashTexture());
                    material.SetTextureScale("_BaseMap", new Vector2(1f, 50f));
                    transparent = true; // i vuoti del tratteggio sono alpha 0
                    break;
            }

            if (transparent)
                MakeTransparent(material);

            cache[(color, type)] = material;
            return material;
        }

        /// <summary>
        /// Svuota la cache e distrugge i materiali generati. Chiamato all'avvio del
        /// rig: in editor con il "Reload Domain" disattivato i campi static
        /// sopravvivono tra le sessioni di Play e conserverebbero riferimenti a
        /// materiali ormai distrutti. Sul device gira una volta all'avvio (innocuo).
        /// </summary>
        public static void ClearCache()
        {
            foreach (var material in cache.Values)
                if (material != null)
                    Object.Destroy(material);
            cache.Clear();
        }

        /// <summary>
        /// Materiale Unlit non in cache (istanza dedicata, es. cursore del
        /// pennello e pannelli della palette). Di default trasparente (così l'alpha
        /// si può cambiare a runtime); con <paramref name="opaque"/> true è opaco con
        /// ZWrite — usato per sfondi/controlli della palette così sul passthrough MR
        /// non si vede "attraverso" e il sorting è risolto dal depth invece che dalle
        /// render queue trasparenti.
        /// </summary>
        public static Material CreateUnlit(Color color, bool opaque = false)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            if (opaque)
                MakeOpaque(material);
            else
                MakeTransparent(material);
            return material;
        }

        static void MakeOpaque(Material material)
        {
            material.SetFloat("_Surface", 0f); // 0 = Opaque
            material.SetOverrideTag("RenderType", "Opaque");
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Geometry;
            // Doppia faccia: i pannelli sono quad a faccia singola, quindi da DIETRO (palette
            // fissata nella stanza, ci si gira attorno) sarebbero invisibili → si vedrebbe
            // attraverso. Le facce posteriori scrivono comunque alpha = 1, quindi occludono il
            // passthrough: la palette resta solida da ogni lato.
            material.SetFloat("_Cull", (float)CullMode.Off);
            // Sul passthrough Quest è il canale ALPHA del framebuffer a decidere cosa
            // nasconde il mondo reale: un materiale opaco deve scrivere alpha = 1. Forzo
            // l'alpha del colore a 1 (es. PanelColor è 0.96 → lasciava trasparire ~4% del
            // passthrough, invisibile in editor ma visibile in VR).
            var c = material.GetColor("_BaseColor");
            c.a = 1f;
            material.SetColor("_BaseColor", c);
        }

        /// <summary>
        /// Per icone/anteprime trasparenti disegnate SOPRA un controllo opaco: il colore si
        /// fonde normalmente, ma l'alpha del framebuffer viene PRESERVATO (blend alpha Zero/One)
        /// invece di essere sovrascritto. Senza questo, le zone trasparenti dell'icona scrivono
        /// alpha 0 e "bucano" l'occlusione del passthrough Quest (see-through) anche se il
        /// bottone sotto è opaco.
        /// </summary>
        public static void PreserveDestAlpha(Material material)
        {
            material.SetInt("_SrcBlendAlpha", (int)BlendMode.Zero);
            material.SetInt("_DstBlendAlpha", (int)BlendMode.One);
        }

        /// <summary>
        /// Per glifi/icone trasparenti che FLUTTANO liberi (es. l'icona sulla punta del
        /// pennello), non appoggiati su un controllo opaco: il glifo deve OCCLUDERE il
        /// passthrough dove è pieno (scrivere alpha), ma lo sfondo trasparente deve
        /// PRESERVARE l'alpha di ciò che sta dietro (palette/disegno) invece di sovrascriverlo
        /// a 0 e "bucare" l'occlusione. Blend alpha "over": SrcAlpha=One, DstAlpha=OneMinusSrcAlpha
        /// → alpha_out = alpha_glifo + alpha_dietro·(1 − alpha_glifo). Così sopra il vuoto il
        /// glifo occlude (alpha 1) e lo sfondo lascia il passthrough; sopra la palette/disegno lo
        /// sfondo lascia intatto l'alpha=1 di dietro (si vede la palette, non un buco).
        /// </summary>
        public static void CompositeAlphaOver(Material material)
        {
            material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
        }

        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f); // 1 = Transparent
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        // Una colonna di pixel: metà opachi, metà trasparenti, con bordo morbido.
        static Texture2D DashTexture()
        {
            if (dashTexture != null)
                return dashTexture;

            const int size = 64;
            dashTexture = new Texture2D(2, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
            };
            var pixels = new Color[2 * size];
            for (int y = 0; y < size; y++)
            {
                float t = (float)y / size;
                // Banda opaca da 0.05 a 0.55, sfumata ai bordi.
                float a = Mathf.Clamp01((0.5f - Mathf.Abs(t - 0.3f) * 2f) * 6f);
                var color = new Color(1f, 1f, 1f, a);
                pixels[y * 2] = color;
                pixels[y * 2 + 1] = color;
            }
            dashTexture.SetPixels(pixels);
            dashTexture.Apply();
            return dashTexture;
        }
    }
}
