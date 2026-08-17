using System.Collections.Generic;
using Xunit;

namespace Duble.Tests;

public class ResolutionTests
{
    static readonly IResolutionService Rules = new ResolutionService();

    static DuplicateGroup G(Verdict werdykt, string zw, params string[] ids)
        => new DuplicateGroup { Id = "g", Verdict = werdykt, Winner = zw, Members = new List<string>(ids) };

    [Fact]
    public void Domyslnie_duplikat_odrzuca_przegranych_a_przemalowanie_nikogo()
    {
        var r = Rules.Resolve(G(Verdict.Duplicate, "a", "a", "b", "c"), null);
        Assert.True(r.IsDefault); Assert.Equal("a", r.Winner); Assert.Equal(new[] { "b", "c" }, r.Rejected); Assert.False(r.Ignored);
        r = Rules.Resolve(G(Verdict.Superset, "b", "a", "b"), null);
        Assert.Equal(new[] { "a" }, r.Rejected);
        r = Rules.Resolve(G(Verdict.Retexture, "a", "a", "b"), null);
        Assert.Empty(r.Rejected);
        r = Rules.Resolve(G(Verdict.NeedsReview, "a", "a", "b"), null);
        Assert.Empty(r.Rejected);
    }

    [Fact]
    public void Decyzja_jest_autorytatywna_takze_pusta()
    {
        var g = G(Verdict.Duplicate, "a", "a", "b", "c");
        var r = Rules.Resolve(g, new Decision { Winner = "b", Rejected = { "a" } });
        Assert.False(r.IsDefault); Assert.Equal("b", r.Winner); Assert.Equal(new[] { "a" }, r.Rejected);   // c zostaje, choc nie jest zwyciezca
        r = Rules.Resolve(g, new Decision { Rejected = new List<string>() });
        Assert.Equal("a", r.Winner); Assert.Empty(r.Rejected);
        r = Rules.Resolve(g, new Decision { Ignored = true, Note = "to inne buty" });
        Assert.True(r.Ignored); Assert.Empty(r.Rejected); Assert.Equal("to inne buty", r.Note);
        // odrzucony nie moze byc zwyciezca ani ktos spoza grupy
        r = Rules.Resolve(g, new Decision { Winner = "a", Rejected = { "a", "x", "b" } });
        Assert.Equal(new[] { "b" }, r.Rejected);
    }

    [Fact]
    public void Decyzja_rowna_domyslnej_znowu_jest_domyslna()
    {
        var g = G(Verdict.Duplicate, "a", "a", "b", "c");
        // „to nie duplikat" -> nie domyslna; „jednak duplikat" (wpis zostaje, ale wynik = domyslny) -> znowu domyslna
        var d = new Decision { Winner = "a", Rejected = { "b", "c" }, Ignored = true };
        Assert.False(Rules.Resolve(g, d).IsDefault);
        d.Ignored = false;
        Assert.True(Rules.Resolve(g, d).IsDefault);
        d.Note = "sprawdzic pozniej";                                     // sama notatka = decyzja uzytkownika
        Assert.False(Rules.Resolve(g, d).IsDefault);
        d.Note = null; d.Rejected = new List<string> { "b" };             // c zachowane recznie
        Assert.False(Rules.Resolve(g, d).IsDefault);
        // do wgladu: domyslnie nikt nie odpada, wiec pusta lista = domyslna
        var w = G(Verdict.NeedsReview, "a", "a", "b");
        Assert.True(Rules.Resolve(w, new Decision { Winner = "a", Rejected = new List<string>() }).IsDefault);
        Assert.False(Rules.Resolve(w, new Decision { Winner = "b" }).IsDefault);
    }

    static DuplicateGroup Gid(Verdict werdykt, string zw, params string[] ids) { var g = G(werdykt, zw, ids); g.Id = DuplicateGroup.ComputeId(g.Members); return g; }

    [Fact]
    public void Migracja_decyzji_na_podgrupy_po_ponownym_porownaniu()
    {
        var abc = Gid(Verdict.Duplicate, "a", "a", "b", "c");
        var xy = Gid(Verdict.Duplicate, "x", "x", "y");
        var decyzje = new Dictionary<string, Decision>
        {
            [abc.Id] = new Decision { Winner = "a", Rejected = { "b" }, Note = "c zostaje" },   // c zachowane recznie
            [xy.Id] = new Decision { Ignored = true, Note = "inne buty" },
        };
        // po Zastosuj: b zniknelo -> nowa grupa {a,c}; xy: y zniknelo z innego powodu -> {x, w} nie jest podzbiorem -> bez decyzji; {y} pojedyncze nie wystepuje
        var ac = Gid(Verdict.Duplicate, "a", "a", "c");
        var xw = Gid(Verdict.Duplicate, "x", "x", "w");
        var yx = Gid(Verdict.Duplicate, "y", "y", "x");   // ta sama para w innej kolejnosci = ten sam id (PoliczId sortuje) -> juz ma decyzje
        int dodane = Rules.CarryOver(decyzje, new[] { abc, xy }, new[] { ac, xw, yx });
        Assert.Equal(1, dodane);
        var d = decyzje[ac.Id];
        Assert.Equal("a", d.Winner); Assert.Empty(d.Rejected); Assert.Equal("c zostaje", d.Note);
        var r = Rules.Resolve(ac, d);
        Assert.False(r.IsDefault); Assert.Empty(r.Rejected);          // c NIE wraca do "do odrzucenia"
        Assert.False(decyzje.ContainsKey(xw.Id));
        Assert.True(decyzje.ContainsKey(yx.Id));                       // = xy.Id
        // ignorowana nadgrupa -> podgrupa tez ignorowana; zwyciezca spoza podgrupy -> null (domyslny z grupy)
        var bc = Gid(Verdict.Duplicate, "b", "b", "c");
        var abcd = Gid(Verdict.Duplicate, "d", "a", "b", "c", "d");
        var dec2 = new Dictionary<string, Decision> { [abcd.Id] = new Decision { Winner = "d", Rejected = { "a", "b" }, Ignored = true } };
        Assert.Equal(1, Rules.CarryOver(dec2, new[] { abcd }, new[] { bc }));
        Assert.True(dec2[bc.Id].Ignored); Assert.Null(dec2[bc.Id].Winner); Assert.Equal(new[] { "b" }, dec2[bc.Id].Rejected);
        // brak decyzji / brak starych grup -> nic
        Assert.Equal(0, Rules.CarryOver(new Dictionary<string, Decision>(), new[] { abc }, new[] { ac }));
        Assert.Equal(0, Rules.CarryOver(decyzje, null, new[] { ac }));
    }
}
