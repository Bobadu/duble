// Choosing thresholds BY MEASUREMENT rather than by feel.
//
// Three kinds of pair have a known answer, and that is enough to place a threshold:
//
//   POSITIVES      — files identical byte for byte (the same SHA). Their distance MUST come out at 0; if it
//                    does not, the fingerprint is broken.
//   HARD NEGATIVES — colour variants of the SAME garment (letters a/b/c… under one number). In greyscale they
//                    look identical, so they are what decides whether the hash alone is enough or the colour
//                    signature is needed as well.
//   NEGATIVES      — random pairs of different garments.
//
// A threshold goes BELOW the nearest negative, not above the furthest positive: a false duplicate deletes a
// garment that cannot be got back except from the pack it came in.
//
// Run() returns the distributions as DATA — percentiles plus a histogram — so the app can draw them as bar
// charts with the thresholds marked, and the CLI can print the same numbers as text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Duble.Core.Comparison;
using Duble.Core.Fingerprints;
using Duble.Core.Model;
using Duble.Core.Time;

namespace Duble.Core.Calibration;

/// <summary>
/// How a set of values is spread: percentiles plus a histogram over a given range. The last bucket collects
/// everything above the range, so nothing is silently dropped off the chart.
/// </summary>
public sealed class Distribution
{
    public int N { get; set; }
    public double Min { get; set; }
    public double P01 { get; set; }
    public double P05 { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double Max { get; set; }
    public double From { get; set; }
    public double To { get; set; }
    public int[] Buckets { get; set; } = Array.Empty<int>();

    public static Distribution Of(IEnumerable<double> values, double from, double to, int buckets)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        var result = new Distribution { N = sorted.Length, From = from, To = to, Buckets = new int[Math.Max(1, buckets)] };
        if (sorted.Length == 0) return result;

        double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, Math.Max(0, (int)(p * sorted.Length)))];
        result.Min = sorted[0];
        result.P01 = Percentile(0.01);
        result.P05 = Percentile(0.05);
        result.P50 = Percentile(0.50);
        result.P95 = Percentile(0.95);
        result.Max = sorted[^1];

        double width = (to - from) / result.Buckets.Length;
        foreach (var value in sorted)
        {
            // the epsilon keeps 0.3/0.1 = 2.9999… in bucket 3 rather than 2
            int bucket = width > 0 ? (int)Math.Floor((value - from) / width + 1e-9) : 0;
            if (bucket < 0) bucket = 0;
            if (bucket >= result.Buckets.Length) bucket = result.Buckets.Length - 1;
            result.Buckets[bucket]++;
        }
        return result;
    }

    /// <summary>The percentiles as one line, for the command line and for logs.</summary>
    public string Text(string format = "F4")
    {
        if (N == 0) return "no data";
        string Value(double x) => x.ToString(format, CultureInfo.InvariantCulture);
        return $"n={N,-7} min={Value(Min)} p01={Value(P01)} p05={Value(P05)} "
             + $"p50={Value(P50)} p95={Value(P95)} max={Value(Max)}";
    }
}

/// <summary>Everything one calibration measured, plus the thresholds those measurements support.</summary>
public sealed class CalibrationReport
{
    public string When { get; set; } = "";
    public int Garments { get; set; }
    public int GarmentsWithGeometry { get; set; }
    public int Textures { get; set; }
    public int DecodedTextures { get; set; }

    // ---- geometry: L1 distance between shape histograms, 0 and up ----

    public Distribution? GeoSameFile { get; set; }
    public Distribution? GeoSameHash { get; set; }
    public Distribution? GeoNearestForeign { get; set; }

    /// <summary>Pairs with the same mesh that came from different packs — the duplicates worth finding.</summary>
    public int GeoPairsAcrossPacks { get; set; }

    public int GeoSuspicious { get; set; }

    /// <summary>
    /// The closest pairs whose meshes are NOT the same (d &lt; 0.05), for a person to look at: either duplicates
    /// the position hash missed, or histogram collisions that show the threshold is too generous.
    /// </summary>
    public List<SuspiciousPair> Suspicious { get; set; } = new();

    // ---- textures: perceptual hash (Hamming, 0..256) and colour (0 and up) ----

    public Distribution? HashIdentical { get; set; }
    public Distribution? ColorIdentical { get; set; }
    public Distribution? HashVariants { get; set; }
    public Distribution? ColorVariants { get; set; }
    public Distribution? Variance { get; set; }
    public Distribution? HashRandom { get; set; }
    public Distribution? ColorRandom { get; set; }
    public List<CloseRandomPair> CloseRandom { get; set; } = new();

    /// <summary>The thresholds in force while this calibration ran, so the charts can mark them.</summary>
    public Thresholds? UsedThresholds { get; set; }

    /// <summary>
    /// What the measurements suggest: GeometryIdentical, GeometrySimilar, TextureHashDistance and
    /// TextureColorDistance. Everything else is copied from the thresholds that were in force.
    /// </summary>
    public Thresholds? Proposal { get; set; }
}

/// <summary>Two garments whose shape histograms nearly agree although their meshes do not.</summary>
public sealed class SuspiciousPair
{
    public double D { get; set; }
    public double Bbox { get; set; }
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public int TriA { get; set; }
    public int TriB { get; set; }
}

/// <summary>Two textures from different garments that landed closer together than random pairs should.</summary>
public sealed class CloseRandomPair
{
    public int PHash { get; set; }
    public double Color { get; set; }
    public string A { get; set; } = "";
    public string B { get; set; } = "";
}

/// <summary>Measures the distances the thresholds judge, over the user's own catalog.</summary>
public interface ICalibrator
{
    /// <summary>
    /// Distributions for geometry, perceptual hash and colour — identical files, colour variants of one
    /// garment, and random pairs — plus a proposal for the thresholds those distributions support.
    /// </summary>
    CalibrationReport Run(Catalog catalog, Thresholds? thresholds = null, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class Calibrator : ICalibrator
{
    // ranges and bucket counts of the histograms, chosen so the interesting part of each fills the chart
    const double GeoRange = 0.5;
    const int GeoBuckets = 25;
    const double HashRange = 128;
    const int HashBuckets = 32;
    const double ColorRange = 40;
    const int ColorBuckets = 20;
    const double VarianceRange = 80;
    const int VarianceBuckets = 20;

    /// <summary>How many random texture pairs to draw. A fixed seed keeps two runs comparable.</summary>
    const int RandomPairs = 400_000;
    const int RandomSeed = 12345;

    /// <summary>Below this, two different meshes are close enough that a person should look at the pair.</summary>
    const double SuspiciousGeometryDistance = 0.05;

    /// <summary>Below this, a random texture pair is close enough to be worth listing.</summary>
    const int CloseRandomHashDistance = 24;

    readonly IClock clock;

    public Calibrator(IClock clock) => this.clock = clock;

    public CalibrationReport Run(Catalog catalog, Thresholds? thresholds = null, CancellationToken ct = default)
    {
        thresholds ??= Thresholds.Default;
        var report = new CalibrationReport
        {
            When = clock.Stamp(),
            UsedThresholds = thresholds.Clone(),
            Garments = catalog.Garments.Count,
        };

        var garments = catalog.Garments
            .Where(garment => garment.Geometry?.ShapeHistogram != null && garment.Geometry.Vertices > 0)
            .ToList();
        report.GarmentsWithGeometry = garments.Count;

        var nearestForeign = MeasureGeometry(garments, report, ct);
        var (variantHashes, variantColours) = MeasureTextures(catalog, report, ct);

        report.Proposal = Propose(thresholds, nearestForeign, variantHashes, variantColours);
        return report;
    }

    // ===================== geometry =====================

    /// <summary>
    /// Fills in the geometry distributions and returns, for each garment, the distance to the nearest mesh that
    /// is not its own — the number a threshold has to stay below.
    /// </summary>
    static List<double> MeasureGeometry(List<Garment> garments, CalibrationReport report, CancellationToken ct)
    {
        var sameFile = new List<double>();
        var sameHash = new List<double>();
        var nearestForeign = new List<double>();
        var suspicious = new List<(double Distance, Garment A, Garment B)>();

        for (int i = 0; i < garments.Count; i++)
        {
            if ((i & 15) == 0) ct.ThrowIfCancellationRequested();

            double nearest = double.MaxValue;
            for (int j = 0; j < garments.Count; j++)
            {
                if (i == j) continue;
                var a = garments[i];
                var b = garments[j];
                double distance = Distance.ShapeHistogram(a.Geometry!.ShapeHistogram, b.Geometry!.ShapeHistogram);
                bool sameMesh = a.Geometry.PositionHash != null && a.Geometry.PositionHash == b.Geometry.PositionHash;

                if (j > i)
                {
                    if (a.ModelSha256 == b.ModelSha256) sameFile.Add(distance);
                    else if (sameMesh) sameHash.Add(distance);

                    if (sameMesh && a.PackName != b.PackName) report.GeoPairsAcrossPacks++;
                    if (!sameMesh && distance < SuspiciousGeometryDistance) suspicious.Add((distance, a, b));
                }

                if (!sameMesh && distance < nearest) nearest = distance;
            }

            if (nearest < double.MaxValue) nearestForeign.Add(nearest);
        }

        report.GeoSameFile = Distribution.Of(sameFile, 0, GeoRange, GeoBuckets);
        report.GeoSameHash = Distribution.Of(sameHash, 0, GeoRange, GeoBuckets);
        report.GeoNearestForeign = Distribution.Of(nearestForeign, 0, GeoRange, GeoBuckets);
        report.GeoSuspicious = suspicious.Count;
        report.Suspicious = suspicious
            .OrderBy(pair => pair.Distance)
            .Take(25)
            .Select(pair => new SuspiciousPair
            {
                D = pair.Distance,
                Bbox = Distance.BoundingBox(pair.A.Geometry!.BoundingBox, pair.B.Geometry!.BoundingBox),
                A = pair.A.Label + pair.A.Suffix,
                B = pair.B.Label + pair.B.Suffix,
                TriA = pair.A.Geometry!.Triangles,
                TriB = pair.B.Geometry!.Triangles,
            })
            .ToList();

        return nearestForeign;
    }

    // ===================== textures =====================

    /// <summary>
    /// Fills in the texture distributions and returns the hard negatives: the hash and colour distances between
    /// colour variants of one garment, which is what the texture thresholds have to stay below.
    /// </summary>
    static (List<double> Hashes, List<double> Colours) MeasureTextures(
        Catalog catalog, CalibrationReport report, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var decoded = catalog.Garments
            .SelectMany(garment => garment.Textures.Where(t => t.IsDecoded).Select(t => (Garment: garment, Texture: t)))
            .ToList();
        report.Textures = catalog.Garments.Sum(garment => garment.Textures.Count);
        report.DecodedTextures = decoded.Count;

        // positives: the same file under two names, so both distances must be 0
        var identicalHashes = new List<double>();
        var identicalColours = new List<double>();
        foreach (var group in decoded.GroupBy(x => x.Texture.Sha256).Where(g => g.Count() > 1))
        {
            var members = group.ToList();
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                {
                    identicalHashes.Add(Distance.Hamming(members[i].Texture.PerceptualHash, members[j].Texture.PerceptualHash));
                    identicalColours.Add(Distance.Color(members[i].Texture.ColorSignature, members[j].Texture.ColorSignature));
                }
        }

        // hard negatives: colour variants of one garment, which look the same in greyscale
        var variantHashes = new List<double>();
        var variantColours = new List<double>();
        foreach (var garment in catalog.Garments)
        {
            var textures = garment.Textures.Where(t => t.IsDecoded).ToList();
            for (int i = 0; i < textures.Count; i++)
                for (int j = i + 1; j < textures.Count; j++)
                {
                    if (textures[i].Sha256 == textures[j].Sha256) continue;
                    variantHashes.Add(Distance.Hamming(textures[i].PerceptualHash, textures[j].PerceptualHash));
                    variantColours.Add(Distance.Color(textures[i].ColorSignature, textures[j].ColorSignature));
                }
        }

        ct.ThrowIfCancellationRequested();

        // negatives: random pairs from different garments
        var random = new Random(RandomSeed);
        var randomHashes = new List<double>();
        var randomColours = new List<double>();
        var close = new List<CloseRandomPair>();
        for (int k = 0; k < RandomPairs && decoded.Count > 1; k++)
        {
            if ((k & 4095) == 0) ct.ThrowIfCancellationRequested();

            var a = decoded[random.Next(decoded.Count)];
            var b = decoded[random.Next(decoded.Count)];
            if (ReferenceEquals(a.Garment, b.Garment) || a.Texture.Sha256 == b.Texture.Sha256) continue;

            int hash = Distance.Hamming(a.Texture.PerceptualHash, b.Texture.PerceptualHash);
            double colour = Distance.Color(a.Texture.ColorSignature, b.Texture.ColorSignature);
            randomHashes.Add(hash);
            randomColours.Add(colour);

            if (hash <= CloseRandomHashDistance)
                close.Add(new CloseRandomPair
                {
                    PHash = hash,
                    Color = colour,
                    A = $"{a.Garment.Label}/{a.Texture.FileName}",
                    B = $"{b.Garment.Label}/{b.Texture.FileName}",
                });
        }

        report.HashIdentical = Distribution.Of(identicalHashes, 0, HashRange, HashBuckets);
        report.ColorIdentical = Distribution.Of(identicalColours, 0, ColorRange, ColorBuckets);
        report.HashVariants = Distribution.Of(variantHashes, 0, HashRange, HashBuckets);
        report.ColorVariants = Distribution.Of(variantColours, 0, ColorRange, ColorBuckets);
        report.Variance = Distribution.Of(decoded.Select(x => (double)x.Texture.Variance), 0, VarianceRange, VarianceBuckets);
        report.HashRandom = Distribution.Of(randomHashes, 0, HashRange, HashBuckets);
        report.ColorRandom = Distribution.Of(randomColours, 0, ColorRange, ColorBuckets);
        report.CloseRandom = close.OrderBy(pair => pair.PHash).ThenBy(pair => pair.Color).Take(20).ToList();

        return (variantHashes, variantColours);
    }

    // ===================== the proposal =====================

    /// <summary>
    /// Each threshold goes a safe distance below the nearest thing it must NOT catch: geometry a third of the
    /// way to the 0.1th percentile of foreign meshes, the texture thresholds halfway to the closest pair of
    /// colour variants. The results are clamped to the ranges Thresholds accepts, because a small or unusual
    /// catalog can otherwise propose a value outside them.
    /// </summary>
    static Thresholds Propose(Thresholds current, List<double> nearestForeign,
                              List<double> variantHashes, List<double> variantColours)
    {
        var proposal = current.Clone();

        double geometry = nearestForeign.Count > 0
            ? nearestForeign.OrderBy(x => x).ElementAt(Math.Max(0, (int)(0.001 * nearestForeign.Count))) / 3.0
            : current.GeometryIdentical;

        proposal.GeometryIdentical = Math.Min(1, Math.Round(geometry, 4));
        proposal.GeometrySimilar = Math.Min(1, Math.Round(geometry * 4, 4));
        proposal.TextureHashDistance = variantHashes.Count > 0
            ? (int)Math.Min(256, Math.Max(4, variantHashes.Min() / 2))
            : current.TextureHashDistance;
        proposal.TextureColorDistance = variantColours.Count > 0
            ? Math.Min(100, Math.Round(variantColours.Min() / 2, 2))
            : current.TextureColorDistance;

        return proposal;
    }
}
