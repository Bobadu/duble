using System.Collections.Generic;
using Duble.Core;
using Xunit;

namespace Duble.Tests;

public class RozstrzygniecieTests
{
    static Grupa G(string werdykt, string zw, params string[] ids) => new Grupa { Id = "g", Werdykt = werdykt, Zwyciezca = zw, Pozycje = new List<string>(ids) };

    [Fact]
    public void Domyslnie_duplikat_odrzuca_przegranych_a_przemalowanie_nikogo()
    {
        var r = Rozstrzygniecie.Policz(G(Porownanie.Duplikat, "a", "a", "b", "c"), null);
        Assert.True(r.Domyslna); Assert.Equal("a", r.Zwyciezca); Assert.Equal(new[] { "b", "c" }, r.Odrzucone); Assert.False(r.Ignoruj);
        r = Rozstrzygniecie.Policz(G(Porownanie.Nadzbior, "b", "a", "b"), null);
        Assert.Equal(new[] { "a" }, r.Odrzucone);
        r = Rozstrzygniecie.Policz(G(Porownanie.Przemalowanie, "a", "a", "b"), null);
        Assert.Empty(r.Odrzucone);
        r = Rozstrzygniecie.Policz(G(Porownanie.DoWgladu, "a", "a", "b"), null);
        Assert.Empty(r.Odrzucone);
    }

    [Fact]
    public void Decyzja_jest_autorytatywna_takze_pusta()
    {
        var g = G(Porownanie.Duplikat, "a", "a", "b", "c");
        var r = Rozstrzygniecie.Policz(g, new Decyzja { Zwyciezca = "b", Odrzucone = { "a" } });
        Assert.False(r.Domyslna); Assert.Equal("b", r.Zwyciezca); Assert.Equal(new[] { "a" }, r.Odrzucone);   // c zostaje, choc nie jest zwyciezca
        r = Rozstrzygniecie.Policz(g, new Decyzja { Odrzucone = new List<string>() });
        Assert.Equal("a", r.Zwyciezca); Assert.Empty(r.Odrzucone);
        r = Rozstrzygniecie.Policz(g, new Decyzja { Ignoruj = true, Notatka = "to inne buty" });
        Assert.True(r.Ignoruj); Assert.Empty(r.Odrzucone); Assert.Equal("to inne buty", r.Notatka);
        // odrzucony nie moze byc zwyciezca ani ktos spoza grupy
        r = Rozstrzygniecie.Policz(g, new Decyzja { Zwyciezca = "a", Odrzucone = { "a", "x", "b" } });
        Assert.Equal(new[] { "b" }, r.Odrzucone);
    }

    [Fact]
    public void Decyzja_rowna_domyslnej_znowu_jest_domyslna()
    {
        var g = G(Porownanie.Duplikat, "a", "a", "b", "c");
        // „to nie duplikat" -> nie domyslna; „jednak duplikat" (wpis zostaje, ale wynik = domyslny) -> znowu domyslna
        var d = new Decyzja { Zwyciezca = "a", Odrzucone = { "b", "c" }, Ignoruj = true };
        Assert.False(Rozstrzygniecie.Policz(g, d).Domyslna);
        d.Ignoruj = false;
        Assert.True(Rozstrzygniecie.Policz(g, d).Domyslna);
        d.Notatka = "sprawdzic pozniej";                                     // sama notatka = decyzja uzytkownika
        Assert.False(Rozstrzygniecie.Policz(g, d).Domyslna);
        d.Notatka = null; d.Odrzucone = new List<string> { "b" };             // c zachowane recznie
        Assert.False(Rozstrzygniecie.Policz(g, d).Domyslna);
        // do wgladu: domyslnie nikt nie odpada, wiec pusta lista = domyslna
        var w = G(Porownanie.DoWgladu, "a", "a", "b");
        Assert.True(Rozstrzygniecie.Policz(w, new Decyzja { Zwyciezca = "a", Odrzucone = new List<string>() }).Domyslna);
        Assert.False(Rozstrzygniecie.Policz(w, new Decyzja { Zwyciezca = "b" }).Domyslna);
    }

    static Grupa Gid(string werdykt, string zw, params string[] ids) { var g = G(werdykt, zw, ids); g.Id = Grupa.PoliczId(g.Pozycje); return g; }

    [Fact]
    public void Migracja_decyzji_na_podgrupy_po_ponownym_porownaniu()
    {
        var abc = Gid(Porownanie.Duplikat, "a", "a", "b", "c");
        var xy = Gid(Porownanie.Duplikat, "x", "x", "y");
        var decyzje = new Dictionary<string, Decyzja>
        {
            [abc.Id] = new Decyzja { Zwyciezca = "a", Odrzucone = { "b" }, Notatka = "c zostaje" },   // c zachowane recznie
            [xy.Id] = new Decyzja { Ignoruj = true, Notatka = "inne buty" },
        };
        // po Zastosuj: b zniknelo -> nowa grupa {a,c}; xy: y zniknelo z innego powodu -> {x, w} nie jest podzbiorem -> bez decyzji; {y} pojedyncze nie wystepuje
        var ac = Gid(Porownanie.Duplikat, "a", "a", "c");
        var xw = Gid(Porownanie.Duplikat, "x", "x", "w");
        var yx = Gid(Porownanie.Duplikat, "y", "y", "x");   // ta sama para w innej kolejnosci = ten sam id (PoliczId sortuje) -> juz ma decyzje
        int dodane = Rozstrzygniecie.PrzeniesDecyzje(decyzje, new[] { abc, xy }, new[] { ac, xw, yx });
        Assert.Equal(1, dodane);
        var d = decyzje[ac.Id];
        Assert.Equal("a", d.Zwyciezca); Assert.Empty(d.Odrzucone); Assert.Equal("c zostaje", d.Notatka);
        var r = Rozstrzygniecie.Policz(ac, d);
        Assert.False(r.Domyslna); Assert.Empty(r.Odrzucone);          // c NIE wraca do "do odrzucenia"
        Assert.False(decyzje.ContainsKey(xw.Id));
        Assert.True(decyzje.ContainsKey(yx.Id));                       // = xy.Id
        // ignorowana nadgrupa -> podgrupa tez ignorowana; zwyciezca spoza podgrupy -> null (domyslny z grupy)
        var bc = Gid(Porownanie.Duplikat, "b", "b", "c");
        var abcd = Gid(Porownanie.Duplikat, "d", "a", "b", "c", "d");
        var dec2 = new Dictionary<string, Decyzja> { [abcd.Id] = new Decyzja { Zwyciezca = "d", Odrzucone = { "a", "b" }, Ignoruj = true } };
        Assert.Equal(1, Rozstrzygniecie.PrzeniesDecyzje(dec2, new[] { abcd }, new[] { bc }));
        Assert.True(dec2[bc.Id].Ignoruj); Assert.Null(dec2[bc.Id].Zwyciezca); Assert.Equal(new[] { "b" }, dec2[bc.Id].Odrzucone);
        // brak decyzji / brak starych grup -> nic
        Assert.Equal(0, Rozstrzygniecie.PrzeniesDecyzje(new Dictionary<string, Decyzja>(), new[] { abc }, new[] { ac }));
        Assert.Equal(0, Rozstrzygniecie.PrzeniesDecyzje(decyzje, null, new[] { ac }));
    }
}
