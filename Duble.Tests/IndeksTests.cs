using System.IO;
using System.Linq;
using Duble;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>Indeksowanie na prawdziwych danych (pomijane, gdy brak plikow gry / downloads).</summary>
public class IndeksTests
{
    readonly ITestOutputHelper wyj;
    public IndeksTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    [Fact]
    public void Archiwum_rpf_jako_zrodlo_daje_geometrie_i_tekstury()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY: brak studio_body\\dlc.rpf"); return; }
        var poz = Indeks.Zrodlo(Sciezki.Dlc("studio_body"), "studio_body", s => wyj.WriteLine(s));
        var uppr = poz.FirstOrDefault(p => p.Typ == "uppr" && p.Numer == 15);
        Assert.NotNull(uppr);
        Assert.Equal(6080, uppr.Geo.Wierzcholki);            // KS Body V1 ramiona: 6072 (cialo) + 8 (znak wodny "Ks"), pomiar 16.08
        Assert.True(uppr.Gen9);
        Assert.NotEmpty(uppr.Tekstury);
        Assert.All(uppr.Tekstury, t => Assert.True(t.Zdekodowana, t.Plik + " " + t.Format));
        Assert.All(poz, p => Assert.Contains("|", p.SciezkaYdd));   // sciezka "archiwum|wewnatrz"
        // Zrodla.Bajty oddaje bajty z naglowkiem RSC7 (do miniatur/GLB)
        var b = Zrodla.Bajty(uppr.SciezkaYdd);
        Assert.True(Rsc7.JestRsc7(b)); Assert.Equal(159, Rsc7.Wersja(b));
    }

    [Fact]
    public void Plik_rpf_lezacy_w_folderze_jest_kontenerem()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY: brak studio_body\\dlc.rpf"); return; }
        var tmp = Sciezki.Tymczasowy("rpf-w-folderze");
        try
        {
            File.Copy(Sciezki.Dlc("studio_body"), Path.Combine(tmp, "dlc.rpf"));
            var poz = Indeks.Zrodlo(tmp, "test", s => wyj.WriteLine(s));
            Assert.NotEmpty(poz);
            Assert.All(poz, p => Assert.Equal("body.rpf", p.Kontener));   // kontener = najglebsze archiwum (x64/body.rpf wewnatrz dlc.rpf)
            Assert.All(poz, p => Assert.True(p.Gen9));
            Assert.Contains(poz, p => p.Typ == "uppr" && p.Numer == 15 && p.Geo.Wierzcholki == 6080);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Folder_legacy_ma_gen9_false_i_tyle_pozycji_co_wzorzec()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY: brak downloads"); return; }
        var poz = Indeks.Zrodlo(Sciezki.Downloads("vrp_clothes_f_civil03"), "vrp_clothes_f_civil03", s => { });
        Assert.Equal(62, poz.Count);
        Assert.All(poz, p => Assert.False(p.Gen9));
    }
}
