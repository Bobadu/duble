using System;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Core.Results;

namespace Duble.Cli.Tools;

/// <summary>
/// Reading a single .ydd or .ytd straight from disk, for the commands that work on one file rather than on a
/// catalog. CodeWalker is in gen9 mode, which reads both game formats by the version in each RSC7 header.
///
/// Every one of these used to be a try/catch that swallowed the exception and left the caller printing "could
/// not read the model" with no idea why. The message comes back with the failure now.
/// </summary>
public static class GameFiles
{
    /// <summary>The drawable dictionary in a .ydd, checked to hold geometry a person could actually look at.</summary>
    public static Result<YddFile> ReadModel(byte[] bytes)
    {
        try
        {
            var ydd = new YddFile();
            RpfFile.LoadResourceFile(ydd, bytes, 165);

            long vertices = ydd.Drawables?.FirstOrDefault()?.DrawableModels?.High?.FirstOrDefault()
                ?.Geometries?.FirstOrDefault()?.VertexBuffer?.VertexCount ?? 0;
            if (vertices <= 0) return Result<YddFile>.Fail(ErrorCodes.ModelUnreadable, "no geometry in the first drawable");
            if (vertices > 5_000_000)
                return Result<YddFile>.Fail(ErrorCodes.ModelUnreadable, $"{vertices} vertices — this is not a clothing model");

            return Result<YddFile>.Ok(ydd);
        }
        catch (Exception e)
        {
            return Result<YddFile>.Fail(ErrorCodes.ModelUnreadable, e.Message);
        }
    }

    /// <summary>The texture dictionary in a .ytd.</summary>
    public static Result<YtdFile> ReadTextures(byte[] bytes)
    {
        try
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, bytes, 13);

            var first = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            if (first == null) return Result<YtdFile>.Fail(ErrorCodes.TextureUndecodable, "no textures in the dictionary");
            if (first.Width <= 0 || first.Width > 16384 || first.Levels < 1 || first.Levels > 16)
                return Result<YtdFile>.Fail(ErrorCodes.TextureUndecodable,
                    $"implausible header ({first.Width}x{first.Height}, {first.Levels} mips) — probably not a .ytd");

            return Result<YtdFile>.Ok(ytd);
        }
        catch (Exception e)
        {
            return Result<YtdFile>.Fail(ErrorCodes.TextureUndecodable, e.Message);
        }
    }

    /// <summary>BGRA out of the decoder, RGB for a PNG.</summary>
    public static byte[] ToRgb(byte[] bgra, int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < bgra.Length && j + 2 < rgb.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];
            rgb[j + 1] = bgra[i + 1];
            rgb[j + 2] = bgra[i];
        }
        return rgb;
    }
}
