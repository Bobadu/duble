#nullable enable
using System;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using Duble.Core.Formats;
using Duble.Core.Model;
using Duble.Core.Results;

namespace Duble.Core.Fingerprints;

/// <summary>What the caller wants doing with the decoded pixels besides fingerprinting them.</summary>
/// <param name="Side">Side of the thumbnail in pixels.</param>
/// <param name="OnPixels">Called with the decoded BGRA pixels, so a thumbnail can be written without decoding twice.</param>
public sealed record ThumbnailRequest(int Side, Action<byte[], int, int> OnPixels);

/// <summary>Turns a texture file into its fingerprint: perceptual hash, colour signature, alpha share.</summary>
public interface ITextureFingerprinter
{
    /// <summary>
    /// The fingerprint of the first texture in a .ytd. A file that cannot be read at all comes back as
    /// texture.undecodable; a texture whose pixels will not decode gives a fingerprint with IsDecoded false,
    /// which the comparison knows how to treat.
    /// </summary>
    Result<TextureInfo> Compute(byte[] textureBytes, ThumbnailRequest? thumbnail = null);
}

/// <inheritdoc />
public sealed class TextureFingerprinter : ITextureFingerprinter
{
    public TextureFingerprinter(CodeWalkerRuntime runtime) => _ = runtime;

    public Result<TextureInfo> Compute(byte[] textureBytes, ThumbnailRequest? thumbnail = null)
    {
        Texture? texture;
        try
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, textureBytes, 13);
            texture = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
        }
        catch (Exception e)
        {
            return Result<TextureInfo>.Fail(ErrorCodes.TextureUndecodable, e.Message);
        }

        if (texture == null)
            return Result<TextureInfo>.Fail(ErrorCodes.TextureUndecodable, "no texture in the dictionary");

        var info = new TextureInfo();
        Fill(texture, info, thumbnail?.Side ?? 128, thumbnail?.OnPixels);
        return Result<TextureInfo>.Ok(info);
    }

    static readonly double[,] Cos = BuildCosineTable();
    const int NPh = 64;    // bok obrazu wejsciowego DCT
    const int KPh = 16;    // bok bloku wspolczynnikow (16x16 = 256 bitow)
    const int ColorGridSide = 8;   // siatka sygnatury koloru (8x8 x RGB = 192 bajty)

    static double[,] BuildCosineTable()
    {
        var t = new double[KPh, NPh];
        for (int u = 0; u < KPh; u++)
            for (int x = 0; x < NPh; x++)
                t[u, x] = Math.Cos((2.0 * x + 1.0) * u * Math.PI / (2.0 * NPh));
        return t;
    }

    /// <summary>Krotka nazwa formatu do raportu.</summary>
    public static string FormatName(Texture t)
    {
        var f = t.Format.ToString();
        return f switch
        {
            "D3DFMT_DXT1" => "BC1",
            "D3DFMT_DXT3" => "BC2",
            "D3DFMT_DXT5" => "BC3",
            "D3DFMT_ATI1" => "BC4",
            "D3DFMT_ATI2" => "BC5",
            "D3DFMT_BC7" => "BC7",
            "D3DFMT_A8R8G8B8" => "RGBA8",
            "D3DFMT_A8B8G8R8" => "RGBA8",
            _ => f.Replace("D3DFMT_", "")
        };
    }

    /// <summary>
    /// Dekoduje teksture i wypelnia odcisk: PHash, sygnature koloru, udzial alfy.
    /// Zwraca piksele RGB miniatury (do raportu) albo null, gdy sie nie udalo.
    /// </summary>
    static byte[]? Fill(Texture t, TextureInfo wy, int bokMiniatury, Action<byte[], int, int>? piksele)
    {
        wy.Name = t.Name;
        wy.Width = t.Width;
        wy.Height = t.Height;
        wy.MipLevels = t.Levels;
        wy.Format = FormatName(t);
        wy.IsDecoded = false;

        var px = DecodePixels(t, out int w, out int h);
        if (px == null) return null;
        piksele?.Invoke(px, w, h);   // np. miniatura do cache przy indeksowaniu (bez drugiego dekodowania)

        // --- PHash: 64x64 w skali szarosci -> DCT -> blok 16x16 -> mediana ---
        // DCT liczymy ROZDZIELNIE (najpierw wiersze, potem kolumny): 82 tys. mnozen
        // zamiast miliona przy wersji naiwnej, przy identycznym wyniku.
        var szare = ScaleToGrey(px, w, h, NPh, NPh);

        double sr = 0;
        foreach (var s0 in szare) sr += s0;
        sr /= szare.Length;
        double war = 0;
        foreach (var s0 in szare) war += (s0 - sr) * (s0 - sr);
        wy.Variance = (float)Math.Sqrt(war / szare.Length);

        var posrednie = new double[NPh * KPh];         // [x, v]
        for (int x = 0; x < NPh; x++)
        {
            int wiersz = x * NPh;
            for (int v = 0; v < KPh; v++)
            {
                double s = 0;
                for (int y = 0; y < NPh; y++) s += szare[wiersz + y] * Cos[v, y];
                posrednie[x * KPh + v] = s;
            }
        }
        var wsp = new double[KPh * KPh];
        for (int u = 0; u < KPh; u++)
            for (int v = 0; v < KPh; v++)
            {
                double s = 0;
                for (int x = 0; x < NPh; x++) s += posrednie[x * KPh + v] * Cos[u, x];
                wsp[u * KPh + v] = s;
            }

        // mediana liczona BEZ skladowej stalej (0,0) — inaczej zdominowalaby prog
        var bezDc = wsp.Skip(1).OrderBy(x => x).ToArray();
        double mediana = (bezDc[bezDc.Length / 2 - 1] + bezDc[bezDc.Length / 2]) / 2.0;
        var hash = new ulong[4];
        for (int i = 0; i < 256; i++) if (wsp[i] > mediana) hash[i >> 6] |= 1UL << (i & 63);
        wy.PerceptualHash = hash;

        // --- sygnatura koloru 8x8 RGB ---
        var maly = ScaleToRgb(px, w, h, ColorGridSide, ColorGridSide);
        wy.ColorSignature = Convert.ToBase64String(maly);

        // --- udzial pikseli z alfa ---
        int zAlfa = 0, wszystkie = w * h;
        for (int i = 3; i < px.Length; i += 4) if (px[i] < 250) zAlfa++;
        wy.AlphaShare = wszystkie > 0 ? (float)zAlfa / wszystkie : 0f;

        wy.IsDecoded = true;
        return ScaleToRgb(px, w, h, bokMiniatury, bokMiniatury);
    }

    /// <summary>
    /// Render do raportu: RGB z ALFA ZLOZONA NA SZACHOWNICE.
    ///
    /// Bez tego polowa tekstur ubran wychodzi czarna — atlas ma wielkie obszary
    /// przezroczyste, a pod nimi zwykle leza czarne piksele. Odciski licza sie dalej
    /// z surowego RGB (sa juz skalibrowane), skladanie dotyczy WYLACZNIE podgladu.
    /// </summary>
    /// <summary>Pixels of the largest mip at least 128 px on a side, stepping up on a decode error; null when no decoder fits.</summary>
    internal static byte[]? DecodePixels(Texture? t, out int w, out int h)
    {
        w = h = 0;
        if (t == null || t.Width <= 0 || t.Height <= 0) return null;
        int mip = 0;
        for (int m = 0; m < Math.Max(1, (int)t.Levels); m++)
        {
            int mw = Math.Max(1, t.Width >> m), mh = Math.Max(1, t.Height >> m);
            if (mw >= 128 && mh >= 128) mip = m; else break;
        }
        for (; mip >= 0; mip--)   // przy bledzie schodzimy na coraz wiekszy mip, az do 0
        {
            try
            {
                var px = TextureDecoder.Piksele(t, mip, out int mw, out int mh);   // DDSIO + BC7 (BCnEncoder.Net)
                if (px == null) return null;                                 // format bez dekodera
                w = mw; h = mh;
                return px;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Usrednianie po prostokatach (box filter) z BGRA do RGB.</summary>
    internal static byte[] ScaleToRgb(byte[] px, int w, int h, int tw, int th)
    {
        var wy = new byte[tw * th * 3];
        for (int y = 0; y < th; y++)
        {
            int y0 = y * h / th, y1 = Math.Max(y0 + 1, (y + 1) * h / th);
            for (int x = 0; x < tw; x++)
            {
                int x0 = x * w / tw, x1 = Math.Max(x0 + 1, (x + 1) * w / tw);
                long r = 0, g = 0, b = 0; int n = 0;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        int o = (yy * w + xx) * 4;
                        b += px[o]; g += px[o + 1]; r += px[o + 2];   // DDSIO oddaje BGRA
                        n++;
                    }
                int c = (y * tw + x) * 3;
                wy[c] = (byte)(r / n); wy[c + 1] = (byte)(g / n); wy[c + 2] = (byte)(b / n);
            }
        }
        return wy;
    }

    static double[] ScaleToGrey(byte[] px, int w, int h, int tw, int th)
    {
        var rgb = ScaleToRgb(px, w, h, tw, th);
        var wy = new double[tw * th];
        for (int i = 0; i < wy.Length; i++)
            wy[i] = 0.299 * rgb[i * 3] + 0.587 * rgb[i * 3 + 1] + 0.114 * rgb[i * 3 + 2];
        return wy;
    }

    /// <summary>Odleglosc Hamminga miedzy odciskami 256-bitowymi. -1 = brak ktoregos odcisku.</summary>
}
