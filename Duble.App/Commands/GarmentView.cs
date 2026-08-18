// Commands/GarmentView.cs — one garment as the interface reads it, for the Duplicates list, the catalog grid
// and the card of a single garment.
//
// The plain fields are always there: id, source, slot/number/suffix, format, score, thumbnail, counts. With
// details it also carries the quality breakdown, the paths and the textures. Inside a group the score comes
// from the comparison; on its own it is worked out here.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.App.Commands;

public sealed class GarmentView
{
    readonly IQualityScorer scorer;

    public GarmentView(IQualityScorer scorer) => this.scorer = scorer;

    /// <summary>A reason travels as a code and its parameters; the interface writes the sentence from i18n.</summary>
    public static object? ReasonJson(Reason? reason) => reason == null ? null : new { code = reason.Code, parameters = reason.Parameters };

    public static object ResolutionJson(Resolution resolution) => new
    {
        winner = resolution.Winner,
        rejected = resolution.Rejected,
        ignored = resolution.Ignored,
        isDefault = resolution.IsDefault,
        note = resolution.Note,
    };

    public static object? QualityJson(QualityScore? score) => score == null ? null : new
    {
        total = score.Total,
        resolution = score.Resolution,
        mipmaps = score.Mipmaps,
        variants = score.Variants,
        format = score.Format,
        lod = score.Lod,
        resolutionPx = score.ResolutionPx,
        mipmapShare = score.MipmapShare,
        variantCount = score.VariantCount,
        wrongFormat = score.WrongFormatCount,
        lodLevels = score.LodLevels,
        noTextures = score.NoTextures,
    };

    /// <summary>A garment whose model sits inside a .rpf: it can be shown, but never moved.</summary>
    public static bool IsInArchive(Garment garment) => garment.ModelPath != null && garment.ModelPath.Contains('|');

    /// <summary>The thumbnail to show: the sha of the first texture that could be decoded.</summary>
    public static string? Thumbnail(Garment garment)
        => garment.Textures.FirstOrDefault(texture => texture.IsDecoded && texture.Sha256 != null)?.Sha256;

    /// <summary>
    /// A garment as a member of <paramref name="group"/>, or on its own when there is none.
    /// </summary>
    public Dictionary<string, object?> Describe(Garment garment, DuplicateGroup? group, bool details, Func<Garment, string> sourceName)
    {
        double score;
        QualityScore? breakdown = null;
        if (group != null)
        {
            score = group.Scores.GetValueOrDefault(garment.Id!);
            if (details) breakdown = group.ScoreBreakdown.GetValueOrDefault(garment.Id!);
        }
        else
        {
            breakdown = scorer.Score(garment);
            score = breakdown.Total;
        }

        var described = new Dictionary<string, object?>
        {
            ["id"] = garment.Id,
            ["sourceId"] = garment.SourceId,
            ["source"] = sourceName(garment),
            ["container"] = garment.Container,
            ["slot"] = garment.Slot,
            ["number"] = garment.Number,
            ["suffix"] = garment.Suffix,
            ["gen9"] = garment.GameFormat == GameFormat.Enhanced,
            ["prop"] = garment.IsProp,
            ["score"] = score,
            ["thumbnail"] = Thumbnail(garment),
            ["textureCount"] = garment.Textures.Count,
            ["vertices"] = garment.Geometry?.Vertices ?? 0,
            ["triangles"] = garment.Geometry?.Triangles ?? 0,
            ["lods"] = garment.Geometry?.LodLevels ?? 0,
            ["bytes"] = garment.ModelSize + garment.Textures.Sum(texture => texture.Size),
            ["inArchive"] = IsInArchive(garment),
        };

        if (details)
        {
            described["quality"] = QualityJson(breakdown);
            described["modelPath"] = garment.ModelPath;
            described["modelBytes"] = garment.ModelSize;
            // the colour variant is worked out by Core: the interface used to have its own regular expression
            // for it, in two files, and both lost the prop (p_ears_diff_017_a.ytd has no race letter)
            described["textures"] = garment.Textures.Select(texture => new
            {
                sha = texture.Sha256,
                file = texture.FileName,
                name = texture.Name,
                variant = ClothingFileName.ParseTexture(texture.FileName)?.Letter,
                width = texture.Width,
                height = texture.Height,
                format = texture.Format,
                mipmaps = texture.MipLevels,
                alpha = texture.AlphaShare,
                decoded = texture.IsDecoded,
                bytes = texture.Size,
            }).ToList();
        }

        return described;
    }
}
