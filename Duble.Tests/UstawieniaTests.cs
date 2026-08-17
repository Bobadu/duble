using System.IO;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class UstawieniaTests
{
    [Fact]
    public void Zapis_odczyt_i_lista_ostatnich_projektow()
    {
        var tmp = Sciezki.Tymczasowy("ustawienia");
        try
        {
            var plik = Path.Combine(tmp, "settings.json");
            var u = new Ustawienia { Jezyk = "en", Motyw = "dark", Okno = new OknoStan { X = 10, Y = 20, W = 1200, H = 800, Maks = false } };
            u.ZanotujProjekt(@"C:\a\A.duble", "A"); u.ZanotujProjekt(@"C:\b\B.duble", "B"); u.ZanotujProjekt(@"C:\a\A.duble", "A");   // A drugi raz -> na gore, bez duplikatu
            u.Zapisz(plik);
            var w = Ustawienia.Wczytaj(plik);
            Assert.Equal("en", w.Jezyk); Assert.Equal("dark", w.Motyw); Assert.Equal(1200, w.Okno.W);
            Assert.Equal(2, w.Ostatnie.Count); Assert.Equal(@"C:\a\A.duble", w.Ostatnie[0].Sciezka); Assert.Equal("B", w.Ostatnie[1].Name);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Brak_pliku_daje_domyslne()
    {
        var u = Ustawienia.Wczytaj(Path.Combine(Sciezki.Tymczasowy("brak"), "nie-ma.json"));
        Assert.Equal("system", u.Motyw); Assert.Empty(u.Ostatnie); Assert.Null(u.Jezyk);
    }

    [Fact]
    public void Ostatnie_maksymalnie_dziesiec()
    {
        var u = new Ustawienia();
        for (int i = 0; i < 15; i++) u.ZanotujProjekt($@"C:\p{i}\P.duble", "P" + i);
        Assert.Equal(10, u.Ostatnie.Count); Assert.Equal("P14", u.Ostatnie[0].Name);
    }
}
