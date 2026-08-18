using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class SessionTests
{
    [Fact]
    public void A_new_project_writes_its_file_and_cache_and_opening_reads_it_back()
    {
        using var app = new TestApp("session");
        var session = app.Session;

        session.New("My studio", Path.Combine(app.Temp, "My studio.duble"));

        Assert.True(File.Exists(Path.Combine(app.Temp, "My studio.duble")));
        Assert.True(Directory.Exists(Path.Combine(app.Temp, "My studio.duble.cache")));
        Assert.True(session.IsOpen);
        Assert.Empty(session.Catalog.Garments);

        session.Close();
        Assert.False(session.IsOpen);

        session.Open(Path.Combine(app.Temp, "My studio.duble"));
        Assert.Equal("My studio", session.Project.Name);

        var summary = JsonSerializer.Serialize(session.Summary(), Bridge.Json);
        Assert.Contains("\"sources\":0", summary);
        Assert.Contains("\"garments\":0", summary);
    }

    [Fact]
    public void Opening_a_project_that_is_not_there_says_so()
    {
        using var app = new TestApp("session");

        Assert.Throws<FileNotFoundException>(() => app.Session.Open(Path.Combine(app.Temp, "not-there.duble")));
    }

    [Fact]
    public async Task The_project_commands_work_over_the_bridge()
    {
        using var app = new TestApp("session-bridge");
        var folder = JsonSerializer.Serialize(app.Temp);

        var created = await app.Call("project.new", $"{{\"name\":\"Test: A/B\",\"folder\":{folder}}}");

        Assert.Equal("Test: A/B", created.GetProperty("project").GetProperty("name").GetString());
        Assert.True(File.Exists(Path.Combine(app.Temp, "Test A B.duble")));   // characters Windows forbids become spaces
        Assert.Single(app.Settings.Recent);
        Assert.True(app.Saw("project.opened"));

        var again = await app.Failing("project.new", $"{{\"name\":\"Test: A/B\",\"folder\":{folder}}}");
        Assert.Equal(BridgeErrors.Io, again.GetProperty("code").GetString());   // the file is already there

        var recent = await app.Call("project.recent");
        Assert.True(recent.GetProperty("recent")[0].GetProperty("exists").GetBoolean());

        var missing = await app.Failing("project.open", "{\"path\":\"C:\\\\no\\\\such.duble\"}");
        Assert.Equal(BridgeErrors.NotFound, missing.GetProperty("code").GetString());

        var current = await app.Call("project.get");
        Assert.Equal("Test: A/B", current.GetProperty("project").GetProperty("name").GetString());

        await app.Call("project.close");
        var closed = await app.Call("project.get");
        // a null is left out of the JSON (WhenWritingNull), so a missing key means no project
        Assert.False(closed.TryGetProperty("project", out var project) && project.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Forgetting_a_project_takes_it_off_the_start_screen()
    {
        using var app = new TestApp("session-forget");
        var folder = JsonSerializer.Serialize(app.Temp);
        await app.Call("project.new", $"{{\"name\":\"Gone\",\"folder\":{folder}}}");

        var file = JsonSerializer.Serialize(Path.Combine(app.Temp, "Gone.duble"));
        await app.Call("project.forget", $"{{\"path\":{file}}}");

        Assert.Empty((await app.Call("project.recent")).GetProperty("recent").EnumerateArray());
    }

    [Fact]
    public async Task Commands_that_need_a_project_say_so_when_none_is_open()
    {
        using var app = new TestApp("session-empty");

        var error = await app.Failing("catalog.list", "{}");

        Assert.Equal(BridgeErrors.NoProject, error.GetProperty("code").GetString());
    }
}
