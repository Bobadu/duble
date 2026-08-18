#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duble.App;

namespace Duble.Tests;

/// <summary>
/// Made-up garments with dummy files on disk, for the tests of the commands, the report and applying. No game
/// files are involved: the fingerprints are written by hand so that the comparison has something definite to
/// find.
///
/// SevenGarments builds three groups: DUPLICATE a=b (jbib 1 and 7), RETEXTURE c=d (lowr 3 and 9) and
/// DUPLICATE e=f=g (feet 5, 6 and 8).
/// </summary>
public static class SampleData
{
    public static float[] Histogram(int peak)
    {
        var histogram = new float[GeometryFingerprint.HistogramBuckets];
        histogram[peak] = 0.7f;
        histogram[Math.Min(GeometryFingerprint.HistogramBuckets - 1, peak + 1)] = 0.3f;
        return histogram;
    }

    /// <summary>One garment in &lt;temp&gt;\&lt;pack&gt;\k.rpf\, with a 100 byte .ydd and a 50 byte .ytd per texture.</summary>
    public static Garment OneGarment(string temp, string pack, string slot, int number, string positionHash,
                                     float[] histogram, string sourceId, params string[] textureShas)
    {
        var folder = Path.Combine(temp, pack, "k.rpf");
        Directory.CreateDirectory(folder);

        var model = Path.Combine(folder, $"{slot}_{number:d3}_u.ydd");
        File.WriteAllBytes(model, new byte[100]);

        var garment = new Garment
        {
            Id = $"{pack}|k.rpf|{slot}|{number}|u",
            PackName = pack, Container = "k.rpf", Slot = slot, Number = number, Suffix = "u", SourceId = sourceId,
            ModelPath = model, ModelSize = 100,
            Geometry = new GeometryFingerprint
            {
                PositionHash = positionHash, Triangles = 1000, Vertices = 600,
                ShapeHistogram = histogram, BoundingBox = new[] { 0.5f, 0.3f, 0.6f }, LodLevels = 3,
            },
        };

        char letter = 'a';
        foreach (var sha in textureShas)
        {
            var file = Path.Combine(folder, $"{slot}_diff_{number:d3}_{letter++}_uni.ytd");
            File.WriteAllBytes(file, new byte[50]);
            garment.Textures.Add(new TextureInfo
            {
                FileName = Path.GetFileName(file), Path = file, Sha256 = sha + pack + number, Size = 50,
                Width = 1024, Height = 1024, MipLevels = 11, Format = "BC3", IsDecoded = true, Variance = 30,
                PerceptualHash = new ulong[] { 1, 2, 3, 4 }, ColorSignature = Convert.ToBase64String(new byte[192]),
            });
        }

        return garment;
    }

    public static List<Garment> SevenGarments(string temp, string sourceId = "z1")
    {
        var a = OneGarment(temp, "p1", "jbib", 1, "H1", Histogram(10), sourceId, "S1", "S2");
        var b = OneGarment(temp, "p2", "jbib", 7, "H1", Histogram(10), sourceId, "S1", "S2");
        b.Textures.ForEach(texture => texture.MipLevels = 1);

        var c = OneGarment(temp, "p1", "lowr", 3, "H3", Histogram(20), sourceId, "T1");
        var d = OneGarment(temp, "p2", "lowr", 9, "H3", Histogram(20), sourceId, "U1");
        d.Textures.ForEach(texture =>
        {
            texture.PerceptualHash = new ulong[] { ulong.MaxValue, 0, ulong.MaxValue, 0 };
            texture.ColorSignature = Convert.ToBase64String(Enumerable.Repeat((byte)200, 192).ToArray());
        });

        var e = OneGarment(temp, "p1", "feet", 5, "H5", Histogram(30), sourceId, "V1");
        var f = OneGarment(temp, "p2", "feet", 6, "H5", Histogram(30), sourceId, "V1");
        f.Textures.ForEach(texture => texture.MipLevels = 1);
        var g = OneGarment(temp, "p3", "feet", 8, "H5", Histogram(30), sourceId, "V1");
        g.Textures.ForEach(texture => texture.MipLevels = 1);

        // the same graphics between packs: OneGarment makes every sha unique, so the pairs are levelled here
        for (int i = 0; i < b.Textures.Count; i++) b.Textures[i].Sha256 = a.Textures[i].Sha256;
        foreach (var texture in f.Textures.Concat(g.Textures)) texture.Sha256 = e.Textures[0].Sha256;

        return new List<Garment> { a, b, c, d, e, f, g };
    }

    /// <summary>
    /// The seven garments as three folder sources p1, p2 and p3 of an open project, with matching SourceIds —
    /// the way the application has them, so that re-indexing after an apply replaces the right garments.
    /// </summary>
    public static List<Garment> SevenGarmentsInThreeSources(Session session, string temp)
    {
        var garments = SevenGarments(temp);
        foreach (var pack in new[] { "p1", "p2", "p3" })
        {
            Directory.CreateDirectory(Path.Combine(temp, pack));
            session.Project!.Sources.Add(new ProjectSource
            {
                Id = "z-" + pack, Name = pack, Path = Path.Combine(temp, pack), Kind = SourceKind.Folder, Enabled = true,
            });
        }
        foreach (var garment in garments) garment.SourceId = "z-" + garment.PackName;
        session.EditCatalog(catalog => catalog.Upsert(garments));
        return garments;
    }
}
