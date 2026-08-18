#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Core.Fingerprints;
using Duble.Core.Formats;
using Duble.Core.Model;
using Duble.Core.Sources;

namespace Duble.Core.Reporting;

/// <summary>
/// The thumbnails of one report, as data:image/png;base64 URIs — that is what makes a report a single file
/// that still works after being copied anywhere, with no network.
///
/// The catalog keeps fingerprints, not images, so every texture is decoded again from the file it came from.
/// The same texture turns up in many groups, so each one is decoded once and remembered by its SHA-256. The
/// cache lives exactly as long as the report being built: base64 previews of a whole wardrobe run to tens of
/// megabytes, and holding them any longer would only be a leak.
/// </summary>
sealed class ReportThumbnails
{
    readonly IArchiveCache archives;
    readonly Dictionary<string, string?> byHash = new(StringComparer.Ordinal);
    readonly int side;

    public ReportThumbnails(IArchiveCache archives, int side)
    {
        this.archives = archives;
        this.side = side;
    }

    /// <summary>How many distinct thumbnails ended up in the report.</summary>
    public int Rendered { get; private set; }

    /// <summary>Textures whose file could not be read — a source that moved or was deleted since indexing.</summary>
    public int MissingFiles { get; private set; }

    /// <summary>Textures no decoder would take, so the tile shows the format name instead of a picture.</summary>
    public int Undecodable { get; private set; }

    /// <summary>The texture as a data: URI, or null when it cannot be shown.</summary>
    public string? DataUri(TextureInfo texture)
    {
        if (texture.Path == null) { MissingFiles++; return null; }

        // the hash identifies the file; an unhashed texture falls back to its path, which is unique too
        var key = texture.Sha256 ?? texture.Path;
        if (byHash.TryGetValue(key, out var cached)) return cached;

        var uri = Render(texture.Path);
        byHash[key] = uri;
        if (uri != null) Rendered++;
        return uri;
    }

    string? Render(string path)
    {
        var read = archives.Read(path);
        if (read.IsFailure) { MissingFiles++; return null; }

        try
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, read.Value, 13);
            var texture = ytd.TextureDict?.Textures?.data_items?.FirstOrDefault();
            var rgb = texture == null ? null : Thumbnail.Render(texture, side);
            if (rgb == null) { Undecodable++; return null; }
            return "data:image/png;base64," + Convert.ToBase64String(PngWriter.Rgb(rgb, side, side));
        }
        catch (Exception)
        {
            Undecodable++;
            return null;
        }
    }
}
