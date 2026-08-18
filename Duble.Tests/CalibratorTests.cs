using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Duble.Tests;

/// <summary>The calibrator over a made-up catalog (Sztuczne.Siedem): distributions, histograms, the proposed
/// thresholds, and cancellation.</summary>
public class CalibratorTests
{
    static readonly ICalibrator Kalibrator = new Calibrator(new SystemClock());

    [Fact]
    public void Rozklad_liczy_percentyle_i_histogram()
    {
        var r = Distribution.Of(new double[] { 0, 0.1, 0.2, 0.3, 0.4, 5.0 }, 0, 0.5, 5);
        Assert.Equal(6, r.N); Assert.Equal(0, r.Min); Assert.Equal(5.0, r.Max); Assert.Equal(0.3, r.P50);
        Assert.Equal(new[] { 1, 1, 1, 1, 2 }, r.Buckets);   // 5.0 laduje w ostatnim kubelku (ponad zakres)
        Assert.Equal(6, r.Buckets.Sum());
        var pusty = Distribution.Of(Array.Empty<double>(), 0, 1, 4);
        Assert.Equal(0, pusty.N); Assert.Equal(4, pusty.Buckets.Length); Assert.Equal("no data", pusty.Text());
        Assert.Contains("n=6", r.Text("F1"));
    }

    [Fact]
    public void Policz_na_sztucznym_katalogu()
    {
        var tmp = Sciezki.Tymczasowy("kalib");
        try
        {
            var kat = new Catalog(); kat.Upsert(Sztuczne.Siedem(tmp));
            var w = Kalibrator.Run(kat);
            Assert.Equal(7, w.Garments); Assert.Equal(7, w.GarmentsWithGeometry);
            Assert.Equal(9, w.Textures); Assert.Equal(9, w.DecodedTextures);
            // ten sam mesh (H1: a=b, H3: c=d, H5: e=f=g) -> pary o tym samym hashu: 1 + 1 + 3 = 5; brak identycznych plikow ydd (SHA null == null? -> ShaYdd null u obu = "identyczne")
            Assert.True(w.GeoSameHash.N + w.GeoSameFile.N >= 5);
            Assert.Equal(7, w.GeoNearestForeign.N);
            Assert.All(new[] { w.GeoNearestForeign, w.HashVariants, w.HashRandom, w.ColorRandom }, r => Assert.Equal(r.N, r.Buckets.Sum()));
            // identyczne SHA tekstur: a/b (2 pary: S1p11=S1p11? Sha = sha+paczka+numer ujednolicone dla par a/b -> 2 pary), e/f/g (3 pary) -> 5
            Assert.Equal(5, w.HashIdentical.N); Assert.Equal(0, w.HashIdentical.Max);
            // warianty koloru: a ma 2 tekstury (S1,S2 rozne sha) -> 1 para; b tak samo -> 1; reszta po 1 teksturze -> 2 pary
            Assert.Equal(2, w.HashVariants.N);
            Assert.True(w.HashRandom.N > 0);
            Assert.NotNull(w.Proposal);
            Assert.InRange(w.Proposal.GeometryIdentical, 0, 1); Assert.True(w.Proposal.GeometrySimilar >= w.Proposal.GeometryIdentical);
            Assert.InRange(w.Proposal.TextureHashDistance, 4, 256); Assert.True(w.Proposal.TextureColorDistance >= 0);
            Assert.Empty(w.Proposal.Validate());
            Assert.Equal(Thresholds.Default.TextureHashDistance, w.UsedThresholds.TextureHashDistance);
            Assert.False(string.IsNullOrEmpty(w.When));

            // anulowanie
            using var cts = new CancellationTokenSource(); cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => Kalibrator.Run(kat, null, cts.Token));
        }
        finally { Directory.Delete(tmp, true); }
    }
}
