// Tekstury.cs — dekodowanie pikseli tekstury: CodeWalker (BC1-BC5, nieskompresowane) + BCnEncoder.Net (BC7).
//
// CodeWalker.DDSIO.GetPixels ma `case BC7: //TODO` i zwraca null; u nas BC7 to ~5 % tekstur, ktore
// dotad nie mialy odcisku ani podgladu. BC7 dekodujemy z surowych blokow (16 B na blok 4x4) lezacych
// w Texture.Data.FullData kolejno od mipa 0. Wynik zawsze BGRA (jak DDSIO), zeby reszta nie rozroznia.
using System;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace Duble.Core.Formats;

public static class Tekstury
{
    static readonly BcDecoder Bc = new();

    /// <summary>Piksele BGRA danego mipa; null gdy nie da sie zdekodowac.</summary>
    public static byte[] Piksele(Texture t, int mip, out int w, out int h)
    {
        w = h = 0;
        if (t == null || t.Width <= 0 || t.Height <= 0) return null;
        mip = Math.Clamp(mip, 0, Math.Max(0, t.Levels - 1));
        w = Math.Max(1, t.Width >> mip); h = Math.Max(1, t.Height >> mip);
        byte[] px = null;
        try { px = DDSIO.GetPixels(t, mip); } catch { px = null; }
        if (px != null && px.Length == w * h * 4) return px;
        if (t.Format == TextureFormat.D3DFMT_BC7) return Bc7(t, mip, w, h);
        return null;
    }

    /// <summary>PNG RGBA z najwiekszego mipa o boku &lt;= maksBok (podglad w aplikacji, tekstura do GLB); null gdy nie do zdekodowania.</summary>
    public static byte[] PngRgba(Texture t, int maksBok = 1024)
    {
        if (t == null || t.Width <= 0 || t.Height <= 0) return null;
        int mip = 0;
        while ((t.Width >> mip) > maksBok && (t.Height >> mip) > maksBok && mip < t.Levels - 1) mip++;
        var px = Piksele(t, mip, out int w, out int h);
        if (px == null) return null;
        var rgba = new byte[px.Length];
        for (int i = 0; i < px.Length; i += 4) { rgba[i] = px[i + 2]; rgba[i + 1] = px[i + 1]; rgba[i + 2] = px[i]; rgba[i + 3] = px[i + 3]; }
        return Png.Rgba(rgba, w, h);
    }

    static byte[] Bc7(Texture t, int mip, int w, int h)
    {
        var dane = t.Data?.FullData;
        if (dane == null) return null;
        long off = 0;
        for (int m = 0; m < mip; m++)
        {
            int mw = Math.Max(1, t.Width >> m), mh = Math.Max(1, t.Height >> m);
            off += (long)((mw + 3) / 4) * ((mh + 3) / 4) * 16;
        }
        int dl = ((w + 3) / 4) * ((h + 3) / 4) * 16;
        if (off + dl > dane.Length) return null;
        var blok = new byte[dl];
        Buffer.BlockCopy(dane, (int)off, blok, 0, dl);
        ColorRgba32[] kol;
        try { kol = Bc.DecodeRaw(blok, w, h, CompressionFormat.Bc7); } catch { return null; }
        if (kol == null || kol.Length < w * h) return null;
        var wy = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            wy[i * 4] = kol[i].b; wy[i * 4 + 1] = kol[i].g; wy[i * 4 + 2] = kol[i].r; wy[i * 4 + 3] = kol[i].a;
        }
        return wy;
    }
}
