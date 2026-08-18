// A .glb preview of one garment, either from the catalog or from raw files (`duble glb`).
//
// The base colour comes from the first texture in the .ytd of the chosen variant (letter a/b/c…), decoded to
// an RGBA PNG at the largest mip with a side of 1024 or less. Textures embedded in the .ydd itself — hair
// carries its own diffuse and normal maps — come out of the drawable's own dictionary.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Core.Fingerprints;
using Duble.Core.Model;
using Duble.Core.Naming;
using Duble.Core.Results;
using Duble.Core.Sources;

namespace Duble.Core.Formats;

/// <summary>Builds the 3D preview the app shows: one garment as a glTF-Binary file.</summary>
public interface IMeshPreviewBuilder
{
    /// <summary>
    /// A preview of the garment, with the texture variant of the given letter when it has one. Fails when the
    /// model cannot be read; a missing texture only costs the preview its colour.
    /// </summary>
    Result<byte[]> Build(Garment garment, string? variantLetter = null, Action<string>? log = null);

    /// <summary>The same from raw bytes, for the command line and for tests.</summary>
    Result<byte[]> Build(byte[] modelBytes, byte[]? textureBytes = null, Action<string>? log = null);
}

/// <inheritdoc />
public sealed class MeshPreviewBuilder : IMeshPreviewBuilder
{
    const int MaxTextureSide = 1024;

    readonly IArchiveCache archives;

    public MeshPreviewBuilder(IArchiveCache archives, CodeWalkerRuntime runtime)
    {
        this.archives = archives;
        _ = runtime;
    }

    public Result<byte[]> Build(Garment garment, string? variantLetter = null, Action<string>? log = null)
    {
        var model = archives.Read(garment.ModelPath ?? "");
        if (model.IsFailure) return Result<byte[]>.Fail(model.Error);

        var texture = garment.Textures.FirstOrDefault(t => variantLetter == null
                          || string.Equals(ClothingFileName.ParseTexture(t.FileName)?.Letter, variantLetter,
                                           StringComparison.OrdinalIgnoreCase))
                      ?? garment.Textures.FirstOrDefault();

        byte[]? textureBytes = null;
        if (texture?.Path != null)
        {
            var read = archives.Read(texture.Path);
            if (read.IsSuccess) textureBytes = read.Value;
        }

        return Build(model.Value, textureBytes, log);
    }

    public Result<byte[]> Build(byte[] modelBytes, byte[]? textureBytes = null, Action<string>? log = null)
    {
        log ??= _ => { };

        Drawable? drawable;
        try
        {
            var ydd = new YddFile();
            RpfFile.LoadResourceFile(ydd, modelBytes, 165);
            drawable = ydd.Drawables is { Length: > 0 } drawables ? drawables[0] : null;
        }
        catch (Exception e)
        {
            return Result<byte[]>.Fail(ErrorCodes.ModelUnreadable, e.Message);
        }

        long vertices = drawable?.DrawableModels?.High?.FirstOrDefault()?.Geometries?.FirstOrDefault()
            ?.VertexBuffer?.VertexCount ?? 0;
        if (vertices <= 0 || vertices > 5_000_000)
            return Result<byte[]>.Fail(ErrorCodes.ModelUnreadable, $"no usable geometry ({vertices} vertices)");

        var geometries = GlbWriter.FromDrawable(drawable);
        var pngs = new Dictionary<string, byte[]>();

        // textures embedded in the drawable itself (hair and the like)
        var embedded = drawable!.ShaderGroup?.TextureDictionary?.Textures?.data_items;
        foreach (var key in geometries.SelectMany(g => new[] { g.Texture, g.NormalMap })
                     .Where(k => k != null && k.StartsWith("emb:")).Distinct())
        {
            var texture = embedded?.FirstOrDefault(t => string.Equals(t?.Name, key![4..], StringComparison.OrdinalIgnoreCase));
            var png = texture == null ? null : TextureDecoder.PngRgba(texture, MaxTextureSide);
            if (png != null) pngs[key!] = png;
            else log($"[warning] embedded texture {key} has no preview");
        }

        if (textureBytes != null)
        {
            try
            {
                var ytd = new YtdFile();
                RpfFile.LoadResourceFile(ytd, textureBytes, 13);
                var first = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
                var png = first == null ? null : TextureDecoder.PngRgba(first, MaxTextureSide);
                if (png != null) pngs["diff"] = png;
                else log("[warning] the base colour texture has no preview ("
                         + (first == null ? "empty ytd" : TextureFingerprinter.FormatName(first)) + ")");
            }
            catch (Exception e)
            {
                log("[warning] could not read the .ytd: " + e.Message);
            }
        }

        log($"{geometries.Count} geometries, {geometries.Sum(g => g.Positions!.Length / 3)} vertices, "
            + $"{geometries.Sum(g => g.Indices!.Length / 3)} triangles, {pngs.Count} textures");

        return Result<byte[]>.Ok(GlbWriter.Write(geometries, pngs));
    }
}
