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
/// The golden master: comparison results written by the CLI BEFORE the rewrite (Duble.Tests\golden\). After
/// any change to the engine, comparing the same sources has to give the same thing back — the same groups,
/// verdicts, winners, scores, and, in Polish, the same sentences character for character.
///
/// The files are never regenerated. They hold the shapes Duble wrote at the time, with Polish property names,
/// and are mapped onto today's types here.
/// </summary>
public class GoldenMasterTests
{
    static readonly IServiceProvider Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static readonly IDuplicateFinder Finder = Services.GetRequiredService<IDuplicateFinder>();

    /// <summary>
    /// Indexes a source, refusing to compare a catalog that is missing anything.
    ///
    /// This test used to fail once in a handful of runs with a group's winner swapped for its neighbour, which
    /// reads as "the engine changed" and is not: a clothing file had failed to read, its garment came out a
    /// texture short, and a garment short of a texture scores lower. Saying so here turns a mystery into a
    /// sentence naming the file.
    /// </summary>
    static IReadOnlyList<Garment> Index(string source, string name)
    {
        var report = Services.GetRequiredService<IGarmentIndexer>().Index(source, name, new IndexOptions()).Value;
        Assert.Empty(report.UnreadableFiles);
        return report.Garments;
    }

    readonly ITestOutputHelper output;

    public GoldenMasterTests(ITestOutputHelper output) => this.output = output;

    // ---- the shape of a golden file, reduced to plain text on both sides ----

    public sealed class GoldenPair
    {
        public string A { get; set; }
        public string B { get; set; }
        public string Verdict { get; set; }
        public string Reason { get; set; }
        public double GeometryDistance { get; set; }
        public double CoverageA { get; set; }
        public double CoverageB { get; set; }
        public int SharedTextures { get; set; }
    }

    public sealed class GoldenGroup
    {
        public List<string> Members { get; set; } = new();
        public string Verdict { get; set; }
        public string Winner { get; set; }
        public string Reason { get; set; }
        public List<GoldenPair> Pairs { get; set; } = new();
        public Dictionary<string, double> Scores { get; set; } = new();
        public Dictionary<string, string> ScoreBreakdown { get; set; } = new();
    }

    public sealed class GoldenResult
    {
        public List<GoldenGroup> Groups { get; set; } = new();
        public List<string> Summary { get; set; } = new();
    }

    /// <summary>The names the golden files were written with, before verdicts became an enum.</summary>
    static string VerdictName(Verdict verdict) => verdict switch
    {
        Verdict.Duplicate => "DUPLIKAT",
        Verdict.Superset => "DUPLIKAT-NADZBIOR",
        Verdict.NeedsReview => "DO WGLADU",
        _ => "PRZEMALOWANIE",
    };

    /// <summary>
    /// Reason parameters the golden files were written with, before they were renamed into English. Without
    /// this, the templates would find nothing to substitute and leave "{winner}" in the sentence.
    /// </summary>
    static readonly Dictionary<string, string> RenamedParameters = new()
    {
        ["zw"] = "winner",
        ["przegrani"] = "losers",
    };

    /// <summary>The summary used to be a list of sentences; it is counts now, so the sentences are rebuilt here.</summary>
    static List<string> Summary(ComparisonResult result)
    {
        var lines = new List<string>();
        foreach (var verdict in Verdicts.All)
            if (result.Counts.TryGetValue(verdict, out var count) && count > 0)
                lines.Add($"{VerdictName(verdict)}: {count}");
        lines.Add($"pozycji do odrzucenia: {result.ProposedForRejection}");
        return lines;
    }

    static string GroupKey(IEnumerable<string> members) => string.Join("\n", members.OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>
    /// A pair with its two sides in a fixed order, the coverages following them.
    ///
    /// Which garment a pair calls A carries no claim about the world — "a covers 95% of b" and "b is covered
    /// 95% by a" are one fact. It used to follow the catalog's order, and that order was not deterministic, so
    /// the golden files froze one of two possible spellings of the same finding. Comparing both sides in a
    /// canonical orientation asserts the finding instead of the spelling.
    /// </summary>
    static GoldenPair Canonical(GoldenPair pair)
        => string.CompareOrdinal(pair.A, pair.B) <= 0
            ? pair
            : new GoldenPair
            {
                A = pair.B,
                B = pair.A,
                Verdict = pair.Verdict,
                Reason = pair.Reason,
                GeometryDistance = pair.GeometryDistance,
                CoverageA = pair.CoverageB,
                CoverageB = pair.CoverageA,
                SharedTextures = pair.SharedTextures,
            };

    /// <summary>Today's result reduced to the golden shape: reasons and breakdowns as their Polish sentences.</summary>
    static GoldenResult ToGolden(ComparisonResult result) => new()
    {
        Summary = Summary(result),
        Groups = result.Groups.Select(group => new GoldenGroup
        {
            Members = group.Members,
            Verdict = VerdictName(group.Verdict),
            Winner = group.Winner,
            Reason = Texts.Reason(group.Reason, "pl"),
            Pairs = group.Pairs.Select(pair => new GoldenPair
            {
                A = pair.A,
                B = pair.B,
                Verdict = VerdictName(pair.Verdict),
                Reason = Texts.Reason(pair.Reason, "pl"),
                GeometryDistance = pair.GeometryDistance,
                CoverageA = pair.CoverageA,
                CoverageB = pair.CoverageB,
                SharedTextures = pair.SharedTextures,
            }).ToList(),
            Scores = group.Scores,
            ScoreBreakdown = group.ScoreBreakdown.ToDictionary(entry => entry.Key, entry => entry.Value.Text("pl")),
        }).ToList(),
    };

    void Compare(GoldenResult golden, GoldenResult current)
    {
        Assert.Equal(golden.Summary, current.Summary);

        var expected = golden.Groups.ToDictionary(group => GroupKey(group.Members));
        var actual = current.Groups.ToDictionary(group => GroupKey(group.Members));

        var missing = expected.Keys.Except(actual.Keys).ToList();
        var extra = actual.Keys.Except(expected.Keys).ToList();
        foreach (var key in missing) output.WriteLine("GROUP GONE: " + key.Replace("\n", " = "));
        foreach (var key in extra) output.WriteLine("GROUP NEW: " + key.Replace("\n", " = "));
        Assert.Empty(missing);
        Assert.Empty(extra);

        foreach (var key in expected.Keys)
        {
            var a = expected[key];
            var b = actual[key];
            Assert.Equal(a.Verdict, b.Verdict);
            Assert.Equal(a.Reason, b.Reason);
            foreach (var id in a.Scores.Keys) Assert.Equal(a.Scores[id], b.Scores[id], 6);
            foreach (var id in a.ScoreBreakdown.Keys) Assert.Equal(a.ScoreBreakdown[id], b.ScoreBreakdown[id]);

            // The winner is a DECISION where something gets rejected, and there it is compared. On a retexture
            // or a needs-review group nothing is ever proposed for rejection, so the winner is a label, and the
            // golden files recorded one that used to come out of the catalog's order rather than the garments —
            // it moved when that order became deterministic. All that can be asked of it is that it is a member.
            if (a.Verdict is "DUPLIKAT" or "DUPLIKAT-NADZBIOR") Assert.Equal(a.Winner, b.Winner);
            else Assert.Contains(b.Winner, b.Members);

            var pairsA = a.Pairs.Select(Canonical).OrderBy(p => p.A + p.B).ToList();
            var pairsB = b.Pairs.Select(Canonical).OrderBy(p => p.A + p.B).ToList();
            Assert.Equal(pairsA.Count, pairsB.Count);
            for (int i = 0; i < pairsA.Count; i++)
            {
                Assert.Equal(pairsA[i].A, pairsB[i].A);
                Assert.Equal(pairsA[i].B, pairsB[i].B);
                Assert.Equal(pairsA[i].Verdict, pairsB[i].Verdict);
                Assert.Equal(pairsA[i].Reason, pairsB[i].Reason);
                Assert.Equal(pairsA[i].GeometryDistance, pairsB[i].GeometryDistance, 9);
                Assert.Equal(pairsA[i].CoverageA, pairsB[i].CoverageA, 9);
                Assert.Equal(pairsA[i].CoverageB, pairsB[i].CoverageB, 9);
                Assert.Equal(pairsA[i].SharedTextures, pairsB[i].SharedTextures);
            }
        }
    }

    /// <summary>
    /// A golden file can be in the OLD shape (reason and breakdown already Polish text; legacy4, written by the
    /// CLI before the rewrite) or the NEWER one (reason as {Kod,P}, breakdown as a score; gen9, written once the
    /// CLI could read .rpf archives). Both are reduced to Polish text, so one comparison handles both.
    /// </summary>
    static GoldenResult LoadGolden(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Sciezki.Golden(fileName)));
        var root = document.RootElement;

        string Text(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String) return element.GetString();
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined) return null;

            if (element.TryGetProperty("Kod", out var code))
            {
                var reason = new Reason { Code = code.GetString() };
                if (element.TryGetProperty("P", out var parameters) && parameters.ValueKind == JsonValueKind.Object)
                    foreach (var parameter in parameters.EnumerateObject())
                    {
                        var name = RenamedParameters.TryGetValue(parameter.Name, out var renamed) ? renamed : parameter.Name;
                        reason.Parameters[name] = parameter.Value.GetString();
                    }
                return Texts.Reason(reason, "pl");
            }

            if (element.TryGetProperty("Razem", out _))
            {
                double Number(string name) => element.TryGetProperty(name, out var v) ? v.GetDouble() : 0;
                int Count(string name) => element.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
                bool Flag(string name) => element.TryGetProperty(name, out var v) && v.GetBoolean();
                return new QualityScore
                {
                    Total = Number("Razem"), Resolution = Number("Rozdz"), Mipmaps = Number("Mipy"),
                    Variants = Number("Warianty"), Format = Number("Format"), Lod = Number("Lod"),
                    ResolutionPx = Number("RozdzPx"), MipmapShare = Number("UdzialMipow"),
                    VariantCount = Count("LiczbaWariantow"), WrongFormatCount = Count("ZlyFormat"),
                    LodLevels = Count("Lody"), NoTextures = Flag("BrakTekstur"),
                }.Text("pl");
            }

            throw new InvalidDataException("unknown shape: " + element.GetRawText());
        }

        JsonElement Property(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value : default;

        var result = new GoldenResult
        {
            Summary = Property(root, "Podsumowanie").EnumerateArray().Select(x => x.GetString()).ToList(),
        };

        foreach (var group in Property(root, "Grupy").EnumerateArray())
        {
            var golden = new GoldenGroup
            {
                Members = Property(group, "Pozycje").EnumerateArray().Select(x => x.GetString()).ToList(),
                Verdict = Property(group, "Werdykt").GetString(),
                Winner = Property(group, "Zwyciezca").GetString(),
                Reason = Text(Property(group, "Powod")),
            };
            foreach (var pair in Property(group, "Pary").EnumerateArray())
                golden.Pairs.Add(new GoldenPair
                {
                    A = Property(pair, "A").GetString(),
                    B = Property(pair, "B").GetString(),
                    Verdict = Property(pair, "Werdykt").GetString(),
                    Reason = Text(Property(pair, "Powod")),
                    GeometryDistance = Property(pair, "DistGeo").GetDouble(),
                    CoverageA = Property(pair, "PokrycieA").GetDouble(),
                    CoverageB = Property(pair, "PokrycieB").GetDouble(),
                    SharedTextures = Property(pair, "WspolnychTekstur").GetInt32(),
                });
            foreach (var score in Property(group, "Punkty").EnumerateObject()) golden.Scores[score.Name] = score.Value.GetDouble();
            foreach (var breakdown in Property(group, "Rozpiska").EnumerateObject()) golden.ScoreBreakdown[breakdown.Name] = Text(breakdown.Value);
            result.Groups.Add(golden);
        }

        return result;
    }

    [Fact, Trait("Speed", "Slow")]
    public void Four_legacy_packs_still_compare_the_same_way()
    {
        if (!Sciezki.SaLegacy4) { output.WriteLine("SKIPPED: no downloads\\vrp_clothes_f_civil01 and the rest"); return; }

        var catalog = new Catalog();
        foreach (var pack in new[] { "vrp_clothes_f_civil01", "vrp_clothes_f_civil02", "vrp_clothes_f_civil03", "civil_f_premium" })
            catalog.Upsert(Index(Sciezki.Downloads(pack), pack));

        Compare(LoadGolden("legacy4-duble.json"), ToGolden(Finder.Find(catalog)));
    }

    [Fact, Trait("Speed", "Slow")]
    public void The_enhanced_studio_wardrobe_still_compares_the_same_way()
    {
        var dlc = Sciezki.Dlc("studio_wardrobe");
        if (dlc == null || !File.Exists(dlc)) { output.WriteLine("SKIPPED: no studio_wardrobe\\dlc.rpf"); return; }

        var catalog = new Catalog();
        catalog.Upsert(Index(dlc, "studio_wardrobe"));

        Compare(LoadGolden("gen9-duble.json"), ToGolden(Finder.Find(catalog)));
    }
}
