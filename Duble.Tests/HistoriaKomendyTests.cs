using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>report.exportHtml / report.exportCsv (jezyk UI, decyzje z projektu) na sztucznym katalogu.</summary>
public class HistoriaKomendyTests
{
    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Eksport_html_i_csv()
    {
        var tmp = Sciezki.Tymczasowy("eksport");
        try
        {
            var wyslane = new List<string>();
            var ust = new Ustawienia { Jezyk = "en" };
            var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), ust, wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
            var s = new Sesja(); s.Nowy("Moj projekt", Path.Combine(tmp, "proj", "Moj projekt.duble"));
            Sztuczne.SiedemZeZrodlami(s, tmp);
            var jr = new JobRunner(m.Zdarzenie);
            Duble.App.Komendy.Grupy.Zarejestruj(m, s, jr);
            Duble.App.Komendy.Historia.Zarejestruj(m, s, jr);

            // bez porownania -> not_found
            var o = Odp(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"report.exportCsv\",\"args\":{\"sciezka\":\"x\"}}"));
            Assert.Equal("not_found", o.GetProperty("error").GetProperty("code").GetString());
            s.Porownaj(default, null); s.Zapisz();

            // decyzja: grupa e=f=g zignorowana
            var efg = Duble.App.Komendy.Grupy.Zywe(s).First(x => x.g.Pozycje.Count == 3).g;
            await m.Obsluz("{\"id\":\"2\",\"cmd\":\"groups.decide\",\"args\":{\"id\":\"" + efg.Id + "\",\"ignoruj\":true,\"notatka\":\"other boots\"}}");

            var csv = Path.Combine(tmp, "out", "g.csv").Replace("\\", "\\\\");
            o = Odp(await m.Obsluz("{\"id\":\"3\",\"cmd\":\"report.exportCsv\",\"args\":{\"sciezka\":\"" + csv + "\"}}"));
            Assert.True(o.GetProperty("ok").GetBoolean(), o.ToString());
            var bajty = File.ReadAllBytes(Path.Combine(tmp, "out", "g.csv"));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bajty.Take(3).ToArray());   // BOM (Excel)
            var tresc = File.ReadAllText(Path.Combine(tmp, "out", "g.csv"));         // ReadAllText zdejmuje BOM
            Assert.StartsWith("group,verdict,", tresc);                               // jezyk UI = en -> naglowki EN, przecinek
            Assert.Contains(",ignored,other boots,", tresc);
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"report.done\"") && w.Contains("\"typ\":\"csv\""));

            var html = Path.Combine(tmp, "out", "r.html").Replace("\\", "\\\\");
            o = Odp(await m.Obsluz("{\"id\":\"4\",\"cmd\":\"report.exportHtml\",\"args\":{\"sciezka\":\"" + html + "\"}}"));
            Assert.True(o.GetProperty("ok").GetBoolean(), o.ToString());
            Assert.True(o.GetProperty("result").GetProperty("uruchomiono").GetBoolean());
            for (int i = 0; i < 300 && !wyslane.Any(w => w.Contains("\"typ\":\"html\"")); i++) await Task.Delay(50);
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"report.done\"") && w.Contains("\"typ\":\"html\""));
            var h = File.ReadAllText(Path.Combine(tmp, "out", "r.html"));
            Assert.StartsWith("<!doctype html>", h);
            Assert.Contains("<html lang=\"en\">", h); Assert.Contains("Duble — Moj projekt", h);
            Assert.Contains("NOT A DUPLICATE", h); Assert.Contains("other boots", h);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
