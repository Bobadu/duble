#nullable enable
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

    static GeometryFingerprint Compute(Drawable? d)
    {
        var g = new GeometryFingerprint();
        if (d == null) return g;

        var dm = d.DrawableModels;
        if (dm != null)
        {
            foreach (var arr in new[] { dm.High, dm.Med, dm.Low, dm.VLow })
                if (arr != null && arr.Length > 0) g.LodLevels++;
        }
        g.Bones = d.Skeleton?.Bones?.Items?.Length ?? 0;
        g.BoundingBox = new[]
        {
            d.BoundingBoxMax.X - d.BoundingBoxMin.X,
            d.BoundingBoxMax.Y - d.BoundingBoxMin.Y,
            d.BoundingBoxMax.Z - d.BoundingBoxMin.Z
        };

        // Odcisk liczymy WYLACZNIE z najwyzszego LOD — nizsze bywaja generowane
        // automatycznie przez rozne narzedzia i roznia sie tam, gdzie ciuch jest ten sam.
        var modele = dm?.High;
        if (modele == null || modele.Length == 0) return g;

        var poz = new List<(float x, float y, float z)>();
        foreach (var m in modele)
        {
            if (m?.Geometries == null) continue;
            foreach (var geo in m.Geometries)
            {
                if (geo == null) continue;
                g.Meshes++;
                g.Triangles += (int)(geo.IndicesCount / 3);
                var vd = geo.VertexBuffer?.Data1 ?? geo.VertexBuffer?.Data2;
                if (vd?.VertexBytes == null || vd.Info == null) continue;
                g.Stride = vd.Info.Stride;
                int stride = vd.Info.Stride;
                int off = vd.Info.GetComponentOffset(0);   // skladowa 0 = pozycja
                int n = vd.VertexCount;
                g.Vertices += n;
                var b = vd.VertexBytes;
                for (int v = 0; v < n; v++)
                {
                    int o = v * stride + off;
                    if (o + 12 > b.Length) break;
                    poz.Add((BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8)));
                }
            }
        }
        if (poz.Count == 0) return g;

        // --- histogram odleglosci od srodka ciezkosci ---
        double sx = 0, sy = 0, sz = 0;
        foreach (var p in poz) { sx += p.x; sy += p.y; sz += p.z; }
        double cx = sx / poz.Count, cy = sy / poz.Count, cz = sz / poz.Count;

        var odl = new double[poz.Count];
        double suma = 0;
        for (int i = 0; i < poz.Count; i++)
        {
            double dx = poz[i].x - cx, dy = poz[i].y - cy, dz = poz[i].z - cz;
            odl[i] = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            suma += odl[i];
        }
        double srednia = suma / poz.Count;
        var hist = new float[GeometryFingerprint.HistogramBuckets];
        if (srednia > 1e-9)
        {
            foreach (var o in odl)
            {
                int k = (int)(o / srednia / GeometryFingerprint.HistogramRange * GeometryFingerprint.HistogramBuckets);
                if (k < 0) k = 0; else if (k >= GeometryFingerprint.HistogramBuckets) k = GeometryFingerprint.HistogramBuckets - 1;
                hist[k]++;
            }
            for (int i = 0; i < GeometryFingerprint.HistogramBuckets; i++) hist[i] /= poz.Count;
        }
        g.ShapeHistogram = hist;

        // --- hash z posortowanych pozycji zaokraglonych do 1 mm ---
        // 1 mm, nie 0,1 mm: ponowny eksport przez Blendera/Maxa wnosi szum wiekszy niz
        // 0,1 mm, a ciuch ma ~0,5 m, wiec 1 mm to nadal 0,2% rozmiaru — bardzo selektywne.
        var klucze = new long[poz.Count];
        for (int i = 0; i < poz.Count; i++)
        {
            long qx = ToMillimetreKey(poz[i].x), qy = ToMillimetreKey(poz[i].y), qz = ToMillimetreKey(poz[i].z);
            klucze[i] = (qx << 32) | (qy << 16) | qz;
        }
        Array.Sort(klucze);
        var bajty = new byte[klucze.Length * 8];
        Buffer.BlockCopy(klucze, 0, bajty, 0, bajty.Length);
        g.PositionHash = Convert.ToHexString(SHA256.HashData(bajty)).Substring(0, 32);
        return g;
    }

    static long ToMillimetreKey(float v)
    {
        long mm = (long)Math.Round(v * 1000.0);
        if (mm < -32768) mm = -32768; else if (mm > 32767) mm = 32767;
        return mm + 32768;   // przesuniecie na zakres bez znaku, zeby zmiescic w 16 bitach
    }

}
