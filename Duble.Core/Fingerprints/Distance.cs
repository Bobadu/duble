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

    /// <summary>
    /// Mean per-channel difference between two colour signatures, 0..255. Like the others, anything it cannot
    /// compare — a missing signature, a mismatched length, or base64 a hand-edited catalog has broken — comes
    /// back as the largest possible distance rather than as an exception.
    /// </summary>
    public static double Color(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return double.MaxValue;

        Span<byte> x = stackalloc byte[SignatureBytes];
        Span<byte> y = stackalloc byte[SignatureBytes];
        if (!Convert.TryFromBase64String(a, x, out int lengthA)) return double.MaxValue;
        if (!Convert.TryFromBase64String(b, y, out int lengthB)) return double.MaxValue;
        if (lengthA != lengthB || lengthA == 0) return double.MaxValue;

        double sum = 0;
        for (int i = 0; i < lengthA; i++) sum += Math.Abs(x[i] - y[i]);
        return sum / lengthA;
    }

    /// <summary>
    /// Room for a colour signature: an 8x8 RGB grid is 192 bytes. Decoding into a buffer of this size on the
    /// stack keeps the comparison free of allocations — it runs this over every candidate pair of textures, and
    /// the calibrator runs it 400 000 times.
    /// </summary>
    const int SignatureBytes = 256;
}
