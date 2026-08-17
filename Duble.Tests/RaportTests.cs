using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using System;
using Xunit;

namespace Duble.Tests;

/// <summary>Raport HTML w jezyku UI z rozstrzygnieciami (decyzje uzytkownika) i CSV grup/decyzji — na sztucznym katalogu.</summary>
public class RaportTests
{
    static readonly IDuplicateFinder Finder =
        new ServiceCollection().AddDubleCore().BuildServiceProvider().GetRequiredService<IDuplicateFinder>();

    static readonly IArchiveCache Archiwa = new ServiceCollection().AddDubleCore().BuildServiceProvider().GetRequiredService<IArchiveCache>();

    static (Catalog kat, ComparisonResult wynik, string tmp) Swiat()
    {
        var tmp = Sciezki.Tymczasowy("raport");
        var kat = new Catalog(); kat.Upsert(Sztuczne.Siedem(tmp));
        var wynik = Finder.Find(kat);
        return (kat, wynik, tmp);
    }

    [Fact]
    public void Html_po_angielsku_z_decyzjami()
    {
        var (kat, wynik, tmp) = Swiat();
        try
        {
            var efg = wynik.Groups.First(g => g.Members.Count == 3);
            var ab = wynik.Groups.First(g => g.Verdict == Verdict.Duplicate && g.Members.Count == 2);
            var decyzje = new Dictionary<string, Decision>
            {
                [efg.Id] = new Decision { Ignored = true, Note = "different boots" },
                [ab.Id] = new Decision { Winner = ab.Members[1], Rejected = { ab.Members[0] } },
            };
            var plik = Path.Combine(tmp, "r.html");
            var log = new List<string>();
            Raport.Zbuduj(Archiwa, kat, wynik, plik, log.Add, "en", g => new ResolutionService().Resolve(g, decyzje.TryGetValue(g.Id, out var d) ? d : null), "My project");
            var html = File.ReadAllText(plik);
            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains("Duble — My project", html);
            Assert.Contains(">STAYS<", html); Assert.Contains(">TO REJECT<", html);
            Assert.DoesNotContain("ZOSTAJE", html); Assert.DoesNotContain("DO ODRZUCENIA", html);
            Assert.Contains("NOT A DUPLICATE", html); Assert.Contains("different boots", html);
            Assert.Contains("YOUR DECISION", html);
            Assert.Contains("Textures side by side", html);
            Assert.Contains("Nothing was deleted", html);
            // do odrzucenia: tylko a (b zostaje z decyzji), efg zignorowana, cd przemalowanie -> 1
            Assert.Contains("to reject <b>1</b>", html);
            Assert.DoesNotContain("[raport.", html);   // zaden klucz bez tlumaczenia

            // po polsku, domyslne rozstrzygniecia: a zostaje, b odrzucona; f,g odrzucone -> 3
            var plikPl = Path.Combine(tmp, "r-pl.html");
            Raport.Zbuduj(Archiwa, kat, wynik, plikPl, null);
            var pl = File.ReadAllText(plikPl);
            Assert.Contains("<html lang=\"pl\">", pl);
            Assert.Contains(">ZOSTAJE<", pl); Assert.Contains("do odrzucenia <b>3</b>", pl);
            Assert.DoesNotContain("[raport.", pl);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Csv_wiersz_na_czlonka_separator_wg_jezyka()
    {
        var (kat, wynik, tmp) = Swiat();
        try
        {
            var efg = wynik.Groups.First(g => g.Members.Count == 3);
            var decyzje = new Dictionary<string, Decision> { [efg.Id] = new Decision { Ignored = true, Note = "inne; buty" } };
            var csv = Raport.Csv(kat, wynik, g => new ResolutionService().Resolve(g, decyzje.TryGetValue(g.Id, out var d) ? d : null), "pl");
            Assert.StartsWith("\uFEFF", csv);
            var linie = csv.TrimEnd('\r', '\n').Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            Assert.Equal(1 + 7, linie.Count);                       // naglowek + 7 czlonkow (2+2+3)
            Assert.StartsWith("\uFEFFgrupa;werdykt;pow\u00F3d;pozycja;", linie[0]);
            Assert.Contains(linie, l => l.Contains(";zignorowana;\"inne; buty\";"));   // srednik w notatce -> cudzyslow
            Assert.Contains(linie, l => l.Contains(";zostaje;")); Assert.Contains(linie, l => l.Contains(";odrzucona;"));
            Assert.Contains(linie, l => l.Contains(";bez zmian;"));                        // przemalowanie
            var en = Raport.Csv(kat, wynik, null, "en");
            Assert.StartsWith("\uFEFFgroup,verdict,reason,item,", en);
            Assert.Contains(",stays,", en); Assert.Contains(",rejected,", en);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
