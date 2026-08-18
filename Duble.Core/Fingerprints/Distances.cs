using System;

namespace Duble.Core.Fingerprints;

/// <summary>How far apart two fingerprints are. Pure arithmetic — the thresholds that judge it live in Thresholds.</summary>
public static class Distance
{
    /// <summary>L1 distance between two shape histograms. 0 means identical, 2 is the most they can differ.</summary>
    public static double ShapeHistogram(float[]? a, float[]? b)
    {
        if (a == null || b == null || a.Length != b.Length) return double.MaxValue;
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return s;
    }

    /// <summary>The largest relative difference between two bounding boxes. 0 means the same size.</summary>
    public static double BoundingBox(float[]? a, float[]? b)
    {
        if (a == null || b == null || a.Length != 3 || b.Length != 3) return double.MaxValue;
        double max = 0;
        for (int i = 0; i < 3; i++)
        {
            double m = Math.Max(Math.Abs(a[i]), Math.Abs(b[i]));
            if (m < 1e-6) continue;
            max = Math.Max(max, Math.Abs(a[i] - b[i]) / m);
        }
        return max;
    }

    // ===================== textures =====================

    /// <summary>Bits that differ between two perceptual hashes, or -1 when either is missing.</summary>
    public static int Hamming(ulong[]? a, ulong[]? b)
    {
        if (a == null || b == null || a.Length != b.Length) return -1;
        int s = 0;
        for (int i = 0; i < a.Length; i++) s += System.Numerics.BitOperations.PopCount(a[i] ^ b[i]);
        return s;
    }

    /// <summary>Srednia roznica sygnatur koloru w kanale (0..255).</summary>
    public static double Color(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return double.MaxValue;
        var x = Convert.FromBase64String(a);
        var y = Convert.FromBase64String(b);
        if (x.Length != y.Length) return double.MaxValue;
        double s = 0;
        for (int i = 0; i < x.Length; i++) s += Math.Abs(x[i] - y[i]);
        return s / x.Length;
    }
}
