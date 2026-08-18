#nullable enable
using System;
using System.Linq;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The four numbers every verdict rests on. Each of them has to answer "I cannot compare these" with the
/// largest possible distance rather than an exception — a catalog read from disk can hold anything.
/// </summary>
public class DistanceTests
{
    static float[] Histogram(params (int Bucket, float Share)[] mass)
    {
        var histogram = new float[GeometryFingerprint.HistogramBuckets];
        foreach (var (bucket, share) in mass) histogram[bucket] = share;
        return histogram;
    }

    static string Signature(params byte[] channels) => Convert.ToBase64String(channels);

    [Fact]
    public void Two_shape_histograms_are_zero_apart_when_equal_and_two_at_most_when_disjoint()
    {
        var a = Histogram((10, 0.7f), (11, 0.3f));
        var b = Histogram((10, 0.7f), (11, 0.3f));
        Assert.Equal(0, Distance.ShapeHistogram(a, b));

        // all the mass in different buckets: 1 of difference each way, so 2 — the most two histograms can differ
        Assert.Equal(2, Distance.ShapeHistogram(Histogram((0, 1f)), Histogram((30, 1f))), 6);

        Assert.Equal(double.MaxValue, Distance.ShapeHistogram(null, b));
        Assert.Equal(double.MaxValue, Distance.ShapeHistogram(a, null));
        Assert.Equal(double.MaxValue, Distance.ShapeHistogram(a, new float[] { 1, 2 }));
    }

    [Fact]
    public void Bounding_boxes_are_compared_by_their_largest_relative_difference()
    {
        Assert.Equal(0, Distance.BoundingBox(new[] { 0.5f, 0.3f, 0.6f }, new[] { 0.5f, 0.3f, 0.6f }));

        // one dimension is 10% off and the others agree, so the answer is that 10%
        Assert.Equal(0.1, Distance.BoundingBox(new[] { 1f, 1f, 1f }, new[] { 1f, 0.9f, 1f }), 6);

        // a dimension that is zero on both sides says nothing and must not divide by nothing
        Assert.Equal(0, Distance.BoundingBox(new[] { 1f, 0f, 1f }, new[] { 1f, 0f, 1f }));

        Assert.Equal(double.MaxValue, Distance.BoundingBox(null, new[] { 1f, 1f, 1f }));
        Assert.Equal(double.MaxValue, Distance.BoundingBox(new[] { 1f, 1f }, new[] { 1f, 1f, 1f }));
    }

    [Fact]
    public void The_hamming_distance_counts_differing_bits_and_reports_minus_one_when_it_cannot()
    {
        Assert.Equal(0, Distance.Hamming(new ulong[] { 1, 2, 3, 4 }, new ulong[] { 1, 2, 3, 4 }));
        Assert.Equal(1, Distance.Hamming(new ulong[] { 0, 0, 0, 0 }, new ulong[] { 1, 0, 0, 0 }));
        Assert.Equal(256, Distance.Hamming(new ulong[] { 0, 0, 0, 0 },
            Enumerable.Repeat(ulong.MaxValue, 4).ToArray()));

        Assert.Equal(-1, Distance.Hamming(null, new ulong[] { 1 }));
        Assert.Equal(-1, Distance.Hamming(new ulong[] { 1 }, null));
        Assert.Equal(-1, Distance.Hamming(new ulong[] { 1, 2 }, new ulong[] { 1 }));
    }

    [Fact]
    public void Colour_signatures_are_compared_by_their_mean_per_channel_difference()
    {
        Assert.Equal(0, Distance.Color(Signature(10, 20, 30), Signature(10, 20, 30)));
        Assert.Equal(255, Distance.Color(Signature(0, 0, 0), Signature(255, 255, 255)));
        Assert.Equal(10, Distance.Color(Signature(0, 0, 0), Signature(30, 0, 0)), 6);
    }

    /// <summary>
    /// The signature comes out of a catalog on disk. Anything that is not a signature has to read as "cannot
    /// compare" — before this, malformed base64 threw and took a whole comparison down with it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 at all !!")]
    [InlineData("QQ")]                       // valid base64, but one byte against three
    public void A_signature_that_is_not_one_reads_as_the_largest_possible_distance(string? broken)
    {
        var good = Signature(10, 20, 30);
        Assert.Equal(double.MaxValue, Distance.Color(broken, good));
        Assert.Equal(double.MaxValue, Distance.Color(good, broken));
    }

    [Fact]
    public void A_signature_longer_than_any_duble_writes_reads_as_the_largest_possible_distance()
    {
        var oversized = Convert.ToBase64String(new byte[4096]);
        Assert.Equal(double.MaxValue, Distance.Color(oversized, oversized));
    }
}
