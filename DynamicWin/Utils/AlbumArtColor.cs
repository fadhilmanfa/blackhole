using SkiaSharp;
using System;

namespace DynamicWin.Utils
{
    /// <summary>
    /// Derives a small two-colour palette from album art so UI accents
    /// (e.g. the equalizer) can follow the artwork instead of the theme.
    /// </summary>
    public static class AlbumArtColor
    {
        const int HueBins = 24;
        const int SampleGrid = 32;

        /// <summary>
        /// Picks the dominant vivid hue of the artwork and returns a bright
        /// primary plus a dimmed secondary for gradients. Returns null when
        /// there is no usable bitmap.
        /// </summary>
        public static (Col primary, Col secondary)? Extract(SKBitmap? art)
        {
            if (art == null || art.Width <= 0 || art.Height <= 0) return null;

            try
            {
                int stepX = Math.Max(1, art.Width / SampleGrid);
                int stepY = Math.Max(1, art.Height / SampleGrid);

                var weight = new float[HueBins];
                var sumR = new float[HueBins];
                var sumG = new float[HueBins];
                var sumB = new float[HueBins];
                var sumSat = new float[HueBins];

                int samples = 0;

                for (int y = 0; y < art.Height; y += stepY)
                {
                    for (int x = 0; x < art.Width; x += stepX)
                    {
                        var px = art.GetPixel(x, y);
                        if (px.Alpha < 128) continue;

                        float r = px.Red / 255f;
                        float g = px.Green / 255f;
                        float b = px.Blue / 255f;

                        ToHsv(r, g, b, out float h, out float s, out float v);

                        // Vivid, mid-bright pixels carry the identity of a
                        // cover; near-black areas only barely contribute.
                        float pxWeight = (0.05f + s * s * 1.8f) * (0.25f + v);
                        if (v < 0.06f) pxWeight *= 0.2f;

                        int bin = (int)(h / 360f * HueBins) % HueBins;

                        weight[bin] += pxWeight;
                        sumR[bin] += r * pxWeight;
                        sumG[bin] += g * pxWeight;
                        sumB[bin] += b * pxWeight;
                        sumSat[bin] += s * pxWeight;

                        samples++;
                    }
                }

                if (samples == 0) return null;

                int best = 0;
                for (int i = 1; i < HueBins; i++)
                    if (weight[i] > weight[best]) best = i;

                float total = weight[best];
                if (total <= 0f) return null;

                float avgR = sumR[best] / total;
                float avgG = sumG[best] / total;
                float avgB = sumB[best] / total;
                float avgSat = sumSat[best] / total;

                ToHsv(avgR, avgG, avgB, out float hue, out float sat, out float val);

                // Grayscale artwork: a neutral silver reads better than an
                // arbitrary hue picked from compression noise.
                if (sat < 0.12f || avgSat < 0.14f)
                {
                    float lum = Math.Clamp(val, 0.8f, 1f);
                    var neutralPrimary = new Col(lum, lum, lum);
                    var neutralSecondary = new Col(lum * 0.6f, lum * 0.6f, lum * 0.6f, 0.5f);
                    return (neutralPrimary, neutralSecondary);
                }

                // Normalise so dark or washed-out covers still produce bars
                // that stay legible on the black island.
                float outSat = Math.Clamp(sat, 0.45f, 1f);
                float outVal = Math.Clamp(val, 0.78f, 1f);

                var primary = FromHsv(hue, outSat, outVal);

                float secSat = Math.Clamp(outSat * 0.8f, 0.3f, 1f);
                float secVal = Math.Clamp(outVal * 0.42f, 0.18f, 0.6f);
                var secondary = FromHsv(hue, secSat, secVal).Override(a: 0.5f);

                return (primary, secondary);
            }
            catch
            {
                return null;
            }
        }

        static void ToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;

            v = max;
            s = max <= 0f ? 0f : delta / max;

            if (delta <= 0f)
            {
                h = 0f;
                return;
            }

            if (max == r) h = 60f * (((g - b) / delta) % 6f);
            else if (max == g) h = 60f * ((b - r) / delta + 2f);
            else h = 60f * ((r - g) / delta + 4f);

            if (h < 0f) h += 360f;
        }

        static Col FromHsv(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            float m = v - c;

            float r, g, b;

            if (h < 60f) { r = c; g = x; b = 0f; }
            else if (h < 120f) { r = x; g = c; b = 0f; }
            else if (h < 180f) { r = 0f; g = c; b = x; }
            else if (h < 240f) { r = 0f; g = x; b = c; }
            else if (h < 300f) { r = x; g = 0f; b = c; }
            else { r = c; g = 0f; b = x; }

            return new Col(r + m, g + m, b + m);
        }
    }
}
