using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>apply.preview / apply.run / history.list|get|undo na sztucznym katalogu (3 zrodla p1/p2/p3, 3 grupy) — pliki naprawde sie przenosza.</summary>
public class ApplyKomendyTests
{
    static (Mostek m, Sesja s, JobRunner jr, List<string> wyslane, string tmp) Zbuduj()
    {
        var tmp = Sciezki.Tymczasowy("apply");
        var wyslane = new List<string>();
        var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), new Ustawienia(), wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
        var s = new Sesja(); s.Nowy("A", Path.Combine(tmp, "proj", "A.duble"));
        Sztuczne.SiedemZeZrodlami(s, tmp);
        s.Porownaj(default, null);
        s.Zapisz();
        var jr = new JobRunner(m.Zdarzenie);
        Duble.App.Komendy.Grupy.Zarejestruj(m, s, jr);
        Duble.App.Komendy.Historia.Zarejestruj(m, s, jr);
        return (m, s, jr, wyslane, tmp);
    }

    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement.GetProperty("result");
    static async Task<JsonElement> Wywolaj(Mostek m, string cmd, string args = "null") => Odp(await m.Obsluz($"{{\"id\":\"1\",\"cmd\":\"{cmd}\",\"args\":{args}}}"));
    static async Task Poczekaj(JobRunner jr, List<string> wyslane, string zdarzenie)
    {
        for (int i = 0; i < 300 && !wyslane.Any(w => w.Contains("\"event\":\"" + zdarzenie + "\"")); i++) await Task.Delay(50);
        for (int i = 0; i < 100 && jr.Zajety; i++) await Task.Delay(50);
        Assert.Contains(wyslane, w => w.Contains("\"event\":\"" + zdarzenie + "\""));
    }

    [Fact]
    public async Task Preview_run_historia_i_cofniecie()
    {
        var (m, s, jr, wyslane, tmp) = Zbuduj();
        try
        {
            // plan: b (p2: ydd + 2 ytd), f (p2: ydd + ytd), g (p3: ydd + ytd) -> 7 plikow do koszy obok zrodel
            var prev = await Wywolaj(m, "apply.preview");
            Assert.Equal(3, prev.GetProperty("pozycje").GetInt32());
            Assert.Equal(7, prev.GetProperty("pliki").GetInt32());
            Assert.Equal(3, prev.GetProperty("lista").GetArrayLength());
            var kosze = prev.GetProperty("kosze").EnumerateArray().Select(k => k.GetProperty("kosz").GetString()).ToList();
            Assert.Equal(2, kosze.Count);
            Assert.Contains(Path.Combine(tmp, "_odrzucone", "p2"), kosze); Assert.Contains(Path.Combine(tmp, "_odrzucone", "p3"), kosze);
            Assert.False(prev.TryGetProperty("kosz", out var kz) && kz.ValueKind != JsonValueKind.Null);   // null -> pominiete (WhenWritingNull)

            // wlasny kosz zapisany w projekcie
            var kosz = Path.Combine(tmp, "kosz").Replace("\\", "\\\\");
            var r = await Wywolaj(m, "apply.run", $"{{\"kosz\":\"{kosz}\",\"ustawKosz\":true}}");
            Assert.True(r.GetProperty("uruchomiono").GetBoolean());
            Assert.Equal(Path.Combine(tmp, "kosz"), s.Projekt.Ustawienia.Kosz);
            await Poczekaj(jr, wyslane, "apply.done");
            var done = JsonDocument.Parse(wyslane.Last(w => w.Contains("\"event\":\"apply.done\""))).RootElement.GetProperty("data");
            Assert.Equal(7, done.GetProperty("przeniesione").GetInt32());
            Assert.Equal(3, done.GetProperty("pozycje").GetInt32());
            Assert.False(done.GetProperty("przerwano").GetBoolean());
            Assert.True(File.Exists(Path.Combine(tmp, "kosz", "p2", "k.rpf", "jbib_007_u.ydd")));
            Assert.True(File.Exists(Path.Combine(tmp, "kosz", "p3", "k.rpf", "feet_008_u.ydd")));
            Assert.False(File.Exists(Path.Combine(tmp, "p2", "k.rpf", "jbib_007_u.ydd")));
            Assert.True(File.Exists(Path.Combine(tmp, "p1", "k.rpf", "jbib_001_u.ydd")));   // zwyciezca nietkniety
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"history.changed\""));
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"compare.done\""));
            // po ponownym indeksowaniu i porownaniu nic nie zostaje do odrzucenia, plik historii jest
            var l = await Wywolaj(m, "groups.list", "{}");
            Assert.Equal(0, l.GetProperty("podsumowanie").GetProperty("doOdrzucenia").GetProperty("pliki").GetInt32());
            Assert.Single(Directory.GetFiles(s.Projekt.FolderHistorii, "*.json"));

            // historia
            var h = await Wywolaj(m, "history.list");
            Assert.Equal(1, h.GetProperty("wpisy").GetArrayLength());
            var wpis = h.GetProperty("wpisy")[0];
            Assert.Equal(7, wpis.GetProperty("pliki").GetInt32()); Assert.True(wpis.GetProperty("moznaCofnac").GetBoolean());
            Assert.False(wpis.TryGetProperty("cofnieto", out var cf) && cf.ValueKind != JsonValueKind.Null);
            var plik = wpis.GetProperty("plik").GetString();
            var g = await Wywolaj(m, "history.get", $"{{\"plik\":\"{plik.Replace("\\", "\\\\")}\"}}");
            var lista = g.GetProperty("wpis").GetProperty("lista");
            Assert.Equal(3, lista.GetArrayLength());
            var pozB = lista.EnumerateArray().First(x => x.GetProperty("nazwa").GetString().StartsWith("jbib_007"));
            Assert.Equal(3, pozB.GetProperty("pliki").GetArrayLength());
            Assert.True(pozB.GetProperty("pliki")[0].GetProperty("jest").GetBoolean());

            // cofnij tylko b
            wyslane.Clear();
            var u = await Wywolaj(m, "history.undo", $"{{\"plik\":\"{plik.Replace("\\", "\\\\")}\",\"pozycje\":[\"{pozB.GetProperty("id").GetString()}\"]}}");
            Assert.True(u.GetProperty("uruchomiono").GetBoolean());
            await Poczekaj(jr, wyslane, "undo.done");
            Assert.True(File.Exists(Path.Combine(tmp, "p2", "k.rpf", "jbib_007_u.ydd")));
            Assert.False(File.Exists(Path.Combine(tmp, "kosz", "p2", "k.rpf", "jbib_007_u.ydd")));
            Assert.True(File.Exists(Path.Combine(tmp, "kosz", "p2", "k.rpf", "feet_006_u.ydd")));   // f nadal w koszu
            h = await Wywolaj(m, "history.list");
            Assert.True(h.GetProperty("wpisy")[0].GetProperty("czesciowo").GetBoolean());
            Assert.True(h.GetProperty("wpisy")[0].GetProperty("moznaCofnac").GetBoolean());
            // b wrocilo do katalogu (ponowne indeksowanie p2) -> grupa a=b znow zywa, b znow do odrzucenia (1 pozycja, ale pliki-atrapy bez geometrii => grupa NIE powstanie)
            Assert.Contains(s.Katalog.Pozycje, p => p.Id == "p2|k.rpf|jbib|7|u");

            // cofnij reszte
            wyslane.Clear();
            await Wywolaj(m, "history.undo", $"{{\"plik\":\"{plik.Replace("\\", "\\\\")}\"}}");
            await Poczekaj(jr, wyslane, "undo.done");
            h = await Wywolaj(m, "history.list");
            Assert.True(h.GetProperty("wpisy")[0].TryGetProperty("cofnieto", out var cf2) && cf2.ValueKind == JsonValueKind.String);
            Assert.False(h.GetProperty("wpisy")[0].GetProperty("moznaCofnac").GetBoolean());
            Assert.False(Directory.Exists(Path.Combine(tmp, "kosz", "p2")));   // pusty kosz posprzatany
            // nic do cofniecia -> uruchomiono=false
            u = await Wywolaj(m, "history.undo", $"{{\"plik\":\"{plik.Replace("\\", "\\\\")}\"}}");
            Assert.False(u.GetProperty("uruchomiono").GetBoolean());
            // nieznany plik / poza folderem historii
            var blad = JsonDocument.Parse(await m.Obsluz("{\"id\":\"9\",\"cmd\":\"history.get\",\"args\":{\"plik\":\"C:\\\\Windows\\\\win.ini\"}}")).RootElement;
            Assert.Equal("not_found", blad.GetProperty("error").GetProperty("code").GetString());
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task Run_bez_niczego_do_przeniesienia_nie_startuje()
    {
        var (m, s, jr, wyslane, tmp) = Zbuduj();
        try
        {
            // wszystkie grupy zignorowane -> plan pusty
            foreach (var g in Duble.App.Komendy.Grupy.Zywe(s)) s.Projekt.Decyzje[g.g.Id] = new Decyzja { Ignoruj = true };
            var r = await Wywolaj(m, "apply.run", "{}");
            Assert.False(r.GetProperty("uruchomiono").GetBoolean());
            Assert.Equal(0, r.GetProperty("plan").GetProperty("pliki").GetInt32());
            Assert.False(jr.Zajety);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
