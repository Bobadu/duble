using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Duble.Core;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>
/// Zloty wzorzec: wynik porownania zapisany przez CLI SPRZED refaktoru (tools\Duble\Duble.Tests\golden\).
/// Po kazdej zmianie silnika porownanie tych samych zrodel musi dac to samo — grupy, werdykty,
/// zwyciezcow, punkty i (po polsku) teksty powodow.
/// </summary>
public class ZlotyWzorzecTests
{
    readonly ITestOutputHelper wyj;
    public ZlotyWzorzecTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    // ---- ksztalt zlotego pliku (dzisiejszy WynikPorownania z tekstowymi powodami) ----
    public class ZPara { public string A { get; set; } public string B { get; set; } public string Werdykt { get; set; } public string Powod { get; set; } public double DistGeo { get; set; } public double PokrycieA { get; set; } public double PokrycieB { get; set; } public int WspolnychTekstur { get; set; } }
    public class ZGrupa { public List<string> Pozycje { get; set; } public string Werdykt { get; set; } public string Zwyciezca { get; set; } public string Powod { get; set; } public List<ZPara> Pary { get; set; } public Dictionary<string, double> Punkty { get; set; } public Dictionary<string, string> Rozpiska { get; set; } }
    public class ZWynik { public List<ZGrupa> Grupy { get; set; } public List<string> Podsumowanie { get; set; } }

    static string KluczGrupy(IEnumerable<string> ids) => string.Join("\n", ids.OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Sprowadza wynik z Core do ksztaltu zlotego (w kolejnych zadaniach Powod/Rozpiska staja sie obiektami — wtedy TU formatujemy je po polsku).</summary>
    static ZWynik NaZloty(WynikPorownania w) => new ZWynik
    {
        Podsumowanie = w.Podsumowanie,
        Grupy = w.Grupy.Select(g => new ZGrupa
        {
            Pozycje = g.Pozycje, Werdykt = g.Werdykt, Zwyciezca = g.Zwyciezca,
            Powod = TekstPowodu(g),
            Pary = g.Pary.Select(p => new ZPara { A = p.A, B = p.B, Werdykt = p.Werdykt, Powod = TekstPowodu(p), DistGeo = p.DistGeo, PokrycieA = p.PokrycieA, PokrycieB = p.PokrycieB, WspolnychTekstur = p.WspolnychTekstur }).ToList(),
            Punkty = g.Punkty,
            Rozpiska = g.Rozpiska.ToDictionary(k => k.Key, k => TekstRozpiski(k.Value)),
        }).ToList()
    };
    // Powod/Rozpiska sa kodami+parametrami; wzorzec ma stare polskie napisy — formatter PL musi je odtworzyc co do znaku.
    static string TekstPowodu(Grupa g) => Teksty.Powod(g.Powod, "pl");
    static string TekstPowodu(Para p) => Teksty.Powod(p.Powod, "pl");
    static string TekstRozpiski(Punktacja r) => r.Tekst("pl");

    void Porownaj(ZWynik zloty, ZWynik nowy)
    {
        Assert.Equal(zloty.Podsumowanie, nowy.Podsumowanie);
        var zg = zloty.Grupy.ToDictionary(g => KluczGrupy(g.Pozycje));
        var ng = nowy.Grupy.ToDictionary(g => KluczGrupy(g.Pozycje));
        var brak = zg.Keys.Except(ng.Keys).ToList(); var nadmiar = ng.Keys.Except(zg.Keys).ToList();
        foreach (var b in brak) wyj.WriteLine("BRAK GRUPY: " + b.Replace("\n", " = "));
        foreach (var n in nadmiar) wyj.WriteLine("NOWA GRUPA: " + n.Replace("\n", " = "));
        Assert.Empty(brak); Assert.Empty(nadmiar);
        foreach (var k in zg.Keys)
        {
            var a = zg[k]; var b = ng[k];
            Assert.Equal(a.Werdykt, b.Werdykt); Assert.Equal(a.Zwyciezca, b.Zwyciezca); Assert.Equal(a.Powod, b.Powod);
            foreach (var id in a.Punkty.Keys) Assert.Equal(a.Punkty[id], b.Punkty[id], 6);
            foreach (var id in a.Rozpiska.Keys) Assert.Equal(a.Rozpiska[id], b.Rozpiska[id]);
            var pa = a.Pary.OrderBy(p => p.A + p.B).ToList(); var pb = b.Pary.OrderBy(p => p.A + p.B).ToList();
            Assert.Equal(pa.Count, pb.Count);
            for (int i = 0; i < pa.Count; i++)
            {
                Assert.Equal(pa[i].A, pb[i].A); Assert.Equal(pa[i].B, pb[i].B); Assert.Equal(pa[i].Werdykt, pb[i].Werdykt);
                Assert.Equal(pa[i].Powod, pb[i].Powod); Assert.Equal(pa[i].DistGeo, pb[i].DistGeo, 9);
                Assert.Equal(pa[i].PokrycieA, pb[i].PokrycieA, 9); Assert.Equal(pa[i].PokrycieB, pb[i].PokrycieB, 9);
                Assert.Equal(pa[i].WspolnychTekstur, pb[i].WspolnychTekstur);
            }
        }
    }

    /// <summary>
    /// Zloty plik moze byc w STARYM ksztalcie (Powod/Rozpiska = polski tekst; legacy4, zapisany CLI sprzed refaktoru)
    /// albo w NOWYM (Powod = {Kod,P}, Rozpiska = Punktacja; gen9, zapisany po Zadaniu 6, bo stare CLI nie czytalo .rpf).
    /// Oba sprowadzamy do tekstu PL — dzieki temu jedna procedura porownania obsluguje oba.
    /// </summary>
    static ZWynik WczytajZloty(string plik)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Sciezki.Golden(plik)));
        var root = doc.RootElement;
        string Tekst(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.String) return e.GetString();
            if (e.ValueKind == JsonValueKind.Null || e.ValueKind == JsonValueKind.Undefined) return null;
            if (e.TryGetProperty("Kod", out _)) return Teksty.Powod(JsonSerializer.Deserialize<Powod>(e.GetRawText()), "pl");
            if (e.TryGetProperty("Razem", out _)) return JsonSerializer.Deserialize<Punktacja>(e.GetRawText()).Tekst("pl");
            throw new InvalidDataException("nieznany ksztalt: " + e.GetRawText());
        }
        JsonElement Pole(JsonElement e, string nazwa) => e.TryGetProperty(nazwa, out var v) ? v : default;
        var wynik = new ZWynik { Grupy = new(), Podsumowanie = Pole(root, "Podsumowanie").EnumerateArray().Select(x => x.GetString()).ToList() };
        foreach (var g in Pole(root, "Grupy").EnumerateArray())
        {
            var zg = new ZGrupa
            {
                Pozycje = Pole(g, "Pozycje").EnumerateArray().Select(x => x.GetString()).ToList(),
                Werdykt = Pole(g, "Werdykt").GetString(), Zwyciezca = Pole(g, "Zwyciezca").GetString(),
                Powod = Tekst(Pole(g, "Powod")),
                Pary = new(), Punkty = new(), Rozpiska = new(),
            };
            foreach (var p in Pole(g, "Pary").EnumerateArray())
                zg.Pary.Add(new ZPara
                {
                    A = Pole(p, "A").GetString(), B = Pole(p, "B").GetString(), Werdykt = Pole(p, "Werdykt").GetString(),
                    Powod = Tekst(Pole(p, "Powod")), DistGeo = Pole(p, "DistGeo").GetDouble(),
                    PokrycieA = Pole(p, "PokrycieA").GetDouble(), PokrycieB = Pole(p, "PokrycieB").GetDouble(),
                    WspolnychTekstur = Pole(p, "WspolnychTekstur").GetInt32(),
                });
            foreach (var kv in Pole(g, "Punkty").EnumerateObject()) zg.Punkty[kv.Name] = kv.Value.GetDouble();
            foreach (var kv in Pole(g, "Rozpiska").EnumerateObject()) zg.Rozpiska[kv.Name] = Tekst(kv.Value);
            wynik.Grupy.Add(zg);
        }
        return wynik;
    }

    [Fact, Trait("Kategoria", "Wolny")]
    public void Legacy4_z_downloads_daje_zloty_wynik()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY: brak downloads\\vrp_clothes_f_civil01 itd."); return; }
        var katalog = new Katalog();
        foreach (var p in new[] { "vrp_clothes_f_civil01", "vrp_clothes_f_civil02", "vrp_clothes_f_civil03", "civil_f_premium" })
            katalog.Wstaw(Indeks.Zrodlo(Sciezki.Downloads(p), p, s => { }));
        var wynik = Porownanie.Znajdz(katalog, s => { });
        Porownaj(WczytajZloty("legacy4-duble.json"), NaZloty(wynik));
    }

    [Fact, Trait("Kategoria", "Wolny")]
    public void Gen9_studio_wardrobe_daje_zloty_wynik()
    {
        var dlc = Sciezki.Dlc("studio_wardrobe");
        if (dlc == null || !File.Exists(dlc)) { wyj.WriteLine("POMINIETY: brak studio_wardrobe\\dlc.rpf"); return; }
        var katalog = new Katalog();
        katalog.Wstaw(Indeks.Zrodlo(dlc, "studio_wardrobe", s => { }));
        var wynik = Porownanie.Znajdz(katalog, s => { });
        Porownaj(WczytajZloty("gen9-duble.json"), NaZloty(wynik));
    }
}
