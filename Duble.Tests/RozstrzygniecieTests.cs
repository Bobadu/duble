using System.Collections.Generic;
using Duble;
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
}
