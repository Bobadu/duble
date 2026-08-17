using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Duble.Tests;

/// <summary>Zastosowanie: plan (stany plikow), wykonanie (przenosiny do kosza wzgledem zrodla), cofka, cofniecie calosci/pozycji, przerwanie.</summary>
public class ZastosowanieTests
{
    /// <summary>Sztuczna pozycja z plikami na dysku: &lt;tmp&gt;\src\&lt;kontener&gt;\&lt;typ&gt;_NNN_u.ydd + tekstury.</summary>
    static Garment Poz(string src, string kontener, string typ, int numer, params string[] litery)
    {
        var folder = Path.Combine(src, kontener); Directory.CreateDirectory(folder);
        var ydd = Path.Combine(folder, $"{typ}_{numer:d3}_u.ydd"); File.WriteAllBytes(ydd, new byte[100]);
        var p = new Garment { Id = $"z1|{kontener}|{typ}|{numer}|u", PackName = "z1", Container = kontener, Slot = typ, Number = numer, Suffix = "u", SourceId = "id1", ModelPath = ydd, ModelSize = 100 };
        foreach (var l in litery)
        {
            var plik = Path.Combine(folder, $"{typ}_diff_{numer:d3}_{l}_uni.ytd");
            if (!File.Exists(plik)) File.WriteAllBytes(plik, new byte[50]);
            p.Textures.Add(new TextureInfo { FileName = Path.GetFileName(plik), Path = plik, Sha256 = "S" + typ + numer + l, Size = 50 });
        }
        return p;
    }

    static (string tmp, string src, string kosz, Catalog kat, Func<Garment, CelPozycji> cel) Swiat()
    {
        var tmp = Sciezki.Tymczasowy("zastosuj");
        var src = Path.Combine(tmp, "z1"); Directory.CreateDirectory(src);
        var kosz = Path.Combine(tmp, "_odrzucone", "z1");
        var kat = new Catalog();
        var a = Poz(src, "k.rpf", "jbib", 1, "a", "b");           // zostaje
        var b = Poz(src, "k.rpf", "jbib", 7, "a");                // odrzucona
        var f1 = Poz(src, "k.rpf", "feet", 50, "a");              // feet_050 zostaje...
        var f2 = Poz(src, "k.rpf", "feet", 50, "a"); f2.Id = "z1|k.rpf|feet|50|u_1"; f2.Suffix = "u_1";
        f2.ModelPath = Path.Combine(src, "k.rpf", "feet_050_u_1.ydd"); File.WriteAllBytes(f2.ModelPath, new byte[100]);   // ...feet_050_1 odrzucona, wspolna tekstura
        var arch = new Garment { Id = "z1|x.rpf|hair|3|u", PackName = "z1", Container = "x.rpf", Slot = "hair", Number = 3, Suffix = "u", SourceId = "id1", ModelPath = Path.Combine(src, "x.rpf") + "|x.rpf\\hair_003_u.ydd", ModelSize = 10 };
        var brak = Poz(src, "k.rpf", "lowr", 9, "a"); File.Delete(brak.ModelPath);   // ydd zniknal z dysku
        kat.Upsert(new[] { a, b, f1, f2, arch, brak });
        Func<Garment, CelPozycji> cel = p => new CelPozycji { Korzen = src, Kosz = kosz, Zrodlo = "z1", ZrodloId = "id1" };
        return (tmp, src, kosz, kat, cel);
    }

    [Fact]
    public void Plan_rozroznia_przenies_wspoldzielony_wArchiwum_brak()
    {
        var (tmp, src, kosz, kat, cel) = Swiat();
        try
        {
            var plan = Zastosowanie.Zaplanuj(kat, new[] { "z1|k.rpf|jbib|7|u", "z1|k.rpf|feet|50|u_1", "z1|x.rpf|hair|3|u", "z1|k.rpf|lowr|9|u", "nie-ma" }, cel);
            Assert.Equal(4, plan.Pozycje.Count);
            var b = plan.Pozycje.Single(p => p.Id == "z1|k.rpf|jbib|7|u");
            Assert.Equal(2, b.DoPrzeniesienia); Assert.Equal(150, b.Bajty);
            Assert.All(b.Pliki, r => Assert.StartsWith(Path.Combine(kosz, "k.rpf"), r.Do));
            var f2 = plan.Pozycje.Single(p => p.Id == "z1|k.rpf|feet|50|u_1");
            Assert.Equal(1, f2.DoPrzeniesienia); Assert.Equal(1, f2.Wspoldzielone);          // ydd idzie, tekstura feet_050 zostaje
            var arch = plan.Pozycje.Single(p => p.Id == "z1|x.rpf|hair|3|u");
            Assert.Equal(1, arch.WArchiwum); Assert.Equal(0, arch.DoPrzeniesienia);
            var brak = plan.Pozycje.Single(p => p.Id == "z1|k.rpf|lowr|9|u");
            Assert.Equal(1, brak.Brakujace); Assert.Equal(1, brak.DoPrzeniesienia);          // ydd brak, tekstura jest
            Assert.Equal(4, plan.Pliki); Assert.Equal(1, plan.Wspoldzielone); Assert.Equal(1, plan.WArchiwum); Assert.Equal(1, plan.Brakujace);
            var kosze = plan.Kosze().ToList();
            Assert.Single(kosze); Assert.Equal(kosz, kosze[0].kosz); Assert.Equal(4, kosze[0].pliki);
            // brak zrodla -> wszystko Brak, zrodlo w BrakujaceZrodla
            var plan2 = Zastosowanie.Zaplanuj(kat, new[] { "z1|k.rpf|jbib|7|u" }, p => null);
            Assert.Equal(2, plan2.Pozycje[0].Brakujace); Assert.Equal(new[] { "z1" }, plan2.BrakujaceZrodla);
            // pusta lista -> pusty plan
            Assert.Empty(Zastosowanie.Zaplanuj(kat, Array.Empty<string>(), cel).Pozycje);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Wykonaj_przenosi_i_Cofnij_przywraca_calosc_albo_pozycje()
    {
        var (tmp, src, kosz, kat, cel) = Swiat();
        try
        {
            var plan = Zastosowanie.Zaplanuj(kat, new[] { "z1|k.rpf|jbib|7|u", "z1|k.rpf|feet|50|u_1" }, cel);
            var postepy = new List<ProgressReport>();
            var cofka = Zastosowanie.Wykonaj(plan, "test", postepy.Add);
            Assert.False(cofka.Przerwano);
            Assert.Equal(3, cofka.Ruchy.Count); Assert.Equal(2, cofka.Pozycje.Count);
            Assert.True(File.Exists(Path.Combine(kosz, "k.rpf", "jbib_007_u.ydd")));
            Assert.True(File.Exists(Path.Combine(kosz, "k.rpf", "jbib_diff_007_a_uni.ytd")));
            Assert.True(File.Exists(Path.Combine(kosz, "k.rpf", "feet_050_u_1.ydd")));
            Assert.False(File.Exists(Path.Combine(src, "k.rpf", "jbib_007_u.ydd")));
            Assert.True(File.Exists(Path.Combine(src, "k.rpf", "feet_diff_050_a_uni.ytd")));   // wspoldzielona zostala
            Assert.True(File.Exists(Path.Combine(src, "k.rpf", "feet_050_u.ydd")));
            Assert.Contains(postepy, p => p.Stage == "zastosuj" && p.Total == 3);
            Assert.True(cofka.MoznaCofnac); Assert.True(cofka.MoznaCofnacPozycje("z1|k.rpf|jbib|7|u"));

            var plik = Path.Combine(tmp, "historia", "c.json");
            cofka.Zapisz(plik);
            var wczytana = Cofka.Wczytaj(plik);
            Assert.Equal(3, wczytana.Ruchy.Count); Assert.Equal("test", wczytana.Opis); Assert.Equal(250, wczytana.Bajty);   // 100 + 50 + 100

            // cofnij tylko feet_050_1
            var (w1, p1) = Zastosowanie.Cofnij(wczytana, new[] { "z1|k.rpf|feet|50|u_1" });
            Assert.Equal(1, w1); Assert.Equal(0, p1);
            Assert.True(File.Exists(Path.Combine(src, "k.rpf", "feet_050_u_1.ydd")));
            Assert.False(File.Exists(Path.Combine(kosz, "k.rpf", "feet_050_u_1.ydd")));
            Assert.Null(wczytana.Cofnieto); Assert.True(wczytana.CzesciowoCofnieta); Assert.True(wczytana.MoznaCofnac);
            Assert.False(wczytana.MoznaCofnacPozycje("z1|k.rpf|feet|50|u_1"));

            // cel zajety -> pominiety; potem calosc
            File.WriteAllBytes(Path.Combine(src, "k.rpf", "jbib_007_u.ydd"), new byte[1]);
            var (w2, p2) = Zastosowanie.Cofnij(wczytana);
            Assert.Equal(1, w2); Assert.Equal(1, p2);
            Assert.Null(wczytana.Cofnieto);
            File.Delete(Path.Combine(src, "k.rpf", "jbib_007_u.ydd"));
            var (w3, p3) = Zastosowanie.Cofnij(wczytana);
            Assert.Equal(1, w3); Assert.Equal(0, p3);
            Assert.NotNull(wczytana.Cofnieto); Assert.False(wczytana.MoznaCofnac);
            Assert.False(Directory.Exists(Path.Combine(kosz, "k.rpf")));   // puste foldery kosza posprzatane
            wczytana.Zapisz(plik);
            Assert.NotNull(Cofka.Wczytaj(plik).Cofnieto);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Anulowanie_daje_czesciowa_cofke()
    {
        var (tmp, src, kosz, kat, cel) = Swiat();
        try
        {
            var plan = Zastosowanie.Zaplanuj(kat, new[] { "z1|k.rpf|jbib|7|u", "z1|k.rpf|feet|50|u_1" }, cel);
            using var cts = new CancellationTokenSource();
            var cofka = Zastosowanie.Wykonaj(plan, "test", p => { if (p.Done == 1) cts.Cancel(); }, cts.Token);
            Assert.True(cofka.Przerwano);
            Assert.Single(cofka.Ruchy);
            Assert.Single(Directory.GetFiles(kosz, "*", SearchOption.AllDirectories));
            Zastosowanie.Cofnij(cofka);
            Assert.NotNull(cofka.Cofnieto);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Wzgledna_liczy_od_korzenia_a_spoza_daje_nazwe()
    {
        var tmp = Sciezki.Tymczasowy("wzgl");
        try
        {
            var plik = Path.Combine(tmp, "a", "b.rpf", "c.ydd");
            Assert.Equal(Path.Combine("a", "b.rpf", "c.ydd"), Zastosowanie.Wzgledna(tmp, plik));
            Assert.Equal("c.ydd", Zastosowanie.Wzgledna(Path.Combine(tmp, "inny"), plik));
            Assert.Equal("c.ydd", Zastosowanie.Wzgledna(null, plik));
        }
        finally { Directory.Delete(tmp, true); }
    }
}
