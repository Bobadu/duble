using System;
using System.Collections.Generic;
using System.Linq;
using Duble;
using Xunit;

namespace Duble.Tests;

/// <summary>Werdykty na SZTUCZNYCH pozycjach (odciski wpisane recznie) — bez plikow, bez CodeWalkera.</summary>
public class PorownanieTests
{
    static float[] Hist(int szczyt) { var h = new float[Geo.Kubelki]; h[szczyt] = 0.7f; h[Math.Min(Geo.Kubelki - 1, szczyt + 1)] = 0.3f; return h; }

    static Pozycja Poz(string paczka, string typ, int numer, string hashPoz, int tri, int wierz, float[] hist, params string[] shaTekstur)
    {
        var p = new Pozycja
        {
            Id = $"{paczka}|k.rpf|{typ}|{numer}|u", Paczka = paczka, Kontener = "k.rpf", Typ = typ, Numer = numer, Sufiks = "u",
            Geo = new Geo { HashPozycji = hashPoz, Trojkaty = tri, Wierzcholki = wierz, Hist = hist, Bbox = new[] { 0.5f, 0.3f, 0.6f }, Lody = 3 },
        };
        char litera = 'a';
        foreach (var sha in shaTekstur)
            p.Tekstury.Add(new Tekstura { Plik = $"{typ}_diff_{numer:d3}_{litera++}_uni.ytd", Sha = sha, W = 1024, H = 1024, Mipy = 11, Format = "BC3", Zdekodowana = true, Wariancja = 30, PHash = new ulong[] { 1, 2, 3, 4 }, Kolor = Convert.ToBase64String(new byte[192]) });
        return p;
    }

    static WynikPorownania Uruchom(Progi progi, params Pozycja[] poz)
    {
        var k = new Katalog(); k.Wstaw(poz);
        return Porownanie.Znajdz(k, s => { }, progi);
    }

    [Fact]
    public void Ten_sam_model_i_te_same_tekstury_to_duplikat_z_lepszym_zwyciezca()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2");
        var b = Poz("p2", "jbib", 7, "H1", 1000, 600, Hist(10), "S1", "S2");
        b.Tekstury.ForEach(t => t.Mipy = 1);   // b gorsze: bez mipow
        var w = Uruchom(null, a, b);
        var g = Assert.Single(w.Grupy);
        Assert.Equal(Porownanie.Duplikat, g.Werdykt);
        Assert.Equal(a.Id, g.Zwyciezca);
        Assert.Equal("SAME_MODEL_SAME_TEX", g.Pary[0].Powod.Kod);
        Assert.Equal(Grupa.PoliczId(new[] { a.Id, b.Id }), g.Id);
    }

    [Fact]
    public void Ten_sam_model_podzbior_tekstur_to_nadzbior()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2", "S3");
        var b = Poz("p2", "jbib", 7, "H1", 1000, 600, Hist(10), "S1", "S2");
        var g = Assert.Single(Uruchom(null, a, b).Grupy);
        Assert.Equal(Porownanie.Nadzbior, g.Werdykt);
        Assert.Equal("SAME_MODEL_SUBSET", g.Pary[0].Powod.Kod);
    }

    [Fact]
    public void Ten_sam_model_inne_tekstury_to_przemalowanie_nie_duplikat()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2");
        var b = Poz("p2", "jbib", 7, "H1", 1000, 600, Hist(10), "S8", "S9");
        b.Tekstury.ForEach(t => { t.PHash = new ulong[] { ulong.MaxValue, 0, ulong.MaxValue, 0 }; t.Kolor = Convert.ToBase64String(Enumerable.Repeat((byte)200, 192).ToArray()); });
        var g = Assert.Single(Uruchom(null, a, b).Grupy);
        Assert.Equal(Porownanie.Przemalowanie, g.Werdykt);
    }

    [Fact]
    public void Podobny_obrys_ale_inna_liczba_trojkatow_nie_jest_kandydatem()
    {
        // przypadek rekawiczek hand_000 (3560 tri) vs hand_025 (2480 tri): histogram blisko, ale to rozne modele
        var a = Poz("p1", "hand", 0, "H1", 3560, 2000, Hist(10), "S1");
        var b = Poz("p1", "hand", 25, "H2", 2480, 1500, Hist(10), "S1");
        Assert.Empty(Uruchom(null, a, b).Grupy);
    }

    [Fact]
    public void Progi_mozna_nadpisac()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1", "S2");
        var b = Poz("p2", "jbib", 7, "H2", 1000, 600, Hist(12), "S1", "S2");   // hist rozny -> odleglosc 2.0 (max)
        Assert.Empty(Uruchom(null, a, b).Grupy);
        var luzne = new Progi { GeoPodobna = 2.5, GeoIdentyczna = 2.5 };
        var g = Assert.Single(Uruchom(luzne, a, b).Grupy);
        Assert.Equal(Porownanie.Duplikat, g.Werdykt);
    }

    [Fact]
    public void Znajdz_zglasza_postep_i_da_sie_anulowac()
    {
        var a = Poz("p1", "jbib", 1, "H1", 1000, 600, Hist(10), "S1");
        var b = Poz("p2", "jbib", 2, "H2", 1000, 600, Hist(20), "S2");
        var c = Poz("p3", "jbib", 3, "H3", 1000, 600, Hist(30), "S3");
        var k = new Katalog(); k.Wstaw(new[] { a, b, c });
        var postepy = new List<Postep>();
        Porownanie.Znajdz(k, null, null, postepy.Add, default);
        Assert.Contains(postepy, p => p.Etap == "porownaj" && p.Zrobione == 3 && p.Wszystkie == 3);
        var cts = new System.Threading.CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Porownanie.Znajdz(k, null, null, null, cts.Token));
    }

    [Fact]
    public void Id_grupy_nie_zalezy_od_kolejnosci_czlonkow()
    {
        Assert.Equal(Grupa.PoliczId(new[] { "b", "a", "c" }), Grupa.PoliczId(new[] { "c", "b", "a" }));
        Assert.NotEqual(Grupa.PoliczId(new[] { "a", "b" }), Grupa.PoliczId(new[] { "a", "c" }));
        Assert.Equal(16, Grupa.PoliczId(new[] { "x" }).Length);
    }

    [Fact]
    public void Domyslne_progi_to_kalibracja_15_08()
    {
        var p = Progi.Domyslne;
        Assert.Equal(0.02, p.GeoIdentyczna); Assert.Equal(0.10, p.GeoPodobna); Assert.Equal(0.05, p.GeoPodobnaTri); Assert.Equal(0.15, p.GeoPodobnaBbox);
        Assert.Equal(20, p.TexPHash); Assert.Equal(3.0, p.TexKolor); Assert.Equal(3.0f, p.TexWariancjaMin); Assert.Equal(1.0, p.TexKolorPlaska);
        Assert.Equal(0.95, p.PelnePokrycie); Assert.Equal(0.5, p.CzesciowePokrycie);
    }

    [Fact]
    public void Progi_sprawdz_kopia_rowne()
    {
        var p = Progi.Domyslne;
        Assert.Empty(p.Sprawdz());
        var k = p.Kopia(); Assert.True(p.Rowne(k)); Assert.NotSame(p, k);
        k.TexPHash = 24; Assert.False(p.Rowne(k)); Assert.Empty(k.Sprawdz());
        k.TexPHash = 300; k.GeoPodobna = 0.01; k.CzesciowePokrycie = 0.99; k.TexKolor = -1;
        var b = k.Sprawdz();
        Assert.Contains("TexPHash", b); Assert.Contains("GeoPodobna", b); Assert.Contains("CzesciowePokrycie", b); Assert.Contains("TexKolor", b);
        Assert.DoesNotContain("GeoIdentyczna", b);
        Assert.False(p.Rowne(null));
    }
}
