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
        Assert.Equal(3, preview.GetProperty("garments").GetInt32());
        Assert.Equal(7, preview.GetProperty("files").GetInt32());
        Assert.Equal(3, preview.GetProperty("list").GetArrayLength());

        var bins = preview.GetProperty("bins").EnumerateArray().Select(bin => bin.GetProperty("bin").GetString()).ToList();
        Assert.Equal(2, bins.Count);
        Assert.Contains(Path.Combine(app.Temp, "_rejected", "p2"), bins);
        Assert.Contains(Path.Combine(app.Temp, "_rejected", "p3"), bins);
        // a null is left out of the JSON, so no "bin" key means "beside the source"
        Assert.False(preview.TryGetProperty("bin", out var bin) && bin.ValueKind != JsonValueKind.Null);

        // a bin folder of the user's own, saved in the project
        var chosenBin = Path.Combine(app.Temp, "bin");
        var run = await app.Call("apply.run", $"{{\"bin\":{JsonSerializer.Serialize(chosenBin)},\"setBin\":true}}");
        Assert.True(run.GetProperty("started").GetBoolean());
        Assert.Equal(chosenBin, app.Session.Project.Settings.BinFolder);

        await app.WaitFor("apply.done");
        var done = app.EventData("apply.done");
        Assert.Equal(7, done.GetProperty("moved").GetInt32());
        Assert.Equal(3, done.GetProperty("garments").GetInt32());
        Assert.False(done.GetProperty("aborted").GetBoolean());

        Assert.True(File.Exists(Path.Combine(chosenBin, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.True(File.Exists(Path.Combine(chosenBin, "p3", "k.rpf", "feet_008_u.ydd")));
        Assert.False(File.Exists(Path.Combine(app.Temp, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.True(File.Exists(Path.Combine(app.Temp, "p1", "k.rpf", "jbib_001_u.ydd")));   // the winner is untouched
        Assert.True(app.Saw("history.changed"));
        Assert.True(app.Saw("compare.done"));

        // after re-indexing and comparing there is nothing left to reject, and there is one log
        var groups = await app.Call("groups.list", "{}");
        Assert.Equal(0, groups.GetProperty("summary").GetProperty("toReject").GetProperty("files").GetInt32());
        Assert.Single(Directory.GetFiles(app.Session.Project.HistoryFolder, "*.json"));

        var history = await app.Call("history.list");
        var entry = Assert.Single(history.GetProperty("entries").EnumerateArray());
        Assert.Equal(7, entry.GetProperty("files").GetInt32());
        Assert.True(entry.GetProperty("canUndo").GetBoolean());
        Assert.False(entry.TryGetProperty("undoneAt", out var undoneAt) && undoneAt.ValueKind != JsonValueKind.Null);

        var logFile = JsonSerializer.Serialize(entry.GetProperty("file").GetString());
        var listed = (await app.Call("history.get", $"{{\"file\":{logFile}}}")).GetProperty("entry").GetProperty("list");
        Assert.Equal(3, listed.GetArrayLength());

        var movedB = listed.EnumerateArray().First(item => item.GetProperty("name").GetString().StartsWith("jbib_007"));
        Assert.Equal(3, movedB.GetProperty("files").GetArrayLength());
        Assert.True(movedB.GetProperty("files")[0].GetProperty("exists").GetBoolean());

        // undo b alone
        app.Sent.Clear();
        var undo = await app.Call("history.undo", $"{{\"file\":{logFile},\"garments\":[\"{movedB.GetProperty("id").GetString()}\"]}}");
        Assert.True(undo.GetProperty("started").GetBoolean());
        await app.WaitFor("undo.done");

        Assert.True(File.Exists(Path.Combine(app.Temp, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.False(File.Exists(Path.Combine(chosenBin, "p2", "k.rpf", "jbib_007_u.ydd")));
        Assert.True(File.Exists(Path.Combine(chosenBin, "p2", "k.rpf", "feet_006_u.ydd")));   // f is still in the bin

        history = await app.Call("history.list");
        Assert.True(history.GetProperty("entries")[0].GetProperty("partlyUndone").GetBoolean());
        Assert.True(history.GetProperty("entries")[0].GetProperty("canUndo").GetBoolean());
        Assert.Contains(app.Session.Catalog.Garments, garment => garment.Id == "p2|k.rpf|jbib|7|u");   // p2 was indexed again

        // undo the rest
        app.Sent.Clear();
        await app.Call("history.undo", $"{{\"file\":{logFile}}}");
        await app.WaitFor("undo.done");

        history = await app.Call("history.list");
        Assert.Equal(JsonValueKind.String, history.GetProperty("entries")[0].GetProperty("undoneAt").ValueKind);
        Assert.False(history.GetProperty("entries")[0].GetProperty("canUndo").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(chosenBin, "p2")));   // the emptied bin folder was tidied away

        var nothingLeft = await app.Call("history.undo", $"{{\"file\":{logFile}}}");
        Assert.False(nothingLeft.GetProperty("started").GetBoolean());
    }

    [Fact]
    public async Task A_log_outside_the_projects_history_folder_is_not_found()
    {
        using var app = Compared("apply-outside");

        var error = await app.Failing("history.get", "{\"file\":\"C:\\\\Windows\\\\win.ini\"}");

        Assert.Equal(BridgeErrors.NotFound, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_nothing_to_move_the_run_does_not_start()
    {
        using var app = Compared("apply-nothing");
        foreach (var live in app.Groups.All()) app.Session.Project.Decisions[live.Group.Id] = new Decision { Ignored = true };

        var answer = await app.Call("apply.run", "{}");

        Assert.False(answer.GetProperty("started").GetBoolean());
        Assert.Equal(0, answer.GetProperty("plan").GetProperty("files").GetInt32());
        Assert.False(app.Jobs.Busy);
    }
}
