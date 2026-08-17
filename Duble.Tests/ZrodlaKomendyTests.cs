using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.Core;
using Duble.App;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>Komendy sources.* na prawdziwym archiwum (studio_body\dlc.rpf, 10 pozycji) — pomijany bez gry.</summary>
public class ZrodlaKomendyTests
{
    readonly ITestOutputHelper wyj;
    public ZrodlaKomendyTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Dodaj_zrodlo_zaindeksuj_i_zobacz_liczby()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY"); return; }
        var tmp = Sciezki.Tymczasowy("zrodla-mostek");
        try
        {
            var wyslane = new System.Collections.Generic.List<string>();
            var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), new Ustawienia(), wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
            var s = new Sesja(); var jr = new JobRunner(m.Zdarzenie);
            Duble.App.Komendy.Projekty.Zarejestruj(m, s); Duble.App.Komendy.Zrodla.Zarejestruj(m, s, jr);
            s.Nowy("T", Path.Combine(tmp, "T.duble"));
            var dlc = Sciezki.Dlc("studio_body").Replace("\\", "\\\\");
            var o = Odp(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"sources.add\",\"args\":{\"sciezki\":[\"" + dlc + "\"]}}"));
            Assert.True(o.GetProperty("ok").GetBoolean(), o.ToString());
            Assert.Equal(1, o.GetProperty("result").GetProperty("dodane").GetArrayLength());
            var lista = Odp(await m.Obsluz("{\"id\":\"2\",\"cmd\":\"sources.list\"}")).GetProperty("result").GetProperty("zrodla");
            var id = lista[0].GetProperty("id").GetString();
            Assert.Equal("rpf", lista[0].GetProperty("typ").GetString()); Assert.Equal(0, lista[0].GetProperty("pozycje").GetInt32());
            Assert.True(lista[0].GetProperty("istnieje").GetBoolean());
            o = Odp(await m.Obsluz("{\"id\":\"3\",\"cmd\":\"sources.index\",\"args\":{\"ids\":[\"" + id + "\"]}}"));
            Assert.True(o.GetProperty("ok").GetBoolean(), o.ToString());
            for (int i = 0; i < 600 && jr.Zajety; i++) await Task.Delay(100);
            Assert.False(jr.Zajety);
            lista = Odp(await m.Obsluz("{\"id\":\"4\",\"cmd\":\"sources.list\"}")).GetProperty("result").GetProperty("zrodla");
            Assert.Equal(10, lista[0].GetProperty("pozycje").GetInt32());     // studio_body: 10 pozycji
            Assert.Equal("gen9", lista[0].GetProperty("format").GetString());
            Assert.True(lista[0].GetProperty("perSlot").GetProperty("uppr").GetInt32() >= 1);
            Assert.True(File.Exists(s.Projekt.PlikKatalogu));
            Assert.True(Directory.GetFiles(s.Projekt.FolderMiniatur, "*.png").Length > 0);
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"job\"") && w.Contains("\"stan\":\"koniec\""));
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"sources.changed\""));
            Assert.All(s.Katalog.Pozycje, p => Assert.Equal(id, p.ZrodloId));
            // dodanie tego samego drugi raz = pominiete
            o = Odp(await m.Obsluz("{\"id\":\"5\",\"cmd\":\"sources.add\",\"args\":{\"sciezki\":[\"" + dlc + "\",\"C:\\\\nie\\\\ma\"]}}"));
            Assert.Equal(0, o.GetProperty("result").GetProperty("dodane").GetArrayLength());
            Assert.Equal(2, o.GetProperty("result").GetProperty("pominiete").GetArrayLength());
            // wylaczenie i usuniecie zrodla czysci katalog
            await m.Obsluz("{\"id\":\"6\",\"cmd\":\"sources.toggle\",\"args\":{\"id\":\"" + id + "\",\"wlaczone\":false}}");
            Assert.False(s.Projekt.Zrodla[0].Wlaczone);
            await m.Obsluz("{\"id\":\"7\",\"cmd\":\"sources.remove\",\"args\":{\"id\":\"" + id + "\"}}");
            Assert.Empty(s.Katalog.Pozycje); Assert.Empty(s.Projekt.Zrodla);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task Rozpakuj_archiwum_do_folderu_i_dodaj_jako_zrodlo()
    {
        if (!Sciezki.JestGra) { wyj.WriteLine("POMINIETY"); return; }
        var tmp = Sciezki.Tymczasowy("unpack-mostek");
        try
        {
            var wyslane = new System.Collections.Generic.List<string>();
            var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), new Ustawienia(), wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
            var s = new Sesja(); var jr = new JobRunner(m.Zdarzenie);
            Duble.App.Komendy.Projekty.Zarejestruj(m, s); Duble.App.Komendy.Zrodla.Zarejestruj(m, s, jr);
            s.Nowy("U", Path.Combine(tmp, "U.duble"));
            var z = s.Projekt.DodajZrodlo(Sciezki.Dlc("studio_body"));   // Nazwa = studio_body (dlc.rpf -> folder paczki)
            Assert.Equal("studio_body", z.Nazwa);
            Assert.Equal("studio_body", Duble.App.Komendy.Zrodla.NazwaFolderuKopii(z));
            var lista = Odp(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"sources.list\"}")).GetProperty("result").GetProperty("zrodla");
            Assert.EndsWith(Path.Combine("_odrzucone", "studio_body"), lista[0].GetProperty("kosz").GetString());
            var folder = Path.Combine(tmp, "kopie").Replace("\\", "\\\\");
            var o = Odp(await m.Obsluz("{\"id\":\"2\",\"cmd\":\"sources.unpack\",\"args\":{\"id\":\"" + z.Id + "\",\"folder\":\"" + folder + "\",\"dodajZrodlo\":true}}"));
            Assert.True(o.GetProperty("ok").GetBoolean(), o.ToString());
            Assert.Equal(Path.Combine(tmp, "kopie", "studio_body"), o.GetProperty("result").GetProperty("folder").GetString());
            for (int i = 0; i < 600 && !wyslane.Any(w => w.Contains("\"event\":\"unpack.done\"")); i++) await Task.Delay(100);
            for (int i = 0; i < 100 && jr.Zajety; i++) await Task.Delay(100);
            var done = JsonDocument.Parse(wyslane.First(w => w.Contains("\"event\":\"unpack.done\""))).RootElement.GetProperty("data");
            Assert.True(done.GetProperty("pliki").GetInt32() >= 20, done.ToString());
            Assert.True(done.GetProperty("archiwa").GetInt32() >= 2);
            Assert.Equal(0, done.GetProperty("bledy").GetArrayLength());
            var dodano = done.GetProperty("dodano").GetString();
            Assert.NotNull(dodano);
            Assert.Equal(2, s.Projekt.Zrodla.Count);
            Assert.False(z.Wlaczone);                                              // oryginal wylaczony
            var nowe = s.Projekt.Zrodla.Find(x => x.Id == dodano);
            Assert.Equal("folder", nowe.Typ); Assert.True(nowe.Wlaczone);
            Assert.Equal(10, s.Katalog.Pozycje.Count(p => p.ZrodloId == dodano));  // kopia zaindeksowana
            Assert.All(s.Katalog.Pozycje.Where(p => p.ZrodloId == dodano), p => Assert.DoesNotContain("|", p.SciezkaYdd));
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"compare.done\""));
            // drugi raz w to samo miejsce -> io (folder niepusty)
            o = Odp(await m.Obsluz("{\"id\":\"3\",\"cmd\":\"sources.unpack\",\"args\":{\"id\":\"" + z.Id + "\",\"folder\":\"" + folder + "\"}}"));
            Assert.Equal("io", o.GetProperty("error").GetProperty("code").GetString());
        }
        finally { Directory.Delete(tmp, true); }
    }
}
