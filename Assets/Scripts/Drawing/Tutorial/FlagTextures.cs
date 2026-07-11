using UnityEngine;

namespace MixedRealityProject.Drawing
{
    /// <summary>
    /// Bandiere generate in codice (nessun asset esterno), coerenti con lo stile procedurale
    /// delle altre texture dell'app. Cache statica: una Texture2D per bandiera.
    /// </summary>
    public static class FlagTextures
    {
        const int W = 300, H = 200;
        const int SS = 3; // super-sampling per lato (anti-aliasing delle diagonali dell'UK)
        static Texture2D italy, uk;

        public static Texture2D Italy()
        {
            if (italy != null) return italy;
            italy = New();
            var green = new Color(0f, 0.56f, 0.27f);
            var white = Color.white;
            var red = new Color(0.81f, 0.13f, 0.16f);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = x < W / 3 ? green : x < 2 * W / 3 ? white : red;
                    px[y * W + x] = c;
                }
            italy.SetPixels(px);
            italy.Apply();
            return italy;
        }

        public static Texture2D UnitedKingdom()
        {
            if (uk != null) return uk;
            uk = New();
            var px = new Color[W * H];
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
            float k = (float)H / W;      // orienta le diagonali (croce di Sant'Andrea)
            float s = W / 96f;           // spessori tarati su base 96px, scalati alla risoluzione
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // Super-sampling: media di SS×SS sotto-campioni → bordi diagonali morbidi.
                    Color acc = default;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float fx = x + (sx + 0.5f) / SS - 0.5f - cx;
                            float fy = y + (sy + 0.5f) / SS - 0.5f - cy;
                            acc += UkAt(fx, fy, k, s);
                        }
                    px[y * W + x] = acc / (SS * SS);
                }
            uk.SetPixels(px);
            uk.Apply();
            return uk;
        }

        // Colore dell'Union Jack in un punto continuo (coord. centrate). Diagonali (bianco+rosso)
        // sotto la croce di San Giorgio (bianco+rosso), su campo blu.
        static Color UkAt(float dx, float dy, float k, float s)
        {
            var blue = new Color(0.0f, 0.14f, 0.45f);
            var white = Color.white;
            var red = new Color(0.80f, 0.10f, 0.18f);
            Color c = blue;
            float diag = Mathf.Min(Mathf.Abs(dy - k * dx), Mathf.Abs(dy + k * dx));
            if (diag < 9f * s) c = white;
            if (diag < 4f * s) c = red;
            if (Mathf.Abs(dx) < 10f * s || Mathf.Abs(dy) < 10f * s) c = white;
            if (Mathf.Abs(dx) < 5f * s || Mathf.Abs(dy) < 5f * s) c = red;
            return c;
        }

        static Texture2D New() => new(W, H, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
    }
}
