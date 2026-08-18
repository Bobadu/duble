using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
    static readonly IDuplicateFinder Finder =
        new ServiceCollection().AddDubleCore().BuildServiceProvider().GetRequiredService<IDuplicateFinder>();

    static readonly IServiceProvider CoreUslugi = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static IReadOnlyList<Garment> Indeksuj(string zrodlo, string nazwa, IndexOptions opcje = null)
        => CoreUslugi.GetRequiredService<IGarmentIndexer>().Index(zrodlo, nazwa, opcje ?? new IndexOptions()).Value.Garments;

    readonly ITestOutputHelper wyj;
    public ZlotyWzorzecTests(ITestOutputHelper wyj) { this.wyj = wyj; }

    // ---- ksztalt zlotego pliku (dzisiejszy ComparisonResult z tekstowymi powodami) ----
    public class ZPara { public string A { get; set; } public string B { get; set; } public string Werdykt { get; set; } public string Reason { get; set; } public double DistGeo { get; set; } public double PokrycieA { get; set; } public double PokrycieB { get; set; } public int WspolnychTekstur { get; set; } }
    public class ZGrupa { public List<string> Garments { get; set; } public string Werdykt { get; set; } public string Zwyciezca { get; set; } public string Reason { get; set; } public List<ZPara> Pairs { get; set; } = new(); public Dictionary<string, double> Scores { get; set; } = new(); public Dictionary<string, string> ScoreBreakdown { get; set; } = new(); }
    public class ZWynik { public List<ZGrupa> Groups { get; set; } = new(); public List<string> Podsumowanie { get; set; } = new(); }

    /// <summary>The names the golden files were written with, before verdicts became an enum.</summary>
    static string Napis(Verdict w) => w switch
    {
        Verdict.Duplicate => "DUPLIKAT",
        Verdict.Superset => "DUPLIKAT-NADZBIOR",
        Verdict.NeedsReview => "DO WGLADU",
        _ => "PRZEMALOWANIE",
    };

    /// <summary>The summary used to be a list of sentences; it is counts now, so the sentences are rebuilt here.</summary>
    static List<string> Podsumowanie(ComparisonResult w)
    {
        var lines = new List<string>();
        foreach (var verdict in new[] { Verdict.Duplicate, Verdict.Superset, Verdict.NeedsReview, Verdict.Retexture })
            if (w.Counts.TryGetValue(verdict, out var n) && n > 0) lines.Add($"{Napis(verdict)}: {n}");
        lines.Add($"pozycji do odrzucenia: {w.ProposedForRejection}");
        return lines;
    }

    static string KluczGrupy(IEnumerable<string> ids) => string.Join("\n", ids.OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Sprowadza wynik z Core do ksztaltu zlotego (w kolejnych zadaniach Reason/Rozpiska staja sie obiektami — wtedy TU formatujemy je po polsku).</summary>
    static ZWynik NaZloty(ComparisonResult w) => new ZWynik
    {
        Podsumowanie = Podsumowanie(w),
        Groups = w.Groups.Select(g => new ZGrupa
        {
            Garments = g.Members, Werdykt = Napis(g.Verdict), Zwyciezca = g.Winner,
            Reason = TekstPowodu(g),
            Pairs = g.Pairs.Select(p => new ZPara { A = p.A, B = p.B, Werdykt = Napis(p.Verdict), Reason = TekstPowodu(p), DistGeo = p.GeometryDistance, PokrycieA = p.CoverageA, PokrycieB = p.CoverageB, WspolnychTekstur = p.SharedTextures }).ToList(),
            Scores = g.Scores,
            ScoreBreakdown = g.ScoreBreakdown.ToDictionary(k => k.Key, k => TekstRozpiski(k.Value)),
        }).ToList()
    };
    // Reason/Rozpiska sa kodami+parametrami; wzorzec ma stare polskie napisy — formatter PL musi je odtworzyc co do znaku.
    static string TekstPowodu(DuplicateGroup g) => Texts.Reason(g.Reason, "pl");
    static string TekstPowodu(GarmentPair p) => Texts.Reason(p.Reason, "pl");
    static string TekstRozpiski(QualityScore r) => r.Text("pl");

    void Porownaj(ZWynik zloty, ZWynik nowy)
    {
        Assert.Equal(zloty.Podsumowanie, nowy.Podsumowanie);
        var zg = zloty.Groups.ToDictionary(g => KluczGrupy(g.Garments));
        var ng = nowy.Groups.ToDictionary(g => KluczGrupy(g.Garments));
        var brak = zg.Keys.Except(ng.Keys).ToList(); var nadmiar = ng.Keys.Except(zg.Keys).ToList();
        foreach (var b in brak) wyj.WriteLine("BRAK GRUPY: " + b.Replace("\n", " = "));
        foreach (var n in nadmiar) wyj.WriteLine("NOWA GRUPA: " + n.Replace("\n", " = "));
        Assert.Empty(brak); Assert.Empty(nadmiar);
        foreach (var k in zg.Keys)
        {
            var a = zg[k]; var b = ng[k];
            Assert.Equal(a.Werdykt, b.Werdykt); Assert.Equal(a.Zwyciezca, b.Zwyciezca); Assert.Equal(a.Reason, b.Reason);
            foreach (var id in a.Scores.Keys) Assert.Equal(a.Scores[id], b.Scores[id], 6);
            foreach (var id in a.ScoreBreakdown.Keys) Assert.Equal(a.ScoreBreakdown[id], b.ScoreBreakdown[id]);
            var pa = a.Pairs.OrderBy(p => p.A + p.B).ToList(); var pb = b.Pairs.OrderBy(p => p.A + p.B).ToList();
            Assert.Equal(pa.Count, pb.Count);
            for (int i = 0; i < pa.Count; i++)
            {
                Assert.Equal(pa[i].A, pb[i].A); Assert.Equal(pa[i].B, pb[i].B); Assert.Equal(pa[i].Werdykt, pb[i].Werdykt);
                Assert.Equal(pa[i].Reason, pb[i].Reason); Assert.Equal(pa[i].DistGeo, pb[i].DistGeo, 9);
                Assert.Equal(pa[i].PokrycieA, pb[i].PokrycieA, 9); Assert.Equal(pa[i].PokrycieB, pb[i].PokrycieB, 9);
                Assert.Equal(pa[i].WspolnychTekstur, pb[i].WspolnychTekstur);
            }
        }
    }

    /// <summary>
    /// Zloty plik moze byc w STARYM ksztalcie (Reason/Rozpiska = polski tekst; legacy4, zapisany CLI sprzed refaktoru)
    /// albo w NOWYM (Reason = {Kod,P}, Rozpiska = QualityScore; gen9, zapisany po Zadaniu 6, bo stare CLI nie czytalo .rpf).
    /// Oba sprowadzamy do tekstu PL — dzieki temu jedna procedura porownania obsluguje oba.
    /// </summary>
    static ZWynik WczytajZloty(string plik)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Sciezki.Golden(plik)));
        var root = doc.RootElement;
        string Text(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.String) return e.GetString();
            if (e.ValueKind == JsonValueKind.Null || e.ValueKind == JsonValueKind.Undefined) return null;
            // the golden files hold the shapes Duble wrote before the rewrite, with Polish property names —
            // they are never regenerated, so they are mapped onto today's types here
            if (e.TryGetProperty("Kod", out var kod))
            {
                var powod = new Reason { Code = kod.GetString() };
                if (e.TryGetProperty("P", out var parametry) && parametry.ValueKind == JsonValueKind.Object)
                    foreach (var kv in parametry.EnumerateObject()) powod.Parameters[kv.Name] = kv.Value.GetString();
                return Texts.Reason(powod, "pl");
            }
            if (e.TryGetProperty("Razem", out _))
            {
                double D(string n) => e.TryGetProperty(n, out var v) ? v.GetDouble() : 0;
                int I(string n) => e.TryGetProperty(n, out var v) ? v.GetInt32() : 0;
                bool B(string n) => e.TryGetProperty(n, out var v) && v.GetBoolean();
                return new QualityScore
                {
                    Total = D("Razem"), Resolution = D("Rozdz"), Mipmaps = D("Mipy"), Variants = D("Warianty"),
                    Format = D("Format"), Lod = D("Lod"), ResolutionPx = D("RozdzPx"), MipmapShare = D("UdzialMipow"),
                    VariantCount = I("LiczbaWariantow"), WrongFormatCount = I("ZlyFormat"), LodLevels = I("Lody"),
                    NoTextures = B("BrakTekstur"),
                }.Text("pl");
            }
            throw new InvalidDataException("nieznany ksztalt: " + e.GetRawText());
        }
        JsonElement Pole(JsonElement e, string nazwa) => e.TryGetProperty(nazwa, out var v) ? v : default;
        var wynik = new ZWynik { Groups = new(), Podsumowanie = Pole(root, "Podsumowanie").EnumerateArray().Select(x => x.GetString()).ToList() };
        foreach (var g in Pole(root, "Grupy").EnumerateArray())
        {
            var zg = new ZGrupa
            {
                Garments = Pole(g, "Pozycje").EnumerateArray().Select(x => x.GetString()).ToList(),
                Werdykt = Pole(g, "Werdykt").GetString(), Zwyciezca = Pole(g, "Zwyciezca").GetString(),
                Reason = Text(Pole(g, "Powod")),
            };
            foreach (var p in Pole(g, "Pary").EnumerateArray())
                zg.Pairs.Add(new ZPara
                {
                    A = Pole(p, "A").GetString(), B = Pole(p, "B").GetString(), Werdykt = Pole(p, "Werdykt").GetString(),
                    Reason = Text(Pole(p, "Powod")), DistGeo = Pole(p, "DistGeo").GetDouble(),
                    PokrycieA = Pole(p, "PokrycieA").GetDouble(), PokrycieB = Pole(p, "PokrycieB").GetDouble(),
                    WspolnychTekstur = Pole(p, "WspolnychTekstur").GetInt32(),
                });
            foreach (var kv in Pole(g, "Punkty").EnumerateObject()) zg.Scores[kv.Name] = kv.Value.GetDouble();
            foreach (var kv in Pole(g, "Rozpiska").EnumerateObject()) zg.ScoreBreakdown[kv.Name] = Text(kv.Value);
            wynik.Groups.Add(zg);
        }
        return wynik;
    }

    [Fact, Trait("Kategoria", "Wolny")]
    public void Legacy4_z_downloads_daje_zloty_wynik()
    {
        if (!Sciezki.SaLegacy4) { wyj.WriteLine("POMINIETY: brak downloads\\vrp_clothes_f_civil01 itd."); return; }
        var katalog = new Catalog();
        foreach (var p in new[] { "vrp_clothes_f_civil01", "vrp_clothes_f_civil02", "vrp_clothes_f_civil03", "civil_f_premium" })
            katalog.Upsert(Indeksuj(Sciezki.Downloads(p), p));
        var wynik = Finder.Find(katalog);
        Porownaj(WczytajZloty("legacy4-duble.json"), NaZloty(wynik));
    }

    [Fact, Trait("Kategoria", "Wolny")]
    public void Gen9_studio_wardrobe_daje_zloty_wynik()
    {
        var dlc = Sciezki.Dlc("studio_wardrobe");
        if (dlc == null || !File.Exists(dlc)) { wyj.WriteLine("POMINIETY: brak studio_wardrobe\\dlc.rpf"); return; }
        var katalog = new Catalog();
        katalog.Upsert(Indeksuj(dlc, "studio_wardrobe"));
        var wynik = Finder.Find(katalog);
        Porownaj(WczytajZloty("gen9-duble.json"), NaZloty(wynik));
    }
}
