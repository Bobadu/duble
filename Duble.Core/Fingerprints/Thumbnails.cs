#nullable enable
using System;
using CodeWalker.Utils;
using CodeWalker.GameFiles;

namespace Duble.Core.Fingerprints;

/// <summary>Small RGB previews of a texture, for the report and the catalog grid.</summary>
public static class Thumbnail
{
    public static byte[]? Render(Texture? t, int bok)
    {
        var px = TextureFingerprinter.DecodePixels(t, out int w, out int h);
        return px == null ? null : FromPixels(px, w, h, bok);
    }

    /// <summary>Jak Render, ale z gotowych pikseli BGRA (uzywane przy indeksowaniu — dekodujemy raz).</summary>
    public static byte[] FromPixels(byte[] px, int w, int h, int bok)
    {
        var rgba = ScaleToRgba(px, w, h, bok, bok);
        var wy = new byte[bok * bok * 3];
        for (int y = 0; y < bok; y++)
            for (int x = 0; x < bok; x++)
            {
                int o = (y * bok + x) * 4;
                double a = rgba[o + 3] / 255.0;
                // szachownica 8 px, w ciepłej szarosci — zgodna z paleta raportu
                bool jasne = ((x >> 3) + (y >> 3)) % 2 == 0;
                byte t1 = (byte)(jasne ? 0xD6 : 0xB4), t2 = (byte)(jasne ? 0xD2 : 0xB0), t3 = (byte)(jasne ? 0xCA : 0xA8);
                int c = (y * bok + x) * 3;
                wy[c] = (byte)(rgba[o] * a + t1 * (1 - a));
                wy[c + 1] = (byte)(rgba[o + 1] * a + t2 * (1 - a));
                wy[c + 2] = (byte)(rgba[o + 2] * a + t3 * (1 - a));
            }
        return wy;
    }

    /// <summary>Usrednianie po prostokatach z BGRA do RGBA (z zachowaniem alfy).</summary>

    /// <summary>Box-averaging from BGRA down to RGBA, alpha kept.</summary>
    static byte[] ScaleToRgba(byte[] px, int w, int h, int tw, int th)
    {
        var wy = new byte[tw * th * 4];
        for (int y = 0; y < th; y++)
        {
            int y0 = y * h / th, y1 = Math.Max(y0 + 1, (y + 1) * h / th);
            for (int x = 0; x < tw; x++)
            {
                int x0 = x * w / tw, x1 = Math.Max(x0 + 1, (x + 1) * w / tw);
                long r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        int o = (yy * w + xx) * 4;
                        b += px[o]; g += px[o + 1]; r += px[o + 2]; a += px[o + 3];
                        n++;
                    }
                int c = (y * tw + x) * 4;
                wy[c] = (byte)(r / n); wy[c + 1] = (byte)(g / n); wy[c + 2] = (byte)(b / n); wy[c + 3] = (byte)(a / n);
            }
        }
        return wy;
    }
}
