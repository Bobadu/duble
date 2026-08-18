// Decoding a texture's pixels: CodeWalker handles BC1–BC5 and the uncompressed formats, BCnEncoder.Net handles
// BC7.
//
// CodeWalker's DDSIO.GetPixels has `case BC7: //TODO` and returns null, and BC7 is about 5% of the textures in
// the packs we measured — until this, those had neither a fingerprint nor a preview. BC7 is decoded straight
// from the raw blocks (16 bytes per 4x4 block) that sit in Texture.Data.FullData, mip 0 first. The result is
// always BGRA, the same as DDSIO returns, so nothing downstream has to know which decoder ran.
using System;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace Duble.Core.Formats;

/// <summary>Gets pixels out of a GTA texture, whatever it is compressed with.</summary>
public static class TextureDecoder
{
    static readonly BcDecoder Decoder = new();

    /// <summary>BGRA pixels of one mip level; null when nothing can decode it.</summary>
    public static byte[]? Pixels(Texture? texture, int mip, out int width, out int height)
    {
        width = height = 0;
        if (texture == null || texture.Width <= 0 || texture.Height <= 0) return null;

        mip = Math.Clamp(mip, 0, Math.Max(0, texture.Levels - 1));
        width = Math.Max(1, texture.Width >> mip);
        height = Math.Max(1, texture.Height >> mip);

        byte[]? pixels = null;
        try { pixels = DDSIO.GetPixels(texture, mip); }
        catch (Exception) { pixels = null; }

        if (pixels != null && pixels.Length == width * height * 4) return pixels;
        return texture.Format == TextureFormat.D3DFMT_BC7 ? DecodeBc7(texture, mip, width, height) : null;
    }

    /// <summary>
    /// An RGBA PNG of the largest mip whose side is at most maxSide — the app's preview and the texture the
    /// GLB carries. Null when the texture will not decode.
    /// </summary>
    public static byte[]? PngRgba(Texture? texture, int maxSide = 1024)
    {
        if (texture == null || texture.Width <= 0 || texture.Height <= 0) return null;

        int mip = 0;
        while ((texture.Width >> mip) > maxSide && (texture.Height >> mip) > maxSide && mip < texture.Levels - 1) mip++;

        var pixels = Pixels(texture, mip, out int width, out int height);
        if (pixels == null) return null;

        // BGRA out of the decoder, RGBA into the PNG
        var rgba = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            rgba[i] = pixels[i + 2];
            rgba[i + 1] = pixels[i + 1];
            rgba[i + 2] = pixels[i];
            rgba[i + 3] = pixels[i + 3];
        }
        return PngWriter.Rgba(rgba, width, height);
    }

    static byte[]? DecodeBc7(Texture texture, int mip, int width, int height)
    {
        var data = texture.Data?.FullData;
        if (data == null) return null;

        // mips follow one another from level 0; skip over the ones before the one asked for
        long offset = 0;
        for (int level = 0; level < mip; level++)
        {
            int levelWidth = Math.Max(1, texture.Width >> level);
            int levelHeight = Math.Max(1, texture.Height >> level);
            offset += (long)((levelWidth + 3) / 4) * ((levelHeight + 3) / 4) * 16;
        }

        int length = ((width + 3) / 4) * ((height + 3) / 4) * 16;
        if (offset + length > data.Length) return null;

        var blocks = new byte[length];
        Buffer.BlockCopy(data, (int)offset, blocks, 0, length);

        ColorRgba32[] colors;
        try { colors = Decoder.DecodeRaw(blocks, width, height, CompressionFormat.Bc7); }
        catch (Exception) { return null; }

        if (colors == null || colors.Length < width * height) return null;

        var output = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            output[i * 4] = colors[i].b;
            output[i * 4 + 1] = colors[i].g;
            output[i * 4 + 2] = colors[i].r;
            output[i * 4 + 3] = colors[i].a;
        }
        return output;
    }
}
