using System.IO;
using System.Linq;
using Duble.Core;
using Duble.App;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>Sesja: porownanie, cache wyniku, generowanie tex/&lt;sha&gt;.png (studio_body — pomijany bez gry).</summary>
public class SesjaPorownanieTests
{
    readonly ITestOutputHelper wyj;
    public SesjaPorownanieTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    [Fact]
    public void Porownaj_zapisuje_wynik_a_tex_generuje_sie_na_zadanie()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY"); return; }
        var tmp = Sciezki.Tymczasowy("sesja-por");
        try
        {
            var s = new Sesja();
            s.Nowy("P", Path.Combine(tmp, "P.duble"));
            var z = s.Projekt.DodajZrodlo(Sciezki.Dlc("studio_body"));
            var poz = Indeks.Zrodlo(z.Sciezka, z.Nazwa, new OpcjeIndeksu { FolderMiniatur = s.Projekt.FolderMiniatur });
            foreach (var p in poz) p.ZrodloId = z.Id;
            s.ZmienKatalog(k => k.Wstaw(poz));
            s.Porownaj(default, null);
            Assert.NotNull(s.Wynik);
            Assert.True(File.Exists(s.Projekt.PlikDubli));
            var pod = System.Text.Json.JsonSerializer.Serialize(s.Podsumowanie(), Mostek.Json);
            Assert.Contains("\"duplikaty\":", pod);
            s.Zapisz();

            var sha = s.Katalog.Pozycje.SelectMany(p => p.Tekstury).First(t => t.Zdekodowana).Sha;
            Assert.NotNull(s.ZnajdzTeksture(sha));
            using (var st = s.Zasob("tex", sha))
            {
                Assert.NotNull(st);
                var b = new byte[8]; st.Read(b, 0, 8);
                Assert.Equal(0x89, b[0]); Assert.Equal((byte)'P', b[1]);
            }
            var plik = Path.Combine(s.Projekt.FolderTekstur, sha + ".png");
            Assert.True(File.Exists(plik));
            long dl = new FileInfo(plik).Length;
            using (var st2 = s.Zasob("tex", sha)) Assert.Equal(dl, st2.Length);   // z cache
            Assert.Null(s.Zasob("tex", "NIEMA"));

            // mesh: GLB pozycji z wariantem tekstury, w cache mesh\ (nazwa z SHA ydd i tekstury)
            var uppr = s.Katalog.Pozycje.First(p => p.Typ == "uppr" && p.Numer == 15);
            using (var st = s.Zasob("mesh", uppr.Id, "w=a"))
            {
                Assert.NotNull(st);
                var b = new byte[4]; st.Read(b, 0, 4);
                Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(b));
            }
            var glby = Directory.GetFiles(s.Projekt.FolderSiatek, "*.glb");
            Assert.Single(glby);
            using (var st2 = s.Zasob("mesh", uppr.Id, "w=a")) Assert.Equal(new FileInfo(glby[0]).Length, st2.Length);   // z cache
            Assert.Null(s.Zasob("mesh", "nie|ma|takiej|0|u", null));

            s.Zamknij();
            s.Otworz(Path.Combine(tmp, "P.duble"));
            Assert.NotNull(s.Wynik);
            // wylaczone zrodlo -> porownanie na zerze pozycji
            s.Projekt.Zrodla[0].Wlaczone = false;
            s.Porownaj(default, null);
            Assert.Empty(s.Wynik.Grupy);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
