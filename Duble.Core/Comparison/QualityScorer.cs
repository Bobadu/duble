#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Duble.Core.Model;

namespace Duble.Core.Comparison;

/// <summary>
/// What a quality score is made of, out of 100 — kept alongside the total so that the interface and the report
/// can show WHY one copy won, not just that it did.
/// </summary>
public class QualityScore
{
    public double Total { get; set; }

    /// <summary>Texture resolution, up to 40.</summary>
    public double Resolution { get; set; }

    /// <summary>Mipmaps, up to 20.</summary>
    public double Mipmaps { get; set; }

    /// <summary>Colour variants, up to 20.</summary>
    public double Variants { get; set; }

    /// <summary>Texture format against alpha, up to 10.</summary>
    public double Format { get; set; }

    /// <summary>LOD levels, up to 10.</summary>
    public double Lod { get; set; }

    /// <summary>Median texture side in pixels.</summary>
    public double ResolutionPx { get; set; }

    /// <summary>Share of textures that have mipmaps, 0..1.</summary>
    public double MipmapShare { get; set; }

    public int VariantCount { get; set; }

    /// <summary>How many textures are BC1 with alpha — a compression that throws that alpha away.</summary>
    public int WrongFormatCount { get; set; }

    public int LodLevels { get; set; }

    public bool NoTextures { get; set; }

    /// <summary>The breakdown as a sentence, in the language asked for.</summary>
    public string Text(string language)
    {
        if (NoTextures) return Texts.T(language, "jakosc.brak");

        var invariant = CultureInfo.InvariantCulture;
        var parameters = new Dictionary<string, string>
        {
            ["px"] = ResolutionPx.ToString("F0", invariant), ["pRozdz"] = Resolution.ToString("F0", invariant),
            ["mipy"] = MipmapShare.ToString("P0", invariant), ["pMipy"] = Mipmaps.ToString("F0", invariant),
            ["n"] = VariantCount.ToString(invariant), ["pWar"] = Variants.ToString("F0", invariant),
            ["pFmt"] = Format.ToString("F0", invariant),
            ["zly"] = WrongFormatCount > 0
                ? Texts.T(language, "jakosc.zlyFormat", new Dictionary<string, string> { ["n"] = WrongFormatCount.ToString(invariant) })
                : "",
            ["lod"] = LodLevels.ToString(invariant), ["pLod"] = Lod.ToString("F0", invariant),
        };
        return Texts.T(language, "jakosc.rozpiska", parameters);
    }
}

/// <summary>Rates a garment out of 100, so that a group can propose which copy to keep.</summary>
public interface IQualityScorer
{
    QualityScore Score(Garment garment);
}

/// <inheritdoc />
public sealed class QualityScorer : IQualityScorer
{
    public QualityScore Score(Garment garment)
    {
        var textures = garment.Textures;
        if (textures.Count == 0) return new QualityScore { NoTextures = true, Total = 0 };

        // resolution: the median pixel count, with 1024x1024 counting as full marks
        var pixels = textures.Select(t => (double)t.Width * t.Height).Where(p => p > 0).OrderBy(p => p).ToArray();
        double medianPixels = pixels.Length > 0 ? pixels[pixels.Length / 2] : 0;
        double resolution = medianPixels > 0
            ? Math.Clamp(Math.Log2(medianPixels) / Math.Log2(1024.0 * 1024.0), 0, 1.25) * 40
            : 0;

        // mipmaps: 28% of the textures we measured have a single level, and without mipmaps a texture shimmers
        double mipmapShare = textures.Count(t => t.MipLevels > 1) / (double)textures.Count;
        double mipmaps = mipmapShare * 20;

        // colour variants: a richer choice in the game's menu
        double variants = Math.Min(textures.Count, 20) / 20.0 * 20;

        // format against alpha: BC1 has 1-bit alpha, so with transparency it is a loss
        int wrongFormat = textures.Count(t => t.Format == "BC1" && t.AlphaShare > 0.02f);
        double format = 10 * (1.0 - wrongFormat / (double)textures.Count);

        double lod = Math.Clamp((garment.Geometry?.LodLevels ?? 0) / 3.0, 0, 1) * 10;

        return new QualityScore
        {
            Total = resolution + mipmaps + variants + format + lod,
            Resolution = resolution,
            Mipmaps = mipmaps,
            Variants = variants,
            Format = format,
            Lod = lod,
            ResolutionPx = Math.Sqrt(medianPixels),
            MipmapShare = mipmapShare,
            VariantCount = textures.Count,
            WrongFormatCount = wrongFormat,
            LodLevels = garment.Geometry?.LodLevels ?? 0,
        };
    }
}
