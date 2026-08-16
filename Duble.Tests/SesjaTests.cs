using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Duble;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class SesjaTests
{
    [Fact]
    public void Nowy_projekt_tworzy_plik_i_cache_a_otworz_go_wczytuje()
    {
        var tmp = Sciezki.Tymczasowy("sesja");
        try
        {
            var s = new Sesja();
            s.Nowy("Moje studio", Path.Combine(tmp, "Moje studio.duble"));
            Assert.True(File.Exists(Path.Combine(tmp, "Moje studio.duble")));
            Assert.True(Directory.Exists(Path.Combine(tmp, "Moje studio.duble.cache")));
            Assert.True(s.Otwarty); Assert.Empty(s.Katalog.Pozycje);
            s.Zamknij(); Assert.False(s.Otwarty);
            s.Otworz(Path.Combine(tmp, "Moje studio.duble"));
            Assert.Equal("Moje studio", s.Projekt.Nazwa);
            var pod = JsonSerializer.Serialize(s.Podsumowanie(), Mostek.Json);
            Assert.Contains("\"zrodla\":0", pod); Assert.Contains("\"pozycje\":0", pod);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task Komendy_project_dzialaja_przez_mostek()
    {
        var tmp = Sciezki.Tymczasowy("sesja-mostek");
        try
        {
            var u = new Ustawienia(); var wyslane = new List<string>();
            var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), u, wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
            var s = new Sesja(); Duble.App.Komendy.Projekty.Zarejestruj(m, s);
            var folder = tmp.Replace("\\", "\\\\");
            var o = JsonDocument.Parse(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"project.new\",\"args\":{\"nazwa\":\"Test: A/B\",\"folder\":\"" + folder + "\"}}")).RootElement;
            Assert.True(o.GetProperty("ok").GetBoolean(), o.ToString());
            Assert.True(File.Exists(Path.Combine(tmp, "Test A B.duble")));   // znaki niedozwolone -> spacje
            Assert.Single(u.Ostatnie);
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"project.opened\""));
            o = JsonDocument.Parse(await m.Obsluz("{\"id\":\"2\",\"cmd\":\"project.new\",\"args\":{\"nazwa\":\"Test: A/B\",\"folder\":\"" + folder + "\"}}")).RootElement;
            Assert.Equal("io", o.GetProperty("error").GetProperty("code").GetString());   // juz istnieje
            o = JsonDocument.Parse(await m.Obsluz("{\"id\":\"3\",\"cmd\":\"project.recent\"}")).RootElement;
            Assert.True(o.GetProperty("result").GetProperty("ostatnie")[0].GetProperty("istnieje").GetBoolean());
            o = JsonDocument.Parse(await m.Obsluz("{\"id\":\"4\",\"cmd\":\"project.open\",\"args\":{\"sciezka\":\"C:\\\\nie\\\\ma.duble\"}}")).RootElement;
            Assert.Equal("not_found", o.GetProperty("error").GetProperty("code").GetString());
            o = JsonDocument.Parse(await m.Obsluz("{\"id\":\"5\",\"cmd\":\"project.get\"}")).RootElement;
            Assert.Equal("Test: A/B", o.GetProperty("result").GetProperty("projekt").GetProperty("nazwa").GetString());
            await m.Obsluz("{\"id\":\"6\",\"cmd\":\"project.close\"}");
            o = JsonDocument.Parse(await m.Obsluz("{\"id\":\"7\",\"cmd\":\"project.get\"}")).RootElement;
            // null jest pomijany w JSON (WhenWritingNull) — brak klucza albo null = brak projektu
            Assert.False(o.GetProperty("result").TryGetProperty("projekt", out var pj) && pj.ValueKind != JsonValueKind.Null);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
