using System.IO;
using System.Linq;
using Duble;
using Xunit;

namespace Duble.Tests;

public class ProjektTests
{
    [Fact]
    public void Zapis_i_odczyt_projektu_zachowuje_zrodla_decyzje_i_ustawienia()
    {
        var tmp = Sciezki.Tymczasowy("projekt");
        try
        {
            var plik = Path.Combine(tmp, "Studio.duble");
            var pr = Projekt.Nowy("Studio", plik);
            var folderFivem = Path.Combine(tmp, "paczka"); Directory.CreateDirectory(Path.Combine(folderFivem, "stream"));
            var z1 = pr.DodajZrodlo(folderFivem);
            File.WriteAllBytes(Path.Combine(tmp, "dlc.rpf"), new byte[] { 1, 2, 3 });
            var z2 = pr.DodajZrodlo(Path.Combine(tmp, "dlc.rpf"));
            var z3 = pr.DodajZrodlo(tmp);
            Assert.Equal("fivem", z1.Typ); Assert.Equal("rpf", z2.Typ); Assert.Equal("folder", z3.Typ);
            Assert.Equal("dlc", z2.Nazwa); Assert.Equal("paczka", z1.Nazwa);
            Assert.NotEqual(z1.Id, z2.Id);
            pr.Decyzje["abc123"] = new Decyzja { Zwyciezca = "p|k|jbib|1|u", Odrzucone = { "p|k|jbib|2|u" }, Notatka = "ta jest lepsza" };
            pr.Decyzje["ign"] = new Decyzja { Ignoruj = true };
            pr.Ustawienia.Progi = new Progi { TexPHash = 24 };
            pr.Zapisz();
            Assert.True(File.Exists(plik));

            var w = Projekt.Wczytaj(plik);
            Assert.Equal("Studio", w.Nazwa); Assert.Equal(plik, w.Sciezka);
            Assert.Equal(plik + ".cache", w.FolderCache);
            Assert.Equal(3, w.Zrodla.Count);
            Assert.Equal("fivem", w.Zrodla[0].Typ); Assert.True(w.Zrodla[0].Wlaczone);
            Assert.Equal("ta jest lepsza", w.Decyzje["abc123"].Notatka);
            Assert.Single(w.Decyzje["abc123"].Odrzucone);
            Assert.True(w.Decyzje["ign"].Ignoruj);
            Assert.Equal(24, w.Ustawienia.Progi.TexPHash);
            Assert.Equal(0.02, w.Ustawienia.Progi.GeoIdentyczna);
            Assert.EndsWith(Path.Combine("Studio.duble.cache", "thumbs"), w.FolderMiniatur);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
