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
    public static object? ReasonJson(Reason? reason) => reason == null ? null : new { kod = reason.Code, p = reason.Parameters };

    public static object ResolutionJson(Resolution resolution) => new
    {
        zwyciezca = resolution.Winner,
        odrzucone = resolution.Rejected,
        ignoruj = resolution.Ignored,
        domyslna = resolution.IsDefault,
        notatka = resolution.Note,
    };

    public static object? QualityJson(QualityScore? score) => score == null ? null : new
    {
        razem = score.Total,
        rozdz = score.Resolution,
        mipy = score.Mipmaps,
        warianty = score.Variants,
        format = score.Format,
        lod = score.Lod,
        rozdzPx = score.ResolutionPx,
        udzialMipow = score.MipmapShare,
        liczbaWariantow = score.VariantCount,
        zlyFormat = score.WrongFormatCount,
        lody = score.LodLevels,
        brakTekstur = score.NoTextures,
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
            ["zrodloId"] = garment.SourceId,
            ["zrodlo"] = sourceName(garment),
            ["kontener"] = garment.Container,
            ["typ"] = garment.Slot,
            ["numer"] = garment.Number,
            ["sufiks"] = garment.Suffix,
            ["gen9"] = garment.GameFormat == GameFormat.Enhanced,
            ["props"] = garment.IsProp,
            ["punkty"] = score,
            ["thumb"] = Thumbnail(garment),
            ["tekstur"] = garment.Textures.Count,
            ["wierzcholki"] = garment.Geometry?.Vertices ?? 0,
            ["trojkaty"] = garment.Geometry?.Triangles ?? 0,
            ["lody"] = garment.Geometry?.LodLevels ?? 0,
            ["bajty"] = garment.ModelSize + garment.Textures.Sum(texture => texture.Size),
            ["wArchiwum"] = IsInArchive(garment),
        };

        if (details)
        {
            described["rozpiska"] = QualityJson(breakdown);
            described["sciezkaYdd"] = garment.ModelPath;
            described["bajtyYdd"] = garment.ModelSize;
            // the colour variant is worked out by Core: the interface used to have its own regular expression
            // for it, in two files, and both lost the props (p_ears_diff_017_a.ytd has no race letter)
            described["tekstury"] = garment.Textures.Select(texture => new
            {
                sha = texture.Sha256,
                plik = texture.FileName,
                nazwa = texture.Name,
                litera = ClothingFileName.ParseTexture(texture.FileName)?.Letter,
                w = texture.Width,
                h = texture.Height,
                format = texture.Format,
                mipy = texture.MipLevels,
                alfa = texture.AlphaShare,
                zdekodowana = texture.IsDecoded,
                bajty = texture.Size,
            }).ToList();
        }

        return described;
    }
}
