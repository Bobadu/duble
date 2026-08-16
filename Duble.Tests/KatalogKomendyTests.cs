using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>catalog.list (filtry, szukanie, problemy, w grupach) i catalog.item (tekstury, jakosc, grupy) na sztucznym katalogu.</summary>
public class KatalogKomendyTests
{
    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement.GetProperty("result");
    static async Task<JsonElement> Wywolaj(Mostek m, string cmd, string args = "null") => Odp(await m.Obsluz($"{{\"id\":\"1\",\"cmd\":\"{cmd}\",\"args\":{args}}}"));

    [Fact]
    public async Task Lista_filtry_i_karta_pozycji()
    {
        var tmp = Sciezki.Tymczasowy("katalog");
        try
        {
            var wyslane = new List<string>();
            var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), new Ustawienia(), wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
            var s = new Sesja(); s.Nowy("K", Path.Combine(tmp, "proj", "K.duble"));
            Sztuczne.SiedemZeZrodlami(s, tmp);
            s.Porownaj(default, null); s.Zapisz();
            Duble.App.Komendy.Grupy.Zarejestruj(m, s, new JobRunner(m.Zdarzenie));
            Duble.App.Komendy.KatalogPozycji.Zarejestruj(m, s);

            var l = await Wywolaj(m, "catalog.list", "{}");
            Assert.Equal(7, l.GetProperty("razem").GetInt32());
            Assert.Equal(7, l.GetProperty("pokazane").GetInt32());
            Assert.Equal(7, l.GetProperty("pozycje").GetArrayLength());
            Assert.Equal(3, l.GetProperty("filtry").GetProperty("zrodla").GetArrayLength());
            Assert.Equal(3, l.GetProperty("filtry").GetProperty("sloty").GetArrayLength());
            Assert.Equal(7, l.GetProperty("filtry").GetProperty("formaty").GetProperty("legacy").GetInt32());
            var b = l.GetProperty("pozycje").EnumerateArray().First(p => p.GetProperty("typ").GetString() == "jbib" && p.GetProperty("numer").GetInt32() == 7);
            Assert.True(b.GetProperty("bezMipow").GetBoolean());
            Assert.Equal(Porownanie.Duplikat, b.GetProperty("grupa").GetString());
            var c = l.GetProperty("pozycje").EnumerateArray().First(p => p.GetProperty("typ").GetString() == "lowr" && p.GetProperty("numer").GetInt32() == 3);
            Assert.Equal(Porownanie.Przemalowanie, c.GetProperty("grupa").GetString());

            Assert.Equal(3, (await Wywolaj(m, "catalog.list", "{\"sloty\":[\"feet\"]}")).GetProperty("pozycje").GetArrayLength());
            Assert.Equal(3, (await Wywolaj(m, "catalog.list", "{\"zrodla\":[\"z-p2\"]}")).GetProperty("pozycje").GetArrayLength());
            Assert.Equal(3, (await Wywolaj(m, "catalog.list", "{\"problemy\":true}")).GetProperty("pozycje").GetArrayLength());   // b, f, g bez mipow
            Assert.Equal(7, (await Wywolaj(m, "catalog.list", "{\"wGrupie\":true}")).GetProperty("pozycje").GetArrayLength());     // wszystkie sa w jakiejs grupie
            Assert.Equal(0, (await Wywolaj(m, "catalog.list", "{\"formaty\":[\"gen9\"]}")).GetProperty("pozycje").GetArrayLength());
            Assert.Equal(1, (await Wywolaj(m, "catalog.list", "{\"szukaj\":\"jbib_007\"}")).GetProperty("pozycje").GetArrayLength());

            // zignorowana grupa nie liczy sie jako "w grupie"
            var efg = Duble.App.Komendy.Grupy.Zywe(s).First(x => x.g.Pozycje.Count == 3).g;
            s.Projekt.Decyzje[efg.Id] = new Decyzja { Ignoruj = true };
            Assert.Equal(4, (await Wywolaj(m, "catalog.list", "{\"wGrupie\":true}")).GetProperty("pozycje").GetArrayLength());

            var it = await Wywolaj(m, "catalog.item", "{\"id\":\"" + b.GetProperty("id").GetString() + "\"}");
            var poz = it.GetProperty("pozycja");
            Assert.Equal(2, poz.GetProperty("tekstury").GetArrayLength());
            Assert.True(poz.GetProperty("rozpiska").GetProperty("razem").GetDouble() >= 0);
            Assert.Equal("p2", poz.GetProperty("zrodlo").GetString());
            Assert.EndsWith("p2", poz.GetProperty("zrodloSciezka").GetString());
            Assert.Equal(1, it.GetProperty("grupy").GetArrayLength());
            var gr = it.GetProperty("grupy")[0];
            Assert.Equal(Porownanie.Duplikat, gr.GetProperty("werdykt").GetString());
            Assert.Equal("odrzucona", gr.GetProperty("stan").GetString());
            Assert.Equal(1, gr.GetProperty("inni").GetArrayLength());
            Assert.Equal("jbib_001", gr.GetProperty("inni")[0].GetProperty("nazwa").GetString());

            var blad = JsonDocument.Parse(await m.Obsluz("{\"id\":\"9\",\"cmd\":\"catalog.item\",\"args\":{\"id\":\"nie-ma\"}}")).RootElement;
            Assert.Equal("not_found", blad.GetProperty("error").GetProperty("code").GetString());
        }
        finally { Directory.Delete(tmp, true); }
    }
}
