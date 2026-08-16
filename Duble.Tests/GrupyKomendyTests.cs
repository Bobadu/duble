using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>Komendy groups.* / apply.preview na SZTUCZNYM katalogu (bez plikow gry): 3 grupy — DUPLIKAT a=b, PRZEMALOWANIE c=d, DUPLIKAT e=f=g.</summary>
public class GrupyKomendyTests
{
    static float[] Hist(int szczyt) { var h = new float[Geo.Kubelki]; h[szczyt] = 0.7f; h[Math.Min(Geo.Kubelki - 1, szczyt + 1)] = 0.3f; return h; }

    static Pozycja Poz(string tmp, string paczka, string typ, int numer, string hashPoz, float[] hist, params string[] shaTekstur)
    {
        var folder = Path.Combine(tmp, paczka, "k.rpf"); Directory.CreateDirectory(folder);
        var ydd = Path.Combine(folder, $"{typ}_{numer:d3}_u.ydd"); File.WriteAllBytes(ydd, new byte[100]);
        var p = new Pozycja
        {
            Id = $"{paczka}|k.rpf|{typ}|{numer}|u", Paczka = paczka, Kontener = "k.rpf", Typ = typ, Numer = numer, Sufiks = "u", ZrodloId = "z1",
            SciezkaYdd = ydd, BajtyYdd = 100,
            Geo = new Geo { HashPozycji = hashPoz, Trojkaty = 1000, Wierzcholki = 600, Hist = hist, Bbox = new[] { 0.5f, 0.3f, 0.6f }, Lody = 3 },
        };
        char litera = 'a';
        foreach (var sha in shaTekstur)
        {
            var plik = Path.Combine(folder, $"{typ}_diff_{numer:d3}_{litera++}_uni.ytd"); File.WriteAllBytes(plik, new byte[50]);
            p.Tekstury.Add(new Tekstura { Plik = Path.GetFileName(plik), Sciezka = plik, Sha = sha + paczka + numer, Bajty = 50, W = 1024, H = 1024, Mipy = 11, Format = "BC3", Zdekodowana = true, Wariancja = 30, PHash = new ulong[] { 1, 2, 3, 4 }, Kolor = Convert.ToBase64String(new byte[192]) });
        }
        return p;
    }

    static (Mostek m, Sesja s, List<string> wyslane, string tmp) Zbuduj()
    {
        var tmp = Sciezki.Tymczasowy("grupy");
        var wyslane = new List<string>();
        var m = new Mostek(new FalszyweOkno(), new FalszyweDialogi(), new Ustawienia(), wyslane.Add) { PlikUstawien = Path.Combine(tmp, "settings.json") };
        var s = new Sesja(); s.Nowy("G", Path.Combine(tmp, "G.duble"));
        s.Projekt.Zrodla.Add(new ZrodloProjektu { Id = "z1", Nazwa = "z1", Sciezka = tmp, Typ = "folder", Wlaczone = true });
        var a = Poz(tmp, "p1", "jbib", 1, "H1", Hist(10), "S1", "S2");
        var b = Poz(tmp, "p2", "jbib", 7, "H1", Hist(10), "S1", "S2");
        b.Tekstury.ForEach(t => t.Mipy = 1);
        var c = Poz(tmp, "p1", "lowr", 3, "H3", Hist(20), "T1");
        var d = Poz(tmp, "p2", "lowr", 9, "H3", Hist(20), "U1");
        d.Tekstury.ForEach(t => { t.PHash = new ulong[] { ulong.MaxValue, 0, ulong.MaxValue, 0 }; t.Kolor = Convert.ToBase64String(Enumerable.Repeat((byte)200, 192).ToArray()); });
        var e = Poz(tmp, "p1", "feet", 5, "H5", Hist(30), "V1");
        var f = Poz(tmp, "p2", "feet", 6, "H5", Hist(30), "V1"); f.Tekstury.ForEach(t => t.Mipy = 1);
        var g = Poz(tmp, "p3", "feet", 8, "H5", Hist(30), "V1"); g.Tekstury.ForEach(t => t.Mipy = 1);
        // te same grafiki: SHA rowne miedzy paczkami (Sha = sha+paczka+numer — ujednolic dla par a/b, e/f/g)
        foreach (var t in b.Tekstury) t.Sha = a.Tekstury[b.Tekstury.IndexOf(t)].Sha;
        foreach (var t in f.Tekstury) t.Sha = e.Tekstury[0].Sha; foreach (var t in g.Tekstury) t.Sha = e.Tekstury[0].Sha;
        s.ZmienKatalog(k => k.Wstaw(new[] { a, b, c, d, e, f, g }));
        s.Porownaj(default, null);
        s.Zapisz();
        var jr = new JobRunner(m.Zdarzenie);
        Duble.App.Komendy.Grupy.Zarejestruj(m, s, jr);
        return (m, s, wyslane, tmp);
    }

    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement.GetProperty("result");
    static async Task<JsonElement> Wywolaj(Mostek m, string cmd, string args = "null") => Odp(await m.Obsluz($"{{\"id\":\"1\",\"cmd\":\"{cmd}\",\"args\":{args}}}"));

    [Fact]
    public async Task Lista_filtry_decyzje_i_podglad_zastosowania()
    {
        var (m, s, wyslane, tmp) = Zbuduj();
        try
        {
            var l = await Wywolaj(m, "groups.list", "{}");
            Assert.Equal(3, l.GetProperty("grupy").GetArrayLength());
            var pod = l.GetProperty("podsumowanie");
            Assert.Equal(2, pod.GetProperty("duplikat").GetInt32()); Assert.Equal(1, pod.GetProperty("przemalowanie").GetInt32());
            Assert.Equal(3, pod.GetProperty("doOdrzucenia").GetProperty("pozycje").GetInt32());   // b, f, g
            Assert.Equal(Porownanie.Duplikat, l.GetProperty("grupy")[0].GetProperty("werdykt").GetString());   // duplikaty przed przemalowaniem
            Assert.Equal(3, l.GetProperty("grupy")[0].GetProperty("czlonkowie").GetArrayLength());            // wieksza grupa pierwsza
            Assert.True(l.GetProperty("filtry").GetProperty("sloty").GetArrayLength() >= 3);

            l = await Wywolaj(m, "groups.list", "{\"werdykty\":[\"PRZEMALOWANIE\"]}");
            Assert.Equal(1, l.GetProperty("grupy").GetArrayLength());
            l = await Wywolaj(m, "groups.list", "{\"szukaj\":\"jbib_007\"}");
            Assert.Equal(1, l.GetProperty("grupy").GetArrayLength());
            l = await Wywolaj(m, "groups.list", "{\"sloty\":[\"feet\"]}");
            Assert.Equal(1, l.GetProperty("grupy").GetArrayLength());

            // grupa a=b: zmien zwyciezce na b
            var gab = (await Wywolaj(m, "groups.list", "{\"sloty\":[\"jbib\"]}")).GetProperty("grupy")[0];
            var idAb = gab.GetProperty("id").GetString();
            var idB = gab.GetProperty("czlonkowie").EnumerateArray().First(c => c.GetProperty("numer").GetInt32() == 7).GetProperty("id").GetString();
            var idA = gab.GetProperty("czlonkowie").EnumerateArray().First(c => c.GetProperty("numer").GetInt32() == 1).GetProperty("id").GetString();
            var r = (await Wywolaj(m, "groups.decide", $"{{\"id\":\"{idAb}\",\"zwyciezca\":\"{idB}\"}}")).GetProperty("rozstrzygniecie");
            Assert.Equal(idB, r.GetProperty("zwyciezca").GetString());
            Assert.Equal(idA, r.GetProperty("odrzucone")[0].GetString());
            Assert.False(r.GetProperty("domyslna").GetBoolean());
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"groups.changed\""));
            Assert.True(File.ReadAllText(s.Projekt.Sciezka).Contains(idAb));   // decyzja zapisana w .duble

            // grupa e=f=g: to nie duplikat
            var gefg = (await Wywolaj(m, "groups.list", "{\"sloty\":[\"feet\"]}")).GetProperty("grupy")[0];
            var idEfg = gefg.GetProperty("id").GetString();
            await Wywolaj(m, "groups.decide", $"{{\"id\":\"{idEfg}\",\"ignoruj\":true,\"notatka\":\"inne buty\"}}");
            Assert.Equal(2, (await Wywolaj(m, "groups.list", "{}")).GetProperty("grupy").GetArrayLength());
            Assert.Equal(3, (await Wywolaj(m, "groups.list", "{\"zignorowane\":true}")).GetProperty("grupy").GetArrayLength());
            var prev = await Wywolaj(m, "apply.preview");
            Assert.Equal(1, prev.GetProperty("pozycje").GetInt32());        // tylko a
            Assert.Equal(3, prev.GetProperty("pliki").GetInt32());          // ydd + 2 ytd
            Assert.Equal(200, prev.GetProperty("bajty").GetInt64());        // 100 + 50 + 50

            // szczegoly grupy: dopasowania i rozpiska
            var g = (await Wywolaj(m, "groups.get", $"{{\"id\":\"{idAb}\"}}")).GetProperty("grupa");
            Assert.Equal(1, g.GetProperty("dopasowania").GetArrayLength());
            Assert.Equal(2, g.GetProperty("dopasowania")[0].GetProperty("pary").GetArrayLength());
            Assert.True(g.GetProperty("czlonkowie")[0].GetProperty("rozpiska").GetProperty("razem").GetDouble() > 0);
            Assert.Equal(2, g.GetProperty("czlonkowie")[0].GetProperty("tekstury").GetArrayLength());
            Assert.Equal("inne buty", (await Wywolaj(m, "groups.get", $"{{\"id\":\"{idEfg}\"}}")).GetProperty("grupa").GetProperty("rozstrzygniecie").GetProperty("notatka").GetString());

            // reset -> domyslne
            r = (await Wywolaj(m, "groups.reset", $"{{\"id\":\"{idEfg}\"}}")).GetProperty("rozstrzygniecie");
            Assert.True(r.GetProperty("domyslna").GetBoolean());
            Assert.Equal(3, (await Wywolaj(m, "groups.list", "{}")).GetProperty("grupy").GetArrayLength());

            // nieznana grupa
            var blad = JsonDocument.Parse(await m.Obsluz("{\"id\":\"9\",\"cmd\":\"groups.get\",\"args\":{\"id\":\"nie-ma\"}}")).RootElement;
            Assert.Equal("not_found", blad.GetProperty("error").GetProperty("code").GetString());
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task Compare_run_uruchamia_zadanie_i_daje_compare_done()
    {
        var (m, s, wyslane, tmp) = Zbuduj();
        try
        {
            var r = await Wywolaj(m, "compare.run");
            Assert.True(r.GetProperty("uruchomiono").GetBoolean());
            for (int i = 0; i < 100 && !wyslane.Any(w => w.Contains("\"event\":\"compare.done\"")); i++) await Task.Delay(50);
            Assert.Contains(wyslane, w => w.Contains("\"event\":\"compare.done\""));
        }
        finally { Directory.Delete(tmp, true); }
    }
}
