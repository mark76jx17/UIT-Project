using System.Collections.Generic;
using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Genera (e mette in cache) una piccola texture-anteprima per ogni tipo di
    /// pennello, in stile Gravity Sketch: un breve **tratto curvo** (swoosh) che mostra
    /// com'è davvero il tratto — tubo pieno e tondo (stroke), nastro piatto con torsione
    /// (ribbon), tubo spezzato in trattini (dashed). Più leggibile di un campione dritto.
    /// Bianco/grigio su trasparente: si stacca bene sul chip scuro del bottone.
    /// </summary>
    public static class BrushPreview
    {
        static readonly Dictionary<BrushType, Texture2D> cache = new();

        public static Texture2D Get(BrushType type)
        {
            if (cache.TryGetValue(type, out var tex))
                return tex;

            const int w = 128, h = 72;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];

            // Curva centrale: swoosh diagonale con estremi addolciti (sin), da sx-basso a
            // dx-alto. La campiono in pochi punti e per ogni pixel uso la distanza al
            // campione più vicino → estremità arrotondate "gratis".
            const int Samples = 64;
            float marginX = w * 0.16f;
            float midY = (h - 1) * 0.5f;
            float amp = h * 0.26f;
            var cx = new float[Samples];
            var cy = new float[Samples];
            for (int s = 0; s < Samples; s++)
            {
                float ts = s / (float)(Samples - 1);
                cx[s] = Mathf.Lerp(marginX, w - marginX, ts);
                cy[s] = midY + amp * Mathf.Sin(Mathf.PI * (ts - 0.5f));
            }

            float tubeR = h * 0.20f;   // raggio del tubo (stroke/dashed)
            float ribbonR = h * 0.17f; // semi-larghezza massima del nastro

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Campione più vicino: distanza + parametro t lungo la curva.
                    float best = float.MaxValue;
                    int bi = 0;
                    for (int s = 0; s < Samples; s++)
                    {
                        float ex = x - cx[s], ey = y - cy[s];
                        float dd = ex * ex + ey * ey;
                        if (dd < best) { best = dd; bi = s; }
                    }
                    float dist = Mathf.Sqrt(best);
                    float t = bi / (float)(Samples - 1);
                    float side = y - cy[bi]; // >0 = sopra la curva (luce dall'alto)

                    Color c = new(1f, 1f, 1f, 0f);
                    switch (type)
                    {
                        case BrushType.Round: // tubo pieno, tondo, con volume
                        {
                            float a = Mathf.Clamp01((tubeR - dist) / 1.5f);
                            float up = Mathf.Clamp01(0.5f + side / (tubeR * 2f));
                            float shade = 0.78f + 0.22f * up; // più chiaro in alto
                            c = new Color(shade, shade, shade, a);
                            break;
                        }
                        case BrushType.Ribbon: // nastro piatto con torsione
                        {
                            // La semi-larghezza si stringe a metà curva → sembra girare di taglio.
                            float twist = 0.32f + 0.68f * Mathf.Abs(Mathf.Cos(Mathf.PI * (t - 0.5f)));
                            float halfW = ribbonR * twist;
                            float a = Mathf.Clamp01((halfW - dist) / 1.2f);
                            float grad = Mathf.Clamp01(0.5f + side / (halfW * 2.4f));
                            float shade = 0.80f + 0.15f * grad; // piatto, lieve gradiente
                            c = new Color(shade, shade, 0.97f, a);
                            break;
                        }
                        case BrushType.Dashed: // tubo spezzato in trattini lungo la curva
                        {
                            float a = Mathf.Clamp01((tubeR * 0.92f - dist) / 1.5f);
                            float dash = Mathf.Repeat(t * 6f, 1f) < 0.55f ? 1f : 0f;
                            c = new Color(1f, 1f, 1f, a * dash);
                            break;
                        }
                        case BrushType.Glow: // core sottile + alone (resta, anche se nascosto)
                        {
                            float core = Mathf.Exp(-Mathf.Pow(dist / (tubeR * 0.5f), 2f));
                            float halo = Mathf.Exp(-Mathf.Pow(dist / (tubeR * 1.8f), 2f));
                            c = new Color(1f, 0.98f, 0.85f, Mathf.Clamp01(core + halo * 0.5f));
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
