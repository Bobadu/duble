using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using CodeWalker.GameFiles;
using Duble.Core.Formats;
using Duble.Core.Model;
using Duble.Core.Results;

namespace Duble.Core.Fingerprints;

/// <summary>Turns a model file into the numbers that describe its shape.</summary>
public interface IGeometryFingerprinter
{
    /// <summary>
    /// The fingerprint of the first drawable in a .ydd. A file that cannot be read comes back as
    /// model.unreadable; a readable file with no geometry gives an empty fingerprint, which is not an error —
    /// plenty of packs contain them.
    /// </summary>
    Result<GeometryFingerprint> Compute(byte[] modelBytes);
}

/// <summary>
/// ASSUMPTIONS CONFIRMED BY MEASUREMENT OVER OUR OWN SOURCES (15.08):
///  - a vertex position is always component 0 (Float3, offset 0) — checked by comparing the min/max over the
///    vertices with the drawable's own bounding box: they agree to within 0.001
///  - gen9 files need RpfManager.IsGen9, which CodeWalkerRuntime sets once; in that mode CodeWalker reads
///    Legacy correctly as well, by the version in each RSC7 header
/// </summary>
public sealed class GeometryFingerprinter : IGeometryFingerprinter
{
    public GeometryFingerprinter(CodeWalkerRuntime runtime) => _ = runtime;

    public Result<GeometryFingerprint> Compute(byte[] modelBytes)
    {
        try
        {
            var ydd = new YddFile();
            RpfFile.LoadResourceFile(ydd, modelBytes, 165);
            return Result<GeometryFingerprint>.Ok(Compute(ydd.Drawables is { Length: > 0 } d ? d[0] : null));
        }
        catch (Exception e)
        {
            return Result<GeometryFingerprint>.Fail(ErrorCodes.ModelUnreadable, e.Message);
        }
    }

    static GeometryFingerprint Compute(Drawable? drawable)
    {
        var fingerprint = new GeometryFingerprint();
        if (drawable == null) return fingerprint;

        var models = drawable.DrawableModels;
        if (models != null)
            foreach (var lod in new[] { models.High, models.Med, models.Low, models.VLow })
                if (lod is { Length: > 0 }) fingerprint.LodLevels++;

        fingerprint.Bones = drawable.Skeleton?.Bones?.Items?.Length ?? 0;
        fingerprint.BoundingBox = new[]
        {
            drawable.BoundingBoxMax.X - drawable.BoundingBoxMin.X,
            drawable.BoundingBoxMax.Y - drawable.BoundingBoxMin.Y,
            drawable.BoundingBoxMax.Z - drawable.BoundingBoxMin.Z,
        };

        // The fingerprint comes from the HIGHEST LOD ONLY. Lower ones are often generated automatically by
        // whichever tool the author used, so they differ between two copies of the same garment.
        var highest = models?.High;
        if (highest == null || highest.Length == 0) return fingerprint;

        var positions = ReadPositions(highest, fingerprint);
        if (positions.Count == 0) return fingerprint;

        fingerprint.ShapeHistogram = ShapeHistogram(positions);
        fingerprint.PositionHash = PositionHash(positions);
        return fingerprint;
    }

    /// <summary>
    /// Every vertex position in the given models, counting meshes, triangles and vertices along the way. A
    /// position is component 0 of the vertex layout — three floats at that component's offset.
    /// </summary>
    static List<(float X, float Y, float Z)> ReadPositions(DrawableModel[] models, GeometryFingerprint fingerprint)
    {
        var positions = new List<(float X, float Y, float Z)>();

        foreach (var model in models)
        {
            if (model?.Geometries == null) continue;
            foreach (var geometry in model.Geometries)
            {
                if (geometry == null) continue;
                fingerprint.Meshes++;
                fingerprint.Triangles += (int)(geometry.IndicesCount / 3);

                var buffer = geometry.VertexBuffer?.Data1 ?? geometry.VertexBuffer?.Data2;
                if (buffer?.VertexBytes == null || buffer.Info == null) continue;

                fingerprint.Stride = buffer.Info.Stride;
                int stride = buffer.Info.Stride;
                int offset = buffer.Info.GetComponentOffset(0);
                fingerprint.Vertices += buffer.VertexCount;

                var bytes = buffer.VertexBytes;
                for (int vertex = 0; vertex < buffer.VertexCount; vertex++)
                {
                    int at = vertex * stride + offset;
                    if (at + 12 > bytes.Length) break;
                    positions.Add((BitConverter.ToSingle(bytes, at),
                                   BitConverter.ToSingle(bytes, at + 4),
                                   BitConverter.ToSingle(bytes, at + 8)));
                }
            }
        }

        return positions;
    }

    /// <summary>
    /// How far the vertices sit from the centre of mass, as a histogram normalised by the mean distance. That
    /// normalisation is what makes the histogram independent of scale and of where the model sits in space.
    /// </summary>
    static float[] ShapeHistogram(List<(float X, float Y, float Z)> positions)
    {
        double sumX = 0, sumY = 0, sumZ = 0;
        foreach (var position in positions) { sumX += position.X; sumY += position.Y; sumZ += position.Z; }
        double centreX = sumX / positions.Count, centreY = sumY / positions.Count, centreZ = sumZ / positions.Count;

        var distances = new double[positions.Count];
        double total = 0;
        for (int i = 0; i < positions.Count; i++)
        {
            double dx = positions[i].X - centreX, dy = positions[i].Y - centreY, dz = positions[i].Z - centreZ;
            distances[i] = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            total += distances[i];
        }

        var histogram = new float[GeometryFingerprint.HistogramBuckets];
        double mean = total / positions.Count;
        if (mean <= 1e-9) return histogram;   // every vertex in one place: nothing to describe

        foreach (var distance in distances)
        {
            int bucket = (int)(distance / mean / GeometryFingerprint.HistogramRange * GeometryFingerprint.HistogramBuckets);
            bucket = Math.Clamp(bucket, 0, GeometryFingerprint.HistogramBuckets - 1);
            histogram[bucket]++;
        }
        for (int i = 0; i < histogram.Length; i++) histogram[i] /= positions.Count;

        return histogram;
    }

    /// <summary>
    /// A hash over the sorted vertex positions, rounded to a millimetre. Equal hashes mean the same mesh,
    /// whatever order the vertices were written in.
    ///
    /// A millimetre, not a tenth of one: re-exporting through Blender or Max introduces more noise than 0.1 mm.
    /// A garment is about half a metre across, so a millimetre is still 0.2% of it — selective enough.
    /// </summary>
    static string PositionHash(List<(float X, float Y, float Z)> positions)
    {
        var keys = new long[positions.Count];
        for (int i = 0; i < positions.Count; i++)
            keys[i] = (ToMillimetreKey(positions[i].X) << 32)
                    | (ToMillimetreKey(positions[i].Y) << 16)
                    | ToMillimetreKey(positions[i].Z);

        Array.Sort(keys);
        var bytes = new byte[keys.Length * 8];
        Buffer.BlockCopy(keys, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes))[..32];
    }

    static long ToMillimetreKey(float value)
    {
        long millimetres = Math.Clamp((long)Math.Round(value * 1000.0), -32768, 32767);
        return millimetres + 32768;   // shifted into an unsigned range so it fits in 16 bits
    }
}
