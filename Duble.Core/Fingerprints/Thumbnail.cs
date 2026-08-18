using System;
using CodeWalker.GameFiles;

namespace Duble.Core.Fingerprints;

/// <summary>Small RGB previews of a texture, for the report and the catalog grid.</summary>
public static class Thumbnail
{
    /// <summary>Decodes the texture and renders a square preview of the given side; null when it will not decode.</summary>
    public static byte[]? Render(Texture? texture, int side)
    {
        var pixels = TextureFingerprinter.DecodePixels(texture, out int width, out int height);
        return pixels == null ? null : FromPixels(pixels, width, height, side);
    }

    /// <summary>
    /// The same preview from pixels already decoded — indexing decodes once and fingerprints and renders from
    /// that one pass.
    ///
    /// ALPHA IS COMPOSITED ONTO A CHEQUERBOARD. Without it half the clothing textures come out black: a
    /// clothing atlas has large transparent areas, and what lies under them is usually black. Fingerprints go
    /// on being computed from the raw RGB, which is what they were calibrated on; the compositing is for the
    /// preview and nothing else.
    /// </summary>
    public static byte[] FromPixels(byte[] pixels, int width, int height, int side)
    {
        var rgba = ScaleToRgba(pixels, width, height, side, side);
        var output = new byte[side * side * 3];

        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int source = (y * side + x) * 4;
                double alpha = rgba[source + 3] / 255.0;

                // an 8 px chequerboard in a warm grey, matching the report's palette
                bool light = ((x >> 3) + (y >> 3)) % 2 == 0;
                byte backR = (byte)(light ? 0xD6 : 0xB4);
                byte backG = (byte)(light ? 0xD2 : 0xB0);
                byte backB = (byte)(light ? 0xCA : 0xA8);

                int target = (y * side + x) * 3;
                output[target] = (byte)(rgba[source] * alpha + backR * (1 - alpha));
                output[target + 1] = (byte)(rgba[source + 1] * alpha + backG * (1 - alpha));
                output[target + 2] = (byte)(rgba[source + 2] * alpha + backB * (1 - alpha));
            }

        return output;
    }

    /// <summary>Box-averaging from BGRA down to RGBA, alpha kept.</summary>
    static byte[] ScaleToRgba(byte[] pixels, int width, int height, int targetWidth, int targetHeight)
    {
        var output = new byte[targetWidth * targetHeight * 4];

        for (int y = 0; y < targetHeight; y++)
        {
            int fromY = y * height / targetHeight, toY = Math.Max(fromY + 1, (y + 1) * height / targetHeight);
            for (int x = 0; x < targetWidth; x++)
            {
                int fromX = x * width / targetWidth, toX = Math.Max(fromX + 1, (x + 1) * width / targetWidth);

                long r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int sy = fromY; sy < toY; sy++)
                    for (int sx = fromX; sx < toX; sx++)
                    {
                        int source = (sy * width + sx) * 4;
                        b += pixels[source];
                        g += pixels[source + 1];
                        r += pixels[source + 2];
                        a += pixels[source + 3];
                        count++;
                    }

                int target = (y * targetWidth + x) * 4;
                output[target] = (byte)(r / count);
                output[target + 1] = (byte)(g / count);
                output[target + 2] = (byte)(b / count);
                output[target + 3] = (byte)(a / count);
            }
        }

        return output;
    }
}
