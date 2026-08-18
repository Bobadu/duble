using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The calibrator over a made-up catalog (Sztuczne.Siedem): the distributions, their histograms, the
/// thresholds it proposes, and cancelling half-way.
/// </summary>
public class CalibratorTests
{
    static readonly ICalibrator Calibrator = new Calibrator(new SystemClock());

    [Fact]
    public void A_distribution_reports_percentiles_and_a_histogram()
    {
        var spread = Distribution.Of(new double[] { 0, 0.1, 0.2, 0.3, 0.4, 5.0 }, 0, 0.5, 5);
        Assert.Equal(6, spread.N);
        Assert.Equal(0, spread.Min);
        Assert.Equal(5.0, spread.Max);
        Assert.Equal(0.3, spread.P50);
        Assert.Equal(new[] { 1, 1, 1, 1, 2 }, spread.Buckets);   // 5.0 is above the range, so it joins the last bucket
        Assert.Equal(6, spread.Buckets.Sum());
        Assert.Contains("n=6", spread.Text("F1"));

        var empty = Distribution.Of(Array.Empty<double>(), 0, 1, 4);
        Assert.Equal(0, empty.N);
        Assert.Equal(4, empty.Buckets.Length);
        Assert.Equal("no data", empty.Text());
    }

    [Fact]
    public void Measuring_a_catalog_gives_distributions_and_a_usable_proposal()
    {
        var directory = Sciezki.Tymczasowy("calibration");
        try
        {
            var catalog = new Catalog();
            catalog.Upsert(Sztuczne.Siedem(directory));
            var report = Calibrator.Run(catalog);

            Assert.Equal(7, report.Garments);
            Assert.Equal(7, report.GarmentsWithGeometry);
            Assert.Equal(9, report.Textures);
            Assert.Equal(9, report.DecodedTextures);

            // the same mesh appears as a = b, c = d and e = f = g, so at least 1 + 1 + 3 pairs share a hash
            Assert.True(report.GeoSameHash.N + report.GeoSameFile.N >= 5);
            Assert.Equal(7, report.GeoNearestForeign.N);
            Assert.All(new[] { report.GeoNearestForeign, report.HashVariants, report.HashRandom, report.ColorRandom },
                       distribution => Assert.Equal(distribution.N, distribution.Buckets.Sum()));

            // textures with an identical SHA: two pairs among a/b, three among e/f/g — and they MUST measure 0
            Assert.Equal(5, report.HashIdentical.N);
            Assert.Equal(0, report.HashIdentical.Max);

            // colour variants: a and b carry two textures each, so one pair from each
            Assert.Equal(2, report.HashVariants.N);
            Assert.True(report.HashRandom.N > 0);

            Assert.NotNull(report.Proposal);
            Assert.InRange(report.Proposal.GeometryIdentical, 0, 1);
            Assert.True(report.Proposal.GeometrySimilar >= report.Proposal.GeometryIdentical);
            Assert.InRange(report.Proposal.TextureHashDistance, 4, 256);
            Assert.True(report.Proposal.TextureColorDistance >= 0);
            Assert.Empty(report.Proposal.Validate());

            Assert.Equal(Thresholds.Default.TextureHashDistance, report.UsedThresholds.TextureHashDistance);
            Assert.False(string.IsNullOrEmpty(report.When));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Cancelling_stops_the_measurement()
    {
        var directory = Sciezki.Tymczasowy("calibration-cancel");
        try
        {
            var catalog = new Catalog();
            catalog.Upsert(Sztuczne.Siedem(directory));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => Calibrator.Run(catalog, null, cancellation.Token));
        }
        finally { Directory.Delete(directory, true); }
    }
}
