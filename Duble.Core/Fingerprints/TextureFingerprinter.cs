using System;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using Duble.Core.Formats;
using Duble.Core.Model;
using Duble.Core.Results;

namespace Duble.Core.Fingerprints;

/// <summary>Turns a texture file into its fingerprint: perceptual hash, colour signature, alpha share.</summary>
public interface ITextureFingerprinter
{
    /// <summary>
    /// The fingerprint of the first texture in a .ytd. A file that cannot be read at all comes back as
    /// texture.undecodable; a texture whose pixels will not decode gives a fingerprint with IsDecoded false,
    /// which the comparison knows how to treat.
    ///
    /// <paramref name="onPixels" /> is handed the decoded BGRA pixels with their width and height, so a caller
    /// that also wants a thumbnail gets one without decoding the same texture a second time. It is not called
    /// for a texture that would not decode.
    /// </summary>
    Result<TextureInfo> Compute(byte[] textureBytes, Action<byte[], int, int>? onPixels = null);
}

/// <inheritdoc />
public sealed class TextureFingerprinter : ITextureFingerprinter
{
    public TextureFingerprinter(CodeWalkerRuntime runtime) => _ = runtime;

    public Result<TextureInfo> Compute(byte[] textureBytes, Action<byte[], int, int>? onPixels = null)
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

        return Result<TextureInfo>.Ok(Describe(texture, onPixels));
    }

    /// <summary>Side of the greyscale image the DCT runs on.</summary>
    const int DctInputSide = 64;

    /// <summary>Side of the coefficient block kept from it: 16x16 = the 256 bits of the hash.</summary>
    const int DctBlockSide = 16;

    /// <summary>Side of the colour signature grid: 8x8 in RGB is 192 bytes.</summary>
    const int ColorGridSide = 8;

    static readonly double[,] Cosines = BuildCosineTable();

    static double[,] BuildCosineTable()
    {
        var table = new double[DctBlockSide, DctInputSide];
        for (int u = 0; u < DctBlockSide; u++)
            for (int x = 0; x < DctInputSide; x++)
                table[u, x] = Math.Cos((2.0 * x + 1.0) * u * Math.PI / (2.0 * DctInputSide));
        return table;
    }

    /// <summary>The short format name the report and the catalog show.</summary>
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
    /// Decodes the texture and describes it: perceptual hash, colour signature, alpha share. A texture whose
    /// pixels will not decode still gets its size, format and mip count — only IsDecoded stays false.
    /// </summary>
    static TextureInfo Describe(Texture texture, Action<byte[], int, int>? onPixels)
    {
        var info = new TextureInfo
        {
            Name = texture.Name,
            Width = texture.Width,
            Height = texture.Height,
            MipLevels = texture.Levels,
            Format = FormatName(texture),
            IsDecoded = false,
        };

        var pixels = DecodePixels(texture, out int width, out int height);
        if (pixels == null) return info;

        // hand the decoded pixels straight on — indexing writes its thumbnail from these rather than decoding
        // the same texture a second time
        onPixels?.Invoke(pixels, width, height);

        // --- perceptual hash: 64x64 greyscale, DCT, the 16x16 block, then the median ---
        // The DCT is SEPARABLE and computed as rows then columns: 82 thousand multiplications instead of a
        // million for the naive form, with the same result.
        var grey = ScaleToGrey(pixels, width, height, DctInputSide, DctInputSide);

        double mean = 0;
        foreach (var value in grey) mean += value;
        mean /= grey.Length;

        double variance = 0;
        foreach (var value in grey) variance += (value - mean) * (value - mean);
        info.Variance = (float)Math.Sqrt(variance / grey.Length);

        var rows = new double[DctInputSide * DctBlockSide];
        for (int x = 0; x < DctInputSide; x++)
        {
            int row = x * DctInputSide;
            for (int v = 0; v < DctBlockSide; v++)
            {
                double sum = 0;
                for (int y = 0; y < DctInputSide; y++) sum += grey[row + y] * Cosines[v, y];
                rows[x * DctBlockSide + v] = sum;
            }
        }

        var coefficients = new double[DctBlockSide * DctBlockSide];
        for (int u = 0; u < DctBlockSide; u++)
            for (int v = 0; v < DctBlockSide; v++)
            {
                double sum = 0;
                for (int x = 0; x < DctInputSide; x++) sum += rows[x * DctBlockSide + v] * Cosines[u, x];
                coefficients[u * DctBlockSide + v] = sum;
            }

        // the median leaves out the constant term at (0,0), which would otherwise dominate it
        var withoutDc = coefficients.Skip(1).OrderBy(x => x).ToArray();
        double median = (withoutDc[withoutDc.Length / 2 - 1] + withoutDc[withoutDc.Length / 2]) / 2.0;

        var hash = new ulong[4];
        for (int i = 0; i < 256; i++)
            if (coefficients[i] > median) hash[i >> 6] |= 1UL << (i & 63);
        info.PerceptualHash = hash;

        // --- colour signature: an 8x8 RGB grid ---
        info.ColorSignature = Convert.ToBase64String(ScaleToRgb(pixels, width, height, ColorGridSide, ColorGridSide));

        // --- how much of the texture is transparent ---
        int transparent = 0, total = width * height;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] < 250) transparent++;
        info.AlphaShare = total > 0 ? (float)transparent / total : 0f;

        info.IsDecoded = true;
        return info;
    }

    /// <summary>
    /// Pixels of the smallest mip that is still at least 128 px on a side. A 1024² texture with a full mip
    /// chain therefore decodes at 128² — 64 times cheaper — and the fingerprint comes out the same, because
    /// everything is scaled to 64x64 here anyway, with the same filter, so textures with and without mipmaps
    /// stay comparable. On a decode error it steps down towards mip 0; null when no decoder fits the format.
    /// </summary>
    internal static byte[]? DecodePixels(Texture? texture, out int width, out int height)
    {
        width = height = 0;
        if (texture == null || texture.Width <= 0 || texture.Height <= 0) return null;

        int mip = 0;
        for (int level = 0; level < Math.Max(1, (int)texture.Levels); level++)
        {
            int levelWidth = Math.Max(1, texture.Width >> level);
            int levelHeight = Math.Max(1, texture.Height >> level);
            if (levelWidth >= 128 && levelHeight >= 128) mip = level; else break;
        }

        for (; mip >= 0; mip--)
        {
            try
            {
                var pixels = TextureDecoder.Pixels(texture, mip, out int mipWidth, out int mipHeight);
                if (pixels == null) return null;   // a format with no decoder: stepping down will not help
                width = mipWidth;
                height = mipHeight;
                return pixels;
            }
            catch (Exception)
            {
                // this mip is corrupt; try a larger one
            }
        }

        return null;
    }

    /// <summary>Box-averaging from BGRA down to RGB.</summary>
    static byte[] ScaleToRgb(byte[] pixels, int width, int height, int targetWidth, int targetHeight)
    {
        var output = new byte[targetWidth * targetHeight * 3];

        for (int y = 0; y < targetHeight; y++)
        {
            int fromY = y * height / targetHeight, toY = Math.Max(fromY + 1, (y + 1) * height / targetHeight);
            for (int x = 0; x < targetWidth; x++)
            {
                int fromX = x * width / targetWidth, toX = Math.Max(fromX + 1, (x + 1) * width / targetWidth);

                long r = 0, g = 0, b = 0;
                int count = 0;
                for (int sy = fromY; sy < toY; sy++)
                    for (int sx = fromX; sx < toX; sx++)
                    {
                        int source = (sy * width + sx) * 4;
                        b += pixels[source];          // the decoder hands back BGRA
                        g += pixels[source + 1];
                        r += pixels[source + 2];
                        count++;
                    }

                int target = (y * targetWidth + x) * 3;
                output[target] = (byte)(r / count);
                output[target + 1] = (byte)(g / count);
                output[target + 2] = (byte)(b / count);
            }
        }

        return output;
    }

    /// <summary>The same box filter, then Rec. 601 luma — the greyscale the perceptual hash runs on.</summary>
    static double[] ScaleToGrey(byte[] pixels, int width, int height, int targetWidth, int targetHeight)
    {
        var rgb = ScaleToRgb(pixels, width, height, targetWidth, targetHeight);
        var grey = new double[targetWidth * targetHeight];
        for (int i = 0; i < grey.Length; i++)
            grey[i] = 0.299 * rgb[i * 3] + 0.587 * rgb[i * 3 + 1] + 0.114 * rgb[i * 3 + 2];
        return grey;
    }
}
