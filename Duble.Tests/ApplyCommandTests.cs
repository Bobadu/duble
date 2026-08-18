using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// apply.preview, apply.run and history.list / get / undo over a made-up catalog with three sources and three
/// groups. The files really move, so this is also the test that the undo log describes what happened.
/// </summary>
public class ApplyCommandTests
{
    static TestApp Compared(string name)
    {
        var app = new TestApp(name);
        app.NewProject("A");
        SampleData.SevenGarmentsInThreeSources(app.Session, app.Temp);
        app.Session.Compare(default, null);
        app.Session.Save();
        return app;
    }

    [Fact]
    public async Task Preview_run_history_and_undo()
    {
        using var app = Compared("apply");

        // the plan: b (p2: a .ydd and two .ytd), f (p2: .ydd + .ytd), g (p3: .ydd + .ytd) = seven files
        var preview = await app.Call("apply.preview");
        Assert.Equal(3, preview.GetProperty("pozycje").GetInt32());
        Assert.Equal(7, preview.GetProperty("pliki").GetInt32());
        Assert.Equal(3, preview.GetProperty("lista").GetArrayLength());

        var bins = preview.GetProperty("kosze").EnumerateArray().Select(bin => bin.GetProperty("kosz").GetString()).ToList();
        Assert.Equal(2, bins.Count);
        Assert.Contains(Path.Combine(app.Temp, "_rejected", "p2"), bins);
        Assert.Contains(Path.Combine(app.Temp, "_rejected", "p3"), bins);
        // a null is left out of the JSON, so no "kosz" key means "beside the source"
        Assert.False(preview.TryGetProperty("kosz", out var bin) && bin.ValueKind != JsonValueKind.Null);

        // a bin folder of the user's own, saved in the project
        var chosenBin = Path.Combine(app.Temp, "bin");
        var run = await app.Call("apply.run", $"{{\"kosz\":{JsonSerializer.Serialize(chosenBin)},\"ustawKosz\":true}}");
        Assert.True(run.GetProperty("uruchomiono").GetBoolean());
        Assert.Equal(chosenBin, app.Session.Project.Settings.BinFolder);

        await app.WaitFor("apply.done");
        var done = app.EventData("apply.done");
        Assert.Equal(7, done.GetProperty("przeniesione").GetInt32());
        Assert.Equal(3, done.GetProperty("pozycje").GetInt32());
        Assert.False(done.GetProperty("przerwano").GetBoolean());

        Assert.True(File.Exists(Path.Combine(chosenBin, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.True(File.Exists(Path.Combine(chosenBin, "p3", "k.rpf", "feet_008_u.ydd")));
        Assert.False(File.Exists(Path.Combine(app.Temp, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.True(File.Exists(Path.Combine(app.Temp, "p1", "k.rpf", "jbib_001_u.ydd")));   // the winner is untouched
        Assert.True(app.Saw("history.changed"));
        Assert.True(app.Saw("compare.done"));

        // after re-indexing and comparing there is nothing left to reject, and there is one log
        var groups = await app.Call("groups.list", "{}");
        Assert.Equal(0, groups.GetProperty("podsumowanie").GetProperty("doOdrzucenia").GetProperty("pliki").GetInt32());
        Assert.Single(Directory.GetFiles(app.Session.Project.HistoryFolder, "*.json"));

        var history = await app.Call("history.list");
        var entry = Assert.Single(history.GetProperty("wpisy").EnumerateArray());
        Assert.Equal(7, entry.GetProperty("pliki").GetInt32());
        Assert.True(entry.GetProperty("moznaCofnac").GetBoolean());
        Assert.False(entry.TryGetProperty("cofnieto", out var undoneAt) && undoneAt.ValueKind != JsonValueKind.Null);

        var logFile = JsonSerializer.Serialize(entry.GetProperty("plik").GetString());
        var listed = (await app.Call("history.get", $"{{\"plik\":{logFile}}}")).GetProperty("wpis").GetProperty("lista");
        Assert.Equal(3, listed.GetArrayLength());

        var movedB = listed.EnumerateArray().First(item => item.GetProperty("nazwa").GetString().StartsWith("jbib_007"));
        Assert.Equal(3, movedB.GetProperty("pliki").GetArrayLength());
        Assert.True(movedB.GetProperty("pliki")[0].GetProperty("jest").GetBoolean());

        // undo b alone
        app.Sent.Clear();
        var undo = await app.Call("history.undo", $"{{\"plik\":{logFile},\"pozycje\":[\"{movedB.GetProperty("id").GetString()}\"]}}");
        Assert.True(undo.GetProperty("uruchomiono").GetBoolean());
        await app.WaitFor("undo.done");

        Assert.True(File.Exists(Path.Combine(app.Temp, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.False(File.Exists(Path.Combine(chosenBin, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.True(File.Exists(Path.Combine(chosenBin, "p2", "k.rpf", "feet_006_u.ydd")));   // f is still in the bin

        history = await app.Call("history.list");
        Assert.True(history.GetProperty("wpisy")[0].GetProperty("czesciowo").GetBoolean());
        Assert.True(history.GetProperty("wpisy")[0].GetProperty("moznaCofnac").GetBoolean());
        Assert.Contains(app.Session.Catalog.Garments, garment => garment.Id == "p2|k.rpf|jbib|7|u");   // p2 was indexed again

        // undo the rest
        app.Sent.Clear();
        await app.Call("history.undo", $"{{\"plik\":{logFile}}}");
        await app.WaitFor("undo.done");

        history = await app.Call("history.list");
        Assert.Equal(JsonValueKind.String, history.GetProperty("wpisy")[0].GetProperty("cofnieto").ValueKind);
        Assert.False(history.GetProperty("wpisy")[0].GetProperty("moznaCofnac").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(chosenBin, "p2")));   // the emptied bin folder was tidied away

        var nothingLeft = await app.Call("history.undo", $"{{\"plik\":{logFile}}}");
        Assert.False(nothingLeft.GetProperty("uruchomiono").GetBoolean());
    }

    [Fact]
    public async Task A_log_outside_the_projects_history_folder_is_not_found()
    {
        using var app = Compared("apply-outside");

        var error = await app.Failing("history.get", "{\"plik\":\"C:\\\\Windows\\\\win.ini\"}");

        Assert.Equal(BridgeErrors.NotFound, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_nothing_to_move_the_run_does_not_start()
    {
        using var app = Compared("apply-nothing");
        foreach (var live in app.Groups.All()) app.Session.Project.Decisions[live.Group.Id] = new Decision { Ignored = true };

        var answer = await app.Call("apply.run", "{}");

        Assert.False(answer.GetProperty("uruchomiono").GetBoolean());
        Assert.Equal(0, answer.GetProperty("plan").GetProperty("pliki").GetInt32());
        Assert.False(app.Jobs.Busy);
    }
}
