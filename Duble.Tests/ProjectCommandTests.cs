using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Duble.Tests;

/// <summary>The shape the start screen reads: project.new and project.recent.</summary>
public class ProjectCommandTests
{
    /// <summary>
    /// The card of a recent project shows `o.name`. The field came from the shorthand `new { o.Name, ... }`,
    /// so renaming the property in C# renamed the field to `name` and the name vanished from the start screen.
    /// </summary>
    [Fact]
    public async Task A_recent_project_carries_its_name_path_and_date()
    {
        using var app = new TestApp("projects");

        var created = await app.Call("project.new",
            $"{{\"name\":\"Test Project\",\"folder\":{JsonSerializer.Serialize(app.Temp)}}}");
        Assert.Equal("Test Project", created.GetProperty("project").GetProperty("name").GetString());

        var recent = (await app.Call("project.recent")).GetProperty("recent");
        var entry = Assert.Single(recent.EnumerateArray());

        Assert.Equal("Test Project", entry.GetProperty("name").GetString());
        Assert.Equal(Path.Combine(app.Temp, "Test Project.duble"), entry.GetProperty("path").GetString());
        Assert.True(entry.GetProperty("exists").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("lastOpened").GetString()));
    }
}
