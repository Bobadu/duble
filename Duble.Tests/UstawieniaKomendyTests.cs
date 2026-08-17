using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>project.settings.get/set/resetProgi, cache.clear, calibrate.run — na sztucznym katalogu.</summary>
public class UstawieniaKomendyTests
{
    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement;
    static async Task<JsonElement> Wywolaj(Mostek m, string cmd, string args = "null") => Odp(await m.Obsluz($"{{\"id\":\"1\",\"cmd\":\"{cmd}\",\"args\":{args}}}"));

    [Fact]
    public async Task Kosz_progi_cache_kalibracja()
    {
        var tmp = Sciezki.Tymczasowy("ustawienia");
        try
        {
            var wyslane = new List<string>();
            var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), new Ustawienia(), wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
            var s = TestSession.Create(); s.Nowy("U", Path.Combine(tmp, "proj", "U.duble"));
            Sztuczne.SiedemZeZrodlami(s, tmp);
            s.Porownaj(default, null); s.Zapisz();
            var jr = new JobRunner(m.Zdarzenie);
            Duble.App.Komendy.Grupy.Zarejestruj(m, s, jr);
            Duble.App.Komendy.UstawieniaKomendy.Zarejestruj(m, s, jr);

            var g = (await Wywolaj(m, "project.settings.get")).GetProperty("result");
            Assert.False(g.TryGetProperty("kosz", out var kz) && kz.ValueKind != JsonValueKind.Null);
            Assert.False(g.GetProperty("progiZmienione").GetBoolean());
            Assert.Equal(20, g.GetProperty("progi").GetProperty("textureHashDistance").GetInt32());
            Assert.Equal(20, g.GetProperty("progiDomyslne").GetProperty("textureHashDistance").GetInt32());
            Assert.True(g.GetProperty("cache").GetProperty("razem").GetProperty("pliki").GetInt32() >= 0);
            Assert.EndsWith(".duble.cache", g.GetProperty("folderCache").GetString());

            // kosz
            var kosz = Path.Combine(tmp, "kosz").Replace("\\", "\\\\");
            g = (await Wywolaj(m, "project.settings.set", $"{{\"kosz\":\"{kosz}\"}}")).GetProperty("result");
            Assert.Equal(Path.Combine(tmp, "kosz"), g.GetProperty("kosz").GetString());
            Assert.Contains(Path.Combine(tmp, "kosz").Replace("\\", "\\\\"), File.ReadAllText(s.Projekt.Sciezka));
            g = (await Wywolaj(m, "project.settings.set", "{\"kosz\":\"\"}")).GetProperty("result");
            Assert.False(g.TryGetProperty("kosz", out kz) && kz.ValueKind != JsonValueKind.Null);
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"settings.changed\""));

            // progi czesciowe -> zmienione, ruszylo porownanie
            wyslane.Clear();
            g = (await Wywolaj(m, "project.settings.set", "{\"progi\":{\"textureHashDistance\":24,\"textureColorDistance\":3.5}}")).GetProperty("result");
            Assert.True(g.GetProperty("progiZmienione").GetBoolean());
            Assert.Equal(24, g.GetProperty("progi").GetProperty("textureHashDistance").GetInt32());
            Assert.Equal(3.5, g.GetProperty("progi").GetProperty("textureColorDistance").GetDouble());
            Assert.Equal(0.02, g.GetProperty("progi").GetProperty("geometryIdentical").GetDouble());   // reszta bez zmian
            Assert.True(g.GetProperty("porownanie").GetBoolean());
            for (int i = 0; i < 200 && !wyslane.Any(w => w.Contains("\"event\":\"compare.done\"")); i++) await Task.Delay(50);
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"compare.done\""));
            Assert.Equal(24, s.Projekt.Ustawienia.Thresholds.TextureHashDistance);
            // te same wartosci drugi raz -> bez porownania (porownanie == null)
            g = (await Wywolaj(m, "project.settings.set", "{\"progi\":{\"textureHashDistance\":24}}")).GetProperty("result");
            Assert.False(g.TryGetProperty("porownanie", out var por) && por.ValueKind != JsonValueKind.Null);
            // zle progi -> bad_args z nazwa pola, stan bez zmian
            var blad = await Wywolaj(m, "project.settings.set", "{\"progi\":{\"textureHashDistance\":999}}");
            Assert.Equal("bad_args", blad.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("TextureHashDistance", blad.GetProperty("error").GetProperty("message").GetString());
            Assert.Equal(24, s.Projekt.Ustawienia.Thresholds.TextureHashDistance);
            // reset -> domyslne (null w projekcie), porownanie
            for (int i = 0; i < 100 && jr.Zajety; i++) await Task.Delay(50);
            wyslane.Clear();
            g = (await Wywolaj(m, "project.settings.resetProgi")).GetProperty("result");
            Assert.False(g.GetProperty("progiZmienione").GetBoolean());
            Assert.Null(s.Projekt.Ustawienia.Thresholds);
            Assert.True(g.GetProperty("porownanie").GetBoolean());
            for (int i = 0; i < 100 && jr.Zajety; i++) await Task.Delay(50);

            // cache: podlozony plik w tex\ -> usuniety, thumbs nietkniete
            Directory.CreateDirectory(s.Projekt.FolderTekstur); File.WriteAllBytes(Path.Combine(s.Projekt.FolderTekstur, "a.png"), new byte[300]);
            Directory.CreateDirectory(s.Projekt.FolderMiniatur); File.WriteAllBytes(Path.Combine(s.Projekt.FolderMiniatur, "b.png"), new byte[100]);
            g = (await Wywolaj(m, "project.settings.get")).GetProperty("result");
            Assert.Equal(300, g.GetProperty("cache").GetProperty("tex").GetProperty("bajty").GetInt64());
            var c = (await Wywolaj(m, "cache.clear", "{}")).GetProperty("result");
            Assert.Equal(1, c.GetProperty("usunieto").GetInt32()); Assert.Equal(300, c.GetProperty("bajty").GetInt64());
            Assert.False(File.Exists(Path.Combine(s.Projekt.FolderTekstur, "a.png")));
            Assert.True(File.Exists(Path.Combine(s.Projekt.FolderMiniatur, "b.png")));

            // kalibracja
            wyslane.Clear();
            var k = await Wywolaj(m, "calibrate.run");
            Assert.True(k.GetProperty("result").GetProperty("uruchomiono").GetBoolean());
            for (int i = 0; i < 300 && !wyslane.Any(w => w.Contains("\"event\":\"calibrate.done\"")); i++) await Task.Delay(50);
            var done = JsonDocument.Parse(wyslane.First(w => w.Contains("\"event\":\"calibrate.done\""))).RootElement.GetProperty("data").GetProperty("wynik");
            Assert.Equal(7, done.GetProperty("pozycje").GetInt32());
            Assert.True(done.GetProperty("geoNajblizszyObcy").GetProperty("kubelki").GetArrayLength() > 0);
            Assert.True(done.GetProperty("propozycja").GetProperty("textureHashDistance").GetInt32() >= 4);
            Assert.Equal(20, done.GetProperty("usedThresholds").GetProperty("textureHashDistance").GetInt32());
        }
        finally { Directory.Delete(tmp, true); }
    }
}
