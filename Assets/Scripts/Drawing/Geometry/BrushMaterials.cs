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

        public static Material Get(Color color, BrushType type = BrushType.Round)
        {
            // Round e Ribbon condividono lo stesso materiale.
            if (type == BrushType.Ribbon)
                type = BrushType.Round;

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
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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
            // Sul passthrough Quest è il canale ALPHA del framebuffer a decidere cosa
            // nasconde il mondo reale: un materiale opaco deve scrivere alpha = 1. Forzo
            // l'alpha del colore a 1 (es. PanelColor è 0.96 → lasciava trasparire ~4% del
            // passthrough, invisibile in editor ma visibile in VR).
            var c = material.GetColor("_BaseColor");
            c.a = 1f;
            material.SetColor("_BaseColor", c);
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
