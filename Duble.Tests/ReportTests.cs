using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The HTML report and the CSV export over a synthetic catalog: the language the user picked, the decisions
/// they made, and a separator Excel will not choke on.
/// </summary>
public class ReportTests
{
    static readonly IServiceProvider Services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
    static readonly IDuplicateFinder Finder = Services.GetRequiredService<IDuplicateFinder>();
    static readonly IHtmlReportBuilder Reports = Services.GetRequiredService<IHtmlReportBuilder>();
    static readonly ICsvExporter Csv = Services.GetRequiredService<ICsvExporter>();

    static (Catalog Catalog, ComparisonResult Result, string Directory) World()
    {
        var directory = TestPaths.Temp("report");
        var catalog = new Catalog();
        catalog.Upsert(SampleData.SevenGarments(directory));
        return (catalog, Finder.Find(catalog), directory);
    }

    /// <summary>Wraps decisions taken per group into the callback the report and the export both take.</summary>
    static Func<DuplicateGroup, Resolution> Resolving(Dictionary<string, Decision> decisions)
    {
        var service = new ResolutionService();
        return group => service.Resolve(group, decisions.TryGetValue(group.Id, out var decision) ? decision : null);
    }

    [Fact]
    public void The_html_report_speaks_the_language_it_was_asked_for_and_shows_the_users_decisions()
    {
        var (catalog, result, directory) = World();
        try
        {
            // the user ignored the three-member group and overruled the winner of one duplicate pair
            var ignored = result.Groups.First(g => g.Members.Count == 3);
            var overruled = result.Groups.First(g => g.Verdict == Verdict.Duplicate && g.Members.Count == 2);
            var resolve = Resolving(new Dictionary<string, Decision>
            {
                [ignored.Id] = new Decision { Ignored = true, Note = "different boots" },
                [overruled.Id] = new Decision { Winner = overruled.Members[1], Rejected = { overruled.Members[0] } },
            });

            var path = Path.Combine(directory, "report.html");
            var log = new List<string>();

            Reports.Build(catalog, result, path, new ReportOptions
            {
                Language = "en", Title = "My project", Resolve = resolve, Log = log.Add,
            });

            var html = File.ReadAllText(path);
            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains("Duble — My project", html);
            Assert.Contains(">STAYS<", html);
            Assert.Contains(">TO REJECT<", html);
            Assert.DoesNotContain("ZOSTAJE", html);
            Assert.DoesNotContain("DO ODRZUCENIA", html);
            Assert.Contains("NOT A DUPLICATE", html);
            Assert.Contains("different boots", html);
            Assert.Contains("YOUR DECISION", html);
            Assert.Contains("Textures side by side", html);
            Assert.Contains("Nothing was deleted", html);
            // only a is rejected: b stays by the user's decision, efg is ignored, cd is a retexture
            Assert.Contains("to reject <b>1</b>", html);
            Assert.DoesNotContain("[report.", html);   // every key found a translation

            // in Polish, with the comparison's own proposals: a stays, b is rejected, f and g are rejected
            var polish = Path.Combine(directory, "report-pl.html");
            Reports.Build(catalog, result, polish);
            var pl = File.ReadAllText(polish);
            Assert.Contains("<html lang=\"pl\">", pl);
            Assert.Contains(">ZOSTAJE<", pl);
            Assert.Contains("do odrzucenia <b>3</b>", pl);
            Assert.DoesNotContain("[report.", pl);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void The_report_carries_its_own_stylesheet_and_script()
    {
        var (catalog, result, directory) = World();
        try
        {
            var path = Path.Combine(directory, "report.html");
            Reports.Build(catalog, result, path);
            var html = File.ReadAllText(path);

            Assert.Contains("--duplicate:", html);                 // report.css, inlined
            Assert.Contains("document.getElementById('search')", html);   // report.js, inlined
            Assert.DoesNotContain("<link", html);                  // nothing is fetched from anywhere
            Assert.DoesNotContain("src=\"http", html);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void The_csv_has_one_row_per_member_and_a_separator_that_follows_the_language()
    {
        var (catalog, result, directory) = World();
        try
        {
            var ignored = result.Groups.First(g => g.Members.Count == 3);
            var resolve = Resolving(new Dictionary<string, Decision>
            {
                [ignored.Id] = new Decision { Ignored = true, Note = "inne; buty" },
            });
            var csv = Csv.Export(catalog, result, resolve, "pl");

            Assert.StartsWith("\uFEFF", csv);
            var lines = csv.TrimEnd('\r', '\n').Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            Assert.Equal(1 + 7, lines.Count);                       // heading + 7 members (2 + 2 + 3)
            Assert.StartsWith("\uFEFFgrupa;werdykt;powód;pozycja;", lines[0]);
            Assert.Contains(lines, l => l.Contains(";zignorowana;\"inne; buty\";"));   // a semicolon in a note gets quoted
            Assert.Contains(lines, l => l.Contains(";zostaje;"));
            Assert.Contains(lines, l => l.Contains(";odrzucona;"));
            Assert.Contains(lines, l => l.Contains(";bez zmian;"));                    // the retexture

            var english = Csv.Export(catalog, result, null, "en");
            Assert.StartsWith("\uFEFFgroup,verdict,reason,item,", english);
            Assert.Contains(",stays,", english);
            Assert.Contains(",rejected,", english);
        }
        finally { Directory.Delete(directory, true); }
    }
}
