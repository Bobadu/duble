using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Duble.Tests;

/// <summary>Sztuczne pozycje z plikami-atrapami na dysku (bez plikow gry) do testow komend, raportu i zastosowania.
/// Siedem(): 3 grupy — DUPLIKAT a=b (jbib 1/7), PRZEMALOWANIE c=d (lowr 3/9), DUPLIKAT e=f=g (feet 5/6/8).</summary>
public static class Sztuczne
{
    public static float[] Hist(int szczyt) { var h = new float[GeometryFingerprint.HistogramBuckets]; h[szczyt] = 0.7f; h[Math.Min(GeometryFingerprint.HistogramBuckets - 1, szczyt + 1)] = 0.3f; return h; }

    /// <summary>Garment w &lt;tmp&gt;\&lt;paczka&gt;\k.rpf\ z plikami .ydd (100 B) i .ytd (50 B); ZrodloId = zrodloId.</summary>
    public static Garment Poz(string tmp, string paczka, string typ, int numer, string hashPoz, float[] hist, string zrodloId, params string[] shaTekstur)
    {
        var folder = Path.Combine(tmp, paczka, "k.rpf"); Directory.CreateDirectory(folder);
        var ydd = Path.Combine(folder, $"{typ}_{numer:d3}_u.ydd"); File.WriteAllBytes(ydd, new byte[100]);
        var p = new Garment
        {
            Id = $"{paczka}|k.rpf|{typ}|{numer}|u", PackName = paczka, Container = "k.rpf", Slot = typ, Number = numer, Suffix = "u", SourceId = zrodloId,
            ModelPath = ydd, ModelSize = 100,
            Geometry = new GeometryFingerprint { PositionHash = hashPoz, Triangles = 1000, Vertices = 600, ShapeHistogram = hist, BoundingBox = new[] { 0.5f, 0.3f, 0.6f }, LodLevels = 3 },
        };
        char litera = 'a';
        foreach (var sha in shaTekstur)
        {
            var plik = Path.Combine(folder, $"{typ}_diff_{numer:d3}_{litera++}_uni.ytd"); File.WriteAllBytes(plik, new byte[50]);
            p.Textures.Add(new TextureInfo { FileName = Path.GetFileName(plik), Path = plik, Sha256 = sha + paczka + numer, Size = 50, Width = 1024, Height = 1024, MipLevels = 11, Format = "BC3", IsDecoded = true, Variance = 30, PerceptualHash = new ulong[] { 1, 2, 3, 4 }, ColorSignature = Convert.ToBase64String(new byte[192]) });
        }
        return p;
    }

    /// <summary>Project sesji z trzema zrodlami-folderami p1/p2/p3 (&lt;tmp&gt;\p1 …; Id "z-p1"…) i siedmioma pozycjami o pasujacych ZrodloId —
    /// tak jak w aplikacji (Paczka == nazwa zrodla), zeby ponowne indeksowanie po Zastosuj podmienialo wlasciwe pozycje.</summary>
    public static List<Garment> SiedemZeZrodlami(Duble.App.Sesja s, string tmp)
    {
        var poz = Siedem(tmp);
        foreach (var paczka in new[] { "p1", "p2", "p3" })
        {
            Directory.CreateDirectory(Path.Combine(tmp, paczka));
            s.Project.Sources.Add(new ProjectSource { Id = "z-" + paczka, Name = paczka, Path = Path.Combine(tmp, paczka), Kind = SourceKind.Folder, Enabled = true });
        }
        foreach (var p in poz) p.SourceId = "z-" + p.PackName;
        s.ZmienKatalog(k => k.Upsert(poz));
        return poz;
    }

    public static List<Garment> Siedem(string tmp, string zrodloId = "z1")
    {
        var a = Poz(tmp, "p1", "jbib", 1, "H1", Hist(10), zrodloId, "S1", "S2");
        var b = Poz(tmp, "p2", "jbib", 7, "H1", Hist(10), zrodloId, "S1", "S2");
        b.Textures.ForEach(t => t.MipLevels = 1);
        var c = Poz(tmp, "p1", "lowr", 3, "H3", Hist(20), zrodloId, "T1");
        var d = Poz(tmp, "p2", "lowr", 9, "H3", Hist(20), zrodloId, "U1");
        d.Textures.ForEach(t => { t.PerceptualHash = new ulong[] { ulong.MaxValue, 0, ulong.MaxValue, 0 }; t.ColorSignature = Convert.ToBase64String(Enumerable.Repeat((byte)200, 192).ToArray()); });
        var e = Poz(tmp, "p1", "feet", 5, "H5", Hist(30), zrodloId, "V1");
        var f = Poz(tmp, "p2", "feet", 6, "H5", Hist(30), zrodloId, "V1"); f.Textures.ForEach(t => t.MipLevels = 1);
        var g = Poz(tmp, "p3", "feet", 8, "H5", Hist(30), zrodloId, "V1"); g.Textures.ForEach(t => t.MipLevels = 1);
        // te same grafiki: SHA rowne miedzy paczkami (Sha = sha+paczka+numer — ujednolic dla par a/b, e/f/g)
        foreach (var t in b.Textures) t.Sha256 = a.Textures[b.Textures.IndexOf(t)].Sha256;
        foreach (var t in f.Textures) t.Sha256 = e.Textures[0].Sha256; foreach (var t in g.Textures) t.Sha256 = e.Textures[0].Sha256;
        return new List<Garment> { a, b, c, d, e, f, g };
    }
}

/// <summary>A clock stuck at one instant, for tests that assert on a written timestamp.</summary>
public sealed class FixedClock : Duble.Core.Time.IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; }
}
