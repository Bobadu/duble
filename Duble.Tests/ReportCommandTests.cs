using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>report.exportHtml and report.exportCsv over a made-up catalog: the language of the interface and
/// the decisions of the project both have to reach the file.</summary>
public class ReportCommandTests
{
    [Fact]
    public async Task Exporting_html_and_csv()
    {
        using var app = new TestApp("export", new Settings { Language = "en" });
        app.NewProject("My project");
        SampleData.SevenGarmentsInThreeSources(app.Session, app.Temp);

        // nothing has been compared yet
        var tooEarly = await app.Failing("report.exportCsv", "{\"path\":\"x\"}");
        Assert.Equal(BridgeErrors.NotFound, tooEarly.GetProperty("code").GetString());

        app.Session.Compare(default, null);
        app.Session.Save();

        // one decision to carry into both files: the group of three is not a duplicate
        var boots = app.Groups.All().First(live => live.Group.Members.Count == 3).Group;
        await app.Call("groups.decide", $"{{\"id\":\"{boots.Id}\",\"ignored\":true,\"note\":\"other boots\"}}");

        var csvFile = Path.Combine(app.Temp, "out", "groups.csv");
        await app.Call("report.exportCsv", $"{{\"path\":{JsonSerializer.Serialize(csvFile)}}}");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(csvFile).Take(3).ToArray());   // BOM, for Excel
        var csv = File.ReadAllText(csvFile);                                                            // which ReadAllText drops
        Assert.StartsWith("group,verdict,", csv);                                                       // English interface, English headings
        Assert.Contains(",ignored,other boots,", csv);
        Assert.True(app.Saw("report.done"));

        var htmlFile = Path.Combine(app.Temp, "out", "report.html");
        var started = await app.Call("report.exportHtml", $"{{\"path\":{JsonSerializer.Serialize(htmlFile)}}}");
        Assert.True(started.GetProperty("started").GetBoolean());
        await app.WaitFor("report.done");

        var html = File.ReadAllText(htmlFile);
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("Duble — My project", html);
        Assert.Contains("NOT A DUPLICATE", html);
        Assert.Contains("other boots", html);
    }

    [Fact]
    public async Task A_cancelled_save_dialog_is_not_a_failure()
    {
        using var app = new TestApp("export-cancel");
        app.NewProject("P");
        SampleData.SevenGarmentsInThreeSources(app.Session, app.Temp);
        app.Session.Compare(default, null);
        app.Dialogs.SavedFile = null;   // the user closed the dialog

        var answer = await app.Call("report.exportCsv", "{}");

        Assert.True(answer.GetProperty("cancelled").GetBoolean());
    }
}
