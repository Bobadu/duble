using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Duble.App.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Duble.Tests;

/// <summary>The sources.* commands over a real archive (studio_body\dlc.rpf, ten garments); skipped without the game.</summary>
public class SourceCommandTests
{
    readonly ITestOutputHelper output;

    public SourceCommandTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task Adding_a_source_indexing_it_and_reading_the_numbers_back()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        using var app = new TestApp("sources");
        app.NewProject("T");
        var dlc = JsonSerializer.Serialize(TestPaths.Dlc("studio_body"));

        var added = await app.Call("sources.add", $"{{\"paths\":[{dlc}]}}");
        Assert.Equal(1, added.GetProperty("added").GetArrayLength());

        var sources = (await app.Call("sources.list")).GetProperty("sources");
        var id = sources[0].GetProperty("id").GetString();
        Assert.Equal("rpf", sources[0].GetProperty("kind").GetString());
        Assert.Equal(0, sources[0].GetProperty("garments").GetInt32());
        Assert.True(sources[0].GetProperty("exists").GetBoolean());
        Assert.EndsWith(Path.Combine("_rejected", "studio_body"), sources[0].GetProperty("bin").GetString());

        await app.Call("sources.index", $"{{\"ids\":[\"{id}\"]}}");
        await app.WaitFor("compare.done");

        sources = (await app.Call("sources.list")).GetProperty("sources");
        Assert.Equal(10, sources[0].GetProperty("garments").GetInt32());   // studio_body holds ten garments
        Assert.Equal("gen9", sources[0].GetProperty("format").GetString());
        Assert.True(sources[0].GetProperty("perSlot").GetProperty("uppr").GetInt32() >= 1);
        Assert.True(File.Exists(app.Session.Project.CatalogFile));
        Assert.True(Directory.GetFiles(app.Session.Project.ThumbnailFolder, "*.png").Length > 0);
        Assert.True(app.Saw("sources.changed"));
        Assert.All(app.Session.Catalog.Garments, garment => Assert.Equal(id, garment.SourceId));

        // the same source a second time, and one that is not there, are both skipped
        var again = await app.Call("sources.add", $"{{\"paths\":[{dlc},\"C:\\\\no\\\\such\"]}}");
        Assert.Equal(0, again.GetProperty("added").GetArrayLength());
        Assert.Equal(2, again.GetProperty("skipped").GetArrayLength());

        await app.Call("sources.toggle", $"{{\"id\":\"{id}\",\"enabled\":false}}");
        Assert.False(app.Session.Project.Sources[0].Enabled);

        await app.Call("sources.remove", $"{{\"id\":\"{id}\"}}");
        Assert.Empty(app.Session.Catalog.Garments);
        Assert.Empty(app.Session.Project.Sources);
    }

    [Fact]
    public async Task Unpacking_an_archive_into_a_folder_and_adding_the_copy_as_a_source()
    {
        if (!TestPaths.HasGame) { output.WriteLine("SKIPPED: no studio_body\\dlc.rpf"); return; }

        using var app = new TestApp("unpack");
        app.NewProject("U");

        var source = app.Session.Project.AddSource(TestPaths.Dlc("studio_body"), "src1");
        Assert.Equal("studio_body", source.Name);                              // dlc.rpf takes the pack's folder name
        Assert.Equal("studio_body", SourceCommands.CopyFolderName(source));

        var folder = JsonSerializer.Serialize(Path.Combine(app.Temp, "copies"));
        var started = await app.Call("sources.unpack", $"{{\"id\":\"{source.Id}\",\"folder\":{folder},\"addAsSource\":true}}");
        Assert.Equal(Path.Combine(app.Temp, "copies", "studio_body"), started.GetProperty("folder").GetString());

        await app.WaitFor("unpack.done");
        var done = app.EventData("unpack.done");
        Assert.True(done.GetProperty("files").GetInt32() >= 20, done.ToString());
        Assert.True(done.GetProperty("inArchives").GetInt32() >= 2);
        Assert.Equal(0, done.GetProperty("errors").GetArrayLength());

        var copyId = done.GetProperty("added").GetString();
        Assert.NotNull(copyId);
        Assert.Equal(2, app.Session.Project.Sources.Count);
        Assert.False(source.Enabled);                                          // the original is switched off

        var copy = app.Session.Project.Sources.Find(other => other.Id == copyId);
        Assert.Equal(SourceKind.Folder, copy.Kind);
        Assert.True(copy.Enabled);
        Assert.Equal(10, app.Session.Catalog.Garments.Count(g => g.SourceId == copyId));   // and it was indexed
        Assert.All(app.Session.Catalog.Garments.Where(g => g.SourceId == copyId),
            garment => Assert.DoesNotContain("|", garment.ModelPath));                     // no longer inside an archive
        Assert.True(app.Saw("compare.done"));

        // the same place a second time: the folder is not empty any more
        var error = await app.Failing("sources.unpack", $"{{\"id\":\"{source.Id}\",\"folder\":{folder}}}");
        Assert.Equal(BridgeErrors.Io, error.GetProperty("code").GetString());
    }
}
