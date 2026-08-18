using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The verdicts, over garments whose fingerprints are written by hand. No files, no CodeWalker — just the
/// numbers the comparison judges.
/// </summary>
public class DuplicateFinderTests
{
    static readonly IDuplicateFinder Finder =
        new ServiceCollection().AddDubleCore().BuildServiceProvider().GetRequiredService<IDuplicateFinder>();

    /// <summary>A shape histogram with its mass in one bucket, so two garments differ exactly as far as asked.</summary>
    static float[] Shape(int peak)
    {
        var histogram = new float[GeometryFingerprint.HistogramBuckets];
        histogram[peak] = 0.7f;
        histogram[Math.Min(GeometryFingerprint.HistogramBuckets - 1, peak + 1)] = 0.3f;
        return histogram;
    }

    static Garment Make(string pack, string slot, int number, string positionHash,
                        int triangles, int vertices, float[] shape, params string[] textureHashes)
    {
        var garment = new Garment
        {
            Id = $"{pack}|k.rpf|{slot}|{number}|u",
            PackName = pack,
            Container = "k.rpf",
            Slot = slot,
            Number = number,
            Suffix = "u",
            Geometry = new GeometryFingerprint
            {
                PositionHash = positionHash,
                Triangles = triangles,
                Vertices = vertices,
                ShapeHistogram = shape,
                BoundingBox = new[] { 0.5f, 0.3f, 0.6f },
                LodLevels = 3,
            },
        };

        char letter = 'a';
        foreach (var hash in textureHashes)
            garment.Textures.Add(new TextureInfo
            {
                FileName = $"{slot}_diff_{number:d3}_{letter++}_uni.ytd",
                Sha256 = hash,
                Width = 1024, Height = 1024, MipLevels = 11, Format = "BC3",
                IsDecoded = true, Variance = 30,
                PerceptualHash = new ulong[] { 1, 2, 3, 4 },
                ColorSignature = Convert.ToBase64String(new byte[192]),
            });

        return garment;
    }

    static ComparisonResult Compare(Thresholds thresholds, params Garment[] garments)
    {
        var catalog = new Catalog();
        catalog.Upsert(garments);
        return Finder.Find(catalog, thresholds);
    }

    [Fact]
    public void The_same_model_with_the_same_textures_is_a_duplicate_and_the_better_copy_wins()
    {
        var a = Make("p1", "jbib", 1, "H1", 1000, 600, Shape(10), "S1", "S2");
        var b = Make("p2", "jbib", 7, "H1", 1000, 600, Shape(10), "S1", "S2");
        b.Textures.ForEach(texture => texture.MipLevels = 1);   // b is the worse copy: no mipmaps

        var group = Assert.Single(Compare(null, a, b).Groups);
        Assert.Equal(Verdict.Duplicate, group.Verdict);
        Assert.Equal(a.Id, group.Winner);
        Assert.Equal("SAME_MODEL_SAME_TEX", group.Pairs[0].Reason.Code);
        Assert.Equal(DuplicateGroup.ComputeId(new[] { a.Id, b.Id }), group.Id);
    }

    [Fact]
    public void The_same_model_whose_textures_contain_the_others_is_a_superset()
    {
        var a = Make("p1", "jbib", 1, "H1", 1000, 600, Shape(10), "S1", "S2", "S3");
        var b = Make("p2", "jbib", 7, "H1", 1000, 600, Shape(10), "S1", "S2");

        var group = Assert.Single(Compare(null, a, b).Groups);
        Assert.Equal(Verdict.Superset, group.Verdict);
        Assert.Equal("SAME_MODEL_SUBSET", group.Pairs[0].Reason.Code);
    }

    [Fact]
    public void The_same_model_with_different_textures_is_a_retexture_and_never_a_duplicate()
    {
        var a = Make("p1", "jbib", 1, "H1", 1000, 600, Shape(10), "S1", "S2");
        var b = Make("p2", "jbib", 7, "H1", 1000, 600, Shape(10), "S8", "S9");
        b.Textures.ForEach(texture =>
        {
            texture.PerceptualHash = new ulong[] { ulong.MaxValue, 0, ulong.MaxValue, 0 };
            texture.ColorSignature = Convert.ToBase64String(Enumerable.Repeat((byte)200, 192).ToArray());
        });

        var group = Assert.Single(Compare(null, a, b).Groups);
        Assert.Equal(Verdict.Retexture, group.Verdict);
    }

    [Fact]
    public void A_similar_outline_with_a_different_triangle_count_is_not_even_a_candidate()
    {
        // the gloves: hand_000 has 3560 triangles and hand_025 has 2480, yet their outlines nearly agree
        var a = Make("p1", "hand", 0, "H1", 3560, 2000, Shape(10), "S1");
        var b = Make("p1", "hand", 25, "H2", 2480, 1500, Shape(10), "S1");

        Assert.Empty(Compare(null, a, b).Groups);
    }

    [Fact]
    public void A_project_can_override_the_thresholds()
    {
        var a = Make("p1", "jbib", 1, "H1", 1000, 600, Shape(10), "S1", "S2");
        var b = Make("p2", "jbib", 7, "H2", 1000, 600, Shape(12), "S1", "S2");   // different buckets: distance 2.0, the maximum
        Assert.Empty(Compare(null, a, b).Groups);

        var loose = new Thresholds { GeometrySimilar = 2.5, GeometryIdentical = 2.5 };
        var group = Assert.Single(Compare(loose, a, b).Groups);
        Assert.Equal(Verdict.Duplicate, group.Verdict);
    }

    [Fact]
    public void Comparing_reports_progress_and_can_be_cancelled()
    {
        var catalog = new Catalog();
        catalog.Upsert(new[]
        {
            Make("p1", "jbib", 1, "H1", 1000, 600, Shape(10), "S1"),
            Make("p2", "jbib", 2, "H2", 1000, 600, Shape(20), "S2"),
            Make("p3", "jbib", 3, "H3", 1000, 600, Shape(30), "S3"),
        });

        // SyncProgress, nie Progress: ten drugi oddaje wywolania na pule watkow, wiec asercja tuz po Find
        // potrafi wykonac sie zanim ostatni raport dojdzie
        var progress = new List<ProgressReport>();
        Finder.Find(catalog, null, new SyncProgress<ProgressReport>(progress.Add));
        Assert.Contains(progress, report => report.Stage == "compare" && report.Done == 3 && report.Total == 3);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Finder.Find(catalog, null, null, cancellation.Token));
    }

    [Fact]
    public void A_group_id_depends_on_who_is_in_it_and_not_on_their_order()
    {
        Assert.Equal(DuplicateGroup.ComputeId(new[] { "b", "a", "c" }),
                     DuplicateGroup.ComputeId(new[] { "c", "b", "a" }));
        Assert.NotEqual(DuplicateGroup.ComputeId(new[] { "a", "b" }),
                        DuplicateGroup.ComputeId(new[] { "a", "c" }));
        Assert.Equal(16, DuplicateGroup.ComputeId(new[] { "x" }).Length);
    }

    [Fact]
    public void The_default_thresholds_are_the_ones_the_calibration_produced()
    {
        var thresholds = Thresholds.Default;
        Assert.Equal(0.02, thresholds.GeometryIdentical);
        Assert.Equal(0.10, thresholds.GeometrySimilar);
        Assert.Equal(0.05, thresholds.GeometryTriangleTolerance);
        Assert.Equal(0.15, thresholds.GeometryBoundsTolerance);
        Assert.Equal(20, thresholds.TextureHashDistance);
        Assert.Equal(3.0, thresholds.TextureColorDistance);
        Assert.Equal(3.0f, thresholds.FlatTextureVariance);
        Assert.Equal(1.0, thresholds.FlatTextureColorDistance);
        Assert.Equal(0.95, thresholds.FullCoverage);
        Assert.Equal(0.5, thresholds.PartialCoverage);
    }

    [Fact]
    public void Thresholds_can_be_compared_copied_and_checked_for_sense()
    {
        var thresholds = Thresholds.Default;
        Assert.Empty(thresholds.Validate());

        var copy = thresholds.Clone();
        Assert.True(thresholds.SameAs(copy));
        Assert.NotSame(thresholds, copy);

        copy.TextureHashDistance = 24;
        Assert.False(thresholds.SameAs(copy));
        Assert.Empty(copy.Validate());

        copy.TextureHashDistance = 300;      // above the 256 bits there are
        copy.GeometrySimilar = 0.01;         // stricter than "identical", which cannot be
        copy.PartialCoverage = 0.99;         // above full coverage
        copy.TextureColorDistance = -1;
        var bad = copy.Validate();
        Assert.Contains("TextureHashDistance", bad);
        Assert.Contains("GeometrySimilar", bad);
        Assert.Contains("PartialCoverage", bad);
        Assert.Contains("TextureColorDistance", bad);
        Assert.DoesNotContain("GeometryIdentical", bad);

        Assert.False(thresholds.SameAs(null));
    }
}
