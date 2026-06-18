using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Genera (e mette in cache) una piccola texture-anteprima per ogni tipo di
    /// pennello: un campione di tratto orizzontale che mostra com'è davvero il
    /// tratto (tubo pieno, nastro piatto, linea glow, tratteggio). Molto più
    /// informativo di un'icona astratta, e niente problemi di sfondo/import dei PNG.
    /// Tratti bianchi su sfondo trasparente: si leggono bene sul chip scuro del bottone.
    /// </summary>
    public static class BrushPreview
    {
        static readonly Dictionary<BrushType, Texture2D> cache = new();

        public static Texture2D Get(BrushType type)
        {
            if (cache.TryGetValue(type, out var tex))
                return tex;

            const int w = 96, h = 56;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];

            float cy = (h - 1) * 0.5f;
            float marginX = w * 0.14f;

            for (int y = 0; y < h; y++)
            {
                float dy = (y - cy) / (h * 0.5f); // -1..1
                for (int x = 0; x < w; x++)
                {
                    float fx = Mathf.InverseLerp(marginX, w - marginX, x); // 0..1 lungo il tratto
                    bool inX = x >= marginX && x <= w - marginX;
                    Color c = new(1f, 1f, 1f, 0f);

                    switch (type)
                    {
                        case BrushType.Round: // tubo: profilo tondo, centro più chiaro
                        {
                            float t = Mathf.Abs(dy) / 0.62f;
                            float a = inX ? Mathf.Clamp01(1.15f - t * t) : 0f;
                            float shade = 0.82f + 0.18f * Mathf.Clamp01(1f - (dy + 0.5f)); // luce in alto
                            c = new Color(shade, shade, shade, a);
                            break;
                        }
                        case BrushType.Ribbon: // nastro piatto: banda netta sottile
                        {
                            float a = inX && Mathf.Abs(dy) < 0.34f ? 1f : 0f;
                            // bordo morbido di 1px
                            a *= Mathf.Clamp01((0.34f - Mathf.Abs(dy)) * h);
                            c = new Color(0.9f, 0.9f, 0.95f, a);
                            break;
                        }
                        case BrushType.Glow: // core sottile + alone ampio
                        {
                            float core = Mathf.Exp(-Mathf.Pow(dy / 0.16f, 2f));
                            float halo = Mathf.Exp(-Mathf.Pow(dy / 0.7f, 2f));
                            float a = inX ? Mathf.Clamp01(core + halo * 0.5f) : 0f;
                            c = new Color(1f, 0.98f, 0.85f, a);
                            break;
                        }
                        case BrushType.Dashed: // tubo tratteggiato lungo X
                        {
                            float t = Mathf.Abs(dy) / 0.5f;
                            float a = inX ? Mathf.Clamp01(1f - t * t) : 0f;
                            float dash = Mathf.Repeat(fx * 5f, 1f) < 0.55f ? 1f : 0f; // 5 trattini
                            c = new Color(1f, 1f, 1f, a * dash);
                            break;
                        }
                    }
                    px[y * w + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            cache[type] = tex;
            return tex;
        }
    }
}
