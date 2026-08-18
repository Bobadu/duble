using System.Collections.Generic;

namespace Duble.Core.Comparison;

/// <summary>
/// Where the comparison draws its lines. The defaults come from a calibration over 1132 garments and 9437
/// textures (see the header of DuplicateFinder.cs); a project may override them, and the calibrator measures
/// new ones against the user's own catalog.
/// </summary>
public class Thresholds
{
    /// <summary>Shape histogram distance below which two models count as the same mesh — together with an equal triangle and vertex count.</summary>
    public double GeometryIdentical { get; set; } = 0.02;

    /// <summary>Shape histogram distance below which two models are worth comparing at all.</summary>
    public double GeometrySimilar { get; set; } = 0.10;

    /// <summary>How far the triangle counts of two similar models may differ, relative to the larger one.</summary>
    public double GeometryTriangleTolerance { get; set; } = 0.05;

    /// <summary>How far their bounding boxes may differ.</summary>
    public double GeometryBoundsTolerance { get; set; } = 0.15;

    /// <summary>Hamming distance between two 256-bit perceptual hashes, below which the graphics count as the same.</summary>
    public int TextureHashDistance { get; set; } = 20;

    /// <summary>Mean per-channel colour difference (0..255) allowed alongside the hash.</summary>
    public double TextureColorDistance { get; set; } = 3.0;

    /// <summary>Below this brightness deviation a texture is flat and its hash bits are noise — the colour alone decides.</summary>
    public float FlatTextureVariance { get; set; } = 3.0f;

    /// <summary>The stricter colour distance used for those flat textures.</summary>
    public double FlatTextureColorDistance { get; set; } = 1.0;

    /// <summary>Share of one side's textures that must match for its set to count as fully covered.</summary>
    public double FullCoverage { get; set; } = 0.95;

    /// <summary>Below full coverage but above this, the pair is worth a human's eyes rather than a verdict.</summary>
    public double PartialCoverage { get; set; } = 0.5;

    public static Thresholds Default => new();

    public Thresholds Clone() => (Thresholds)MemberwiseClone();

    public bool SameAs(Thresholds? other) => other != null
        && GeometryIdentical == other.GeometryIdentical && GeometrySimilar == other.GeometrySimilar
        && GeometryTriangleTolerance == other.GeometryTriangleTolerance
        && GeometryBoundsTolerance == other.GeometryBoundsTolerance
        && TextureHashDistance == other.TextureHashDistance && TextureColorDistance == other.TextureColorDistance
        && FlatTextureVariance == other.FlatTextureVariance
        && FlatTextureColorDistance == other.FlatTextureColorDistance
        && FullCoverage == other.FullCoverage && PartialCoverage == other.PartialCoverage;

    /// <summary>
    /// The names of the fields that are out of range — an empty list means the thresholds are usable. Geometry
    /// distances live in [0;1] (similar at least as far as identical), the hash distance in [0;256], colour
    /// distances in [0;100], variance in [0;255], coverages in [0;1] (partial no larger than full).
    /// </summary>
    public List<string> Validate()
    {
        var bad = new List<string>();
        bool OutOfRange(double v, double from, double to) => double.IsNaN(v) || v < from || v > to;

        if (OutOfRange(GeometryIdentical, 0, 1)) bad.Add(nameof(GeometryIdentical));
        if (OutOfRange(GeometrySimilar, 0, 1) || GeometrySimilar < GeometryIdentical) bad.Add(nameof(GeometrySimilar));
        if (OutOfRange(GeometryTriangleTolerance, 0, 1)) bad.Add(nameof(GeometryTriangleTolerance));
        if (OutOfRange(GeometryBoundsTolerance, 0, 1)) bad.Add(nameof(GeometryBoundsTolerance));
        if (TextureHashDistance < 0 || TextureHashDistance > 256) bad.Add(nameof(TextureHashDistance));
        if (OutOfRange(TextureColorDistance, 0, 100)) bad.Add(nameof(TextureColorDistance));
        if (OutOfRange(FlatTextureVariance, 0, 255)) bad.Add(nameof(FlatTextureVariance));
        if (OutOfRange(FlatTextureColorDistance, 0, 100)) bad.Add(nameof(FlatTextureColorDistance));
        if (OutOfRange(FullCoverage, 0, 1)) bad.Add(nameof(FullCoverage));
        if (OutOfRange(PartialCoverage, 0, 1) || PartialCoverage > FullCoverage) bad.Add(nameof(PartialCoverage));
        return bad;
    }
}
