#nullable enable
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

    public string Text(string format = "F4")
        => N == 0 ? "brak danych"
         : $"n={N,-7} min={Min.ToString(format, CultureInfo.InvariantCulture)} p01={P01.ToString(format, CultureInfo.InvariantCulture)} "
         + $"p05={P05.ToString(format, CultureInfo.InvariantCulture)} p50={P50.ToString(format, CultureInfo.InvariantCulture)} "
         + $"p95={P95.ToString(format, CultureInfo.InvariantCulture)} max={Max.ToString(format, CultureInfo.InvariantCulture)}";
}

public sealed class CalibrationReport
{
    public string When { get; set; } = "";
    public int Garments { get; set; }
    public int GarmentsWithGeometry { get; set; }
    public int Textures { get; set; }
    public int DecodedTextures { get; set; }

    // geometria (odleglosc histogramow 0..)
    public Distribution? GeoSameFile { get; set; }
    public Distribution? GeoSameHash { get; set; }
    public Distribution? GeoNearestForeign { get; set; }
    public int GeoPairsAcrossPacks { get; set; }
    public int GeoSuspicious { get; set; }
    /// <summary>Najblizsze pary o roznym meshu (d &lt; 0,05): do obejrzenia — duplikaty, ktorych hash nie zlapal, albo kolizje histogramu.</summary>
    public List<SuspiciousPair> Suspicious { get; set; } = new();

    // tekstury: PHash (hamming 0..256) i kolor (0..)
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
    /// <summary>Proposal: GeometryIdentical, GeometrySimilar (4x), TextureHashDistance, TextureColorDistance — reszta jak Thresholds.</summary>
    public Thresholds? Proposal { get; set; }
}

public sealed class SuspiciousPair { public double D { get; set; } public double Bbox { get; set; } public string A { get; set; } = ""; public string B { get; set; } = ""; public int TriA { get; set; } public int TriB { get; set; } }
public sealed class CloseRandomPair { public int PHash { get; set; } public double Color { get; set; } public string A { get; set; } = ""; public string B { get; set; } = ""; }

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
    public const double GeoZakres = 0.5; public const int GeoKubelki = 25;
    public const double PHashZakres = 128; public const int PHashKubelki = 32;
    public const double KolorZakres = 40; public const int KolorKubelki = 20;
    public const double WariancjaZakres = 80; public const int WariancjaKubelki = 20;

    public CalibrationReport Run(Catalog katalog, Thresholds? progi = null, CancellationToken ct = default)
    {
        progi ??= Thresholds.Default;
        var w = new CalibrationReport { When = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), UsedThresholds = progi.Clone(), Garments = katalog.Garments.Count };
        var poz = katalog.Garments.Where(p => p.Geometry?.ShapeHistogram != null && p.Geometry!.Vertices > 0).ToList();
        w.GarmentsWithGeometry = poz.Count;

        // ================= GEOMETRIA =================
        var pozytywySha = new List<double>();
        var pozytywyHash = new List<double>();
        var najblizszyObcy = new List<double>();
        var podejrzane = new List<(double d, Garment a, Garment b)>();
        for (int i = 0; i < poz.Count; i++)
        {
            if ((i & 15) == 0) ct.ThrowIfCancellationRequested();
            double min = double.MaxValue;
            for (int j = 0; j < poz.Count; j++)
            {
                if (i == j) continue;
                var a = poz[i]; var b = poz[j];
                double d = Distance.ShapeHistogram(a.Geometry!.ShapeHistogram, b.Geometry!.ShapeHistogram);
                bool tenSamMesh = a.Geometry!.PositionHash != null && a.Geometry!.PositionHash == b.Geometry!.PositionHash;
                if (j > i)
                {
                    if (a.ModelSha256 == b.ModelSha256) pozytywySha.Add(d);
                    else if (tenSamMesh) pozytywyHash.Add(d);
                    if (tenSamMesh && a.PackName != b.PackName) w.GeoPairsAcrossPacks++;
                    if (!tenSamMesh && d < 0.05) podejrzane.Add((d, a, b));
                }
                if (!tenSamMesh && d < min) min = d;
            }
            if (min < double.MaxValue) najblizszyObcy.Add(min);
        }
        w.GeoSameFile = Distribution.Of(pozytywySha, 0, GeoZakres, GeoKubelki);
        w.GeoSameHash = Distribution.Of(pozytywyHash, 0, GeoZakres, GeoKubelki);
        w.GeoNearestForeign = Distribution.Of(najblizszyObcy, 0, GeoZakres, GeoKubelki);
        w.GeoSuspicious = podejrzane.Count;
        w.Suspicious = podejrzane.OrderBy(x => x.d).Take(25)
            .Select(x => new SuspiciousPair { D = x.d, Bbox = Distance.BoundingBox(x.a.Geometry!.BoundingBox, x.b.Geometry!.BoundingBox), A = x.a.Label + x.a.Suffix, B = x.b.Label + x.b.Suffix, TriA = x.a.Geometry!.Triangles, TriB = x.b.Geometry!.Triangles }).ToList();

        // ================= TEKSTURY =================
        ct.ThrowIfCancellationRequested();
        var wszystkie = katalog.Garments.SelectMany(p => p.Textures.Where(t => t.IsDecoded).Select(t => (poz: p, tex: t))).ToList();
        w.Textures = katalog.Garments.Sum(p => p.Textures.Count);
        w.DecodedTextures = wszystkie.Count;

        // pozytywy: ten sam SHA
        var phSha = new List<double>(); var kolSha = new List<double>();
        foreach (var g in wszystkie.GroupBy(x => x.tex.Sha256).Where(g => g.Count() > 1))
        {
            var l = g.ToList();
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    phSha.Add(Distance.Hamming(l[i].tex.PerceptualHash, l[j].tex.PerceptualHash));
                    kolSha.Add(Distance.Color(l[i].tex.ColorSignature, l[j].tex.ColorSignature));
                }
        }
        // trudne negatywy: warianty koloru tego samego ciucha
        var phWar = new List<double>(); var kolWar = new List<double>();
        foreach (var p in katalog.Garments)
        {
            var l = p.Textures.Where(t => t.IsDecoded).ToList();
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    if (l[i].Sha256 == l[j].Sha256) continue;
                    phWar.Add(Distance.Hamming(l[i].PerceptualHash, l[j].PerceptualHash));
                    kolWar.Add(Distance.Color(l[i].ColorSignature, l[j].ColorSignature));
                }
        }
        ct.ThrowIfCancellationRequested();
        // negatywy losowe (400k prob, ziarno stale -> powtarzalne)
        var rnd = new Random(12345);
        var phLos = new List<double>(); var kolLos = new List<double>();
        var bliskie = new List<CloseRandomPair>();
        for (int k = 0; k < 400_000 && wszystkie.Count > 1; k++)
        {
            if ((k & 4095) == 0) ct.ThrowIfCancellationRequested();
            var a = wszystkie[rnd.Next(wszystkie.Count)];
            var b = wszystkie[rnd.Next(wszystkie.Count)];
            if (ReferenceEquals(a.poz, b.poz) || a.tex.Sha256 == b.tex.Sha256) continue;
            int ph = Distance.Hamming(a.tex.PerceptualHash, b.tex.PerceptualHash);
            double kol = Distance.Color(a.tex.ColorSignature, b.tex.ColorSignature);
            phLos.Add(ph); kolLos.Add(kol);
            if (ph <= 24) bliskie.Add(new CloseRandomPair { PHash = ph, Color = kol, A = $"{a.poz.Label}/{a.tex.FileName}", B = $"{b.poz.Label}/{b.tex.FileName}" });
        }
        w.HashIdentical = Distribution.Of(phSha, 0, PHashZakres, PHashKubelki);
        w.ColorIdentical = Distribution.Of(kolSha, 0, KolorZakres, KolorKubelki);
        w.HashVariants = Distribution.Of(phWar, 0, PHashZakres, PHashKubelki);
        w.ColorVariants = Distribution.Of(kolWar, 0, KolorZakres, KolorKubelki);
        w.Variance = Distribution.Of(wszystkie.Select(x => (double)x.tex.Variance), 0, WariancjaZakres, WariancjaKubelki);
        w.HashRandom = Distribution.Of(phLos, 0, PHashZakres, PHashKubelki);
        w.ColorRandom = Distribution.Of(kolLos, 0, KolorZakres, KolorKubelki);
        w.CloseRandom = bliskie.OrderBy(v => v.PHash).ThenBy(v => v.Color).Take(20).ToList();

        // ================= PROPOZYCJA =================
        var prop = progi.Clone();
        double progGeo = najblizszyObcy.Count > 0
            ? najblizszyObcy.OrderBy(x => x).ElementAt(Math.Max(0, (int)(0.001 * najblizszyObcy.Count))) / 3.0
            : progi.GeometryIdentical;
        // przyciete do zakresow Thresholds.Sprawdz (na sztucznych/dziwnych katalogach propozycja moglaby wyjsc poza)
        prop.GeometryIdentical = Math.Min(1, Math.Round(progGeo, 4));
        prop.GeometrySimilar = Math.Min(1, Math.Round(progGeo * 4, 4));
        prop.TextureHashDistance = phWar.Count > 0 ? (int)Math.Min(256, Math.Max(4, phWar.OrderBy(x => x).First() / 2)) : progi.TextureHashDistance;
        prop.TextureColorDistance = kolWar.Count > 0 ? Math.Min(100, Math.Round(kolWar.OrderBy(x => x).First() / 2, 2)) : progi.TextureColorDistance;
        w.Proposal = prop;
        return w;
    }

    /// <summary>Wydruk dla CLI (`duble kalibruj`).</summary>
}
