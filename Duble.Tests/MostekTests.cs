using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class FalszyweOkno : IOkno
{
    public List<string> Log = new(); public bool Zmaksymalizowane { get; set; }
    public void Minimalizuj() => Log.Add("min");
    public void MaksymalizujAlboPrzywroc() { Zmaksymalizowane = !Zmaksymalizowane; Log.Add("max"); }
    public void Zamknij() => Log.Add("close");
    public void RozpocznijPrzeciaganie() => Log.Add("drag");
    public void Uruchom(Action a) => a();
}

public class FalszyweDialogi : IDialogi
{
    public string Folder = @"C:\wybrany"; public string[] Files = { @"C:\a.rpf" };
    public string WybierzFolder(string tytul, string start) => Folder;
    public string[] WybierzPliki(string tytul, string filtr, bool wiele, string start) => Files;
    public string ZapiszPlik(string tytul, string filtr, string domyslnaNazwa, string start) => @"C:\zapis.duble";
}

public class MostekTests
{
    static (Mostek m, FalszyweOkno okno, List<string> wyslane) Zbuduj()
    {
        var okno = new FalszyweOkno(); var wyslane = new List<string>();
        var m = new Mostek(okno, new FalszyweDialogi(), new Ustawienia { Jezyk = "pl", Motyw = "dark" }, wyslane.Add);
        Duble.App.Komendy.Okno.Zarejestruj(m);
        return (m, okno, wyslane);
    }
    static JsonElement Odp(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Nieznana_komenda_daje_blad_z_id()
    {
        var (m, _, _) = Zbuduj();
        var o = Odp(await m.Obsluz("{\"id\":\"7\",\"cmd\":\"nie.ma\",\"args\":null}"));
        Assert.Equal("7", o.GetProperty("id").GetString()); Assert.False(o.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown_command", o.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Wyjatek_w_handlerze_daje_internal_a_nie_wywala()
    {
        var (m, _, _) = Zbuduj();
        m.Rejestruj("test.boom", _ => throw new InvalidOperationException("bum"));
        var o = Odp(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"test.boom\"}"));
        Assert.False(o.GetProperty("ok").GetBoolean()); Assert.Equal("internal", o.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("bum", o.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Okno_i_ustawienia_dzialaja()
    {
        var (m, okno, wyslane) = Zbuduj();
        await m.Obsluz("{\"id\":\"1\",\"cmd\":\"window.minimize\"}");
        var st = Odp(await m.Obsluz("{\"id\":\"2\",\"cmd\":\"window.maximize\"}"));
        Assert.Equal(new[] { "min", "max" }, okno.Log);
        Assert.True(st.GetProperty("result").GetProperty("maks").GetBoolean());
        var s = Odp(await m.Obsluz("{\"id\":\"3\",\"cmd\":\"settings.get\"}"));
        Assert.Equal("pl", s.GetProperty("result").GetProperty("jezyk").GetString());
        var plik = System.IO.Path.Combine(Sciezki.Tymczasowy("mostek-ust"), "settings.json");
        m.PlikUstawien = plik;
        await m.Obsluz("{\"id\":\"4\",\"cmd\":\"settings.set\",\"args\":{\"motyw\":\"light\",\"jezyk\":\"en\"}}");
        s = Odp(await m.Obsluz("{\"id\":\"5\",\"cmd\":\"settings.get\"}"));
        Assert.Equal("light", s.GetProperty("result").GetProperty("motyw").GetString()); Assert.Equal("en", s.GetProperty("result").GetProperty("jezyk").GetString());
        Assert.True(System.IO.File.Exists(plik));
        m.Zdarzenie("test.ping", new { x = 1 });
        Assert.Contains(wyslane, w => w.Contains("\"event\":\"test.ping\"") && w.Contains("\"x\":1"));
    }

    [Fact]
    public async Task Dialogi_przez_interfejs()
    {
        var (m, _, _) = Zbuduj();
        var o = Odp(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"dialogs.pickFolder\",\"args\":{\"tytul\":\"x\"}}"));
        Assert.Equal(@"C:\wybrany", o.GetProperty("result").GetProperty("sciezka").GetString());
        o = Odp(await m.Obsluz("{\"id\":\"2\",\"cmd\":\"dialogs.pickFiles\",\"args\":{\"filtr\":\"rpf\"}}"));
        Assert.Equal(1, o.GetProperty("result").GetProperty("sciezki").GetArrayLength());
    }

    [Fact]
    public async Task Zle_argumenty_daja_bad_args()
    {
        var (m, _, _) = Zbuduj();
        var o = Odp(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"shell.openFolder\",\"args\":{}}"));
        Assert.Equal("bad_args", o.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task App_info_ma_nazwe_i_wersje()
    {
        var (m, _, _) = Zbuduj();
        var o = Odp(await m.Obsluz("{\"id\":\"1\",\"cmd\":\"app.info\"}"));
        Assert.Equal("Duble", o.GetProperty("result").GetProperty("nazwa").GetString());
        Assert.Equal("Bobadu", o.GetProperty("result").GetProperty("by").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+", o.GetProperty("result").GetProperty("wersja").GetString());
    }
}
