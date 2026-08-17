using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Duble.Tests;

/// <summary>Werdykty na SZTUCZNYCH pozycjach (odciski wpisane recznie) — bez plikow, bez CodeWalkera.</summary>
public class DuplicateFinderTests
{
    static readonly IDuplicateFinder Finder =
        new ServiceCollection().AddDubleCore().BuildServiceProvider().GetRequiredService<IDuplicateFinder>();

    static float[] Hist(int szczyt) { var h = new float[GeometryFingerprint.HistogramBuckets]; h[szczyt] = 0.7f; h[Math.Min(GeometryFingerprint.HistogramBuckets - 1, szczyt + 1)] = 0.3f; return h; }

    static Garment Poz(string paczka, string typ, int numer, string hashPoz, int tri, int wierz, float[] hist, params string[] shaTekstur)
    {
        var p = new Garment
        {
            Id = $"{paczka}|k.rpf|{typ}|{numer}|u", PackName = paczka, Container = "k.rpf", Slot = typ, Number = numer, Suffix = "u",
            Geometry = new GeometryFingerprint { PositionHash = hashPoz, Triangles = tri, Vertices = wierz, ShapeHistogram = hist, BoundingBox = new[] { 0.5f, 0.3f, 0.6f }, LodLevels = 3 },
        };
        char litera = 'a';
        foreach (var sha in shaTekstur)
            p.Textures.Add(new TextureInfo { FileName = $"{typ}_diff_{numer:d3}_{litera++}_uni.ytd", Sha256 = sha, Width = 1024, Height = 1024, MipLevels = 11, Format = "BC3", IsDecoded = true, Variance = 30, PerceptualHash = new ulong[] { 1, 2, 3, 4 }, ColorSignature = Convert.ToBase64String(new byte[192]) });
        return p;
    }

    static ComparisonResult Uruchom(Thresholds progi, params Garment[] poz)
    {
        var k = new Catalog(); k.Upsert(poz);
        return Finder.Find(k, progi);
    }

    [Fact]
    public void Ten_sam_model_i_te_same_tekstury_to_duplikat_z_lepszym_zwyciezca()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2");
        var b = Poz("p2", "jbib", 7, "H1", 1000, 600, Hist(10), "S1", "S2");
        b.Textures.ForEach(t => t.MipLevels = 1);   // b gorsze: bez mipow
        var w = Uruchom(null, a, b);
        var g = Assert.Single(w.Groups);
        Assert.Equal(Verdict.Duplicate, g.Verdict);
        Assert.Equal(a.Id, g.Winner);
        Assert.Equal("SAME_MODEL_SAME_TEX", g.Pairs[0].Reason.Code);
        Assert.Equal(DuplicateGroup.ComputeId(new[] { a.Id, b.Id }), g.Id);
    }

    [Fact]
    public void Ten_sam_model_podzbior_tekstur_to_nadzbior()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2", "S3");
        var b = Poz("p2", "jbib", 7, "H1", 1000, 600, Hist(10), "S1", "S2");
        var g = Assert.Single(Uruchom(null, a, b).Groups);
        Assert.Equal(Verdict.Superset, g.Verdict);
        Assert.Equal("SAME_MODEL_SUBSET", g.Pairs[0].Reason.Code);
    }

    [Fact]
    public void Ten_sam_model_inne_tekstury_to_przemalowanie_nie_duplikat()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2");
        var b = Poz("p2", "jbib", 7, "H1", 1000, 600, Hist(10), "S8", "S9");
        b.Textures.ForEach(t => { t.PerceptualHash = new ulong[] { ulong.MaxValue, 0, ulong.MaxValue, 0 }; t.ColorSignature = Convert.ToBase64String(Enumerable.Repeat((byte)200, 192).ToArray()); });
        var g = Assert.Single(Uruchom(null, a, b).Groups);
        Assert.Equal(Verdict.Retexture, g.Verdict);
    }

    [Fact]
    public void Podobny_obrys_ale_inna_liczba_trojkatow_nie_jest_kandydatem()
    {
        // przypadek rekawiczek hand_000 (3560 tri) vs hand_025 (2480 tri): histogram blisko, ale to rozne modele
        var a = Poz("p1", "hand", 0, "H1", 3560, 2000, Hist(10), "S1");
        var b = Poz("p1", "hand", 25, "H2", 2480, 1500, Hist(10), "S1");
        Assert.Empty(Uruchom(null, a, b).Groups);
    }

    [Fact]
    public void Progi_mozna_nadpisac()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2");
        var b = Poz("p2", "jbib", 7, "H2", 1000, 600, Hist(12), "S1", "S2");   // hist rozny -> odleglosc 2.0 (max)
        Assert.Empty(Uruchom(null, a, b).Groups);
        var luzne = new Thresholds { GeometrySimilar = 2.5, GeometryIdentical = 2.5 };
        var g = Assert.Single(Uruchom(luzne, a, b).Groups);
        Assert.Equal(Verdict.Duplicate, g.Verdict);
    }

    [Fact]
    public void Znajdz_zglasza_postep_i_da_sie_anulowac()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1");
        var b = Poz("p2", "jbib", 2, "H2", 1000, 600, Hist(20), "S2");
        var c = Poz("p3", "jbib", 3, "H3", 1000, 600, Hist(30), "S3");
        var k = new Catalog(); k.Upsert(new[] { a, b, c });
        var postepy = new List<ProgressReport>();
        Finder.Find(k, null, new Progress<ProgressReport>(postepy.Add));
        Assert.Contains(postepy, p => p.Stage == "compare" && p.Done == 3 && p.Total == 3);
        var cts = new System.Threading.CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Finder.Find(k, null, null, cts.Token));
    }

    [Fact]
    public void Id_grupy_nie_zalezy_od_kolejnosci_czlonkow()
    {
        Assert.Equal(DuplicateGroup.ComputeId(new[] { "b", "a", "c" }), DuplicateGroup.ComputeId(new[] { "c", "b", "a" }));
        Assert.NotEqual(DuplicateGroup.ComputeId(new[] { "a", "b" }), DuplicateGroup.ComputeId(new[] { "a", "c" }));
        Assert.Equal(16, DuplicateGroup.ComputeId(new[] { "x" }).Length);
    }

    [Fact]
    public void Domyslne_progi_to_kalibracja_15_08()
    {
        var p = Thresholds.Default;
        Assert.Equal(0.02, p.GeometryIdentical); Assert.Equal(0.10, p.GeometrySimilar); Assert.Equal(0.05, p.GeometryTriangleTolerance); Assert.Equal(0.15, p.GeometryBoundsTolerance);
        Assert.Equal(20, p.TextureHashDistance); Assert.Equal(3.0, p.TextureColorDistance); Assert.Equal(3.0f, p.FlatTextureVariance); Assert.Equal(1.0, p.FlatTextureColorDistance);
        Assert.Equal(0.95, p.FullCoverage); Assert.Equal(0.5, p.PartialCoverage);
    }

    [Fact]
    public void Progi_sprawdz_kopia_rowne()
    {
        var p = Thresholds.Default;
        Assert.Empty(p.Validate());
        var k = p.Clone(); Assert.True(p.SameAs(k)); Assert.NotSame(p, k);
        k.TextureHashDistance = 24; Assert.False(p.SameAs(k)); Assert.Empty(k.Validate());
        k.TextureHashDistance = 300; k.GeometrySimilar = 0.01; k.PartialCoverage = 0.99; k.TextureColorDistance = -1;
        var b = k.Validate();
        Assert.Contains("TextureHashDistance", b); Assert.Contains("GeometrySimilar", b); Assert.Contains("PartialCoverage", b); Assert.Contains("TextureColorDistance", b);
        Assert.DoesNotContain("GeometryIdentical", b);
        Assert.False(p.SameAs(null));
    }
}
