using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>project.settings.get / set / resetProgi, cache.clear and calibrate.run over a made-up catalog.</summary>
public class ProjectSettingsCommandTests
{
    static TestApp Compared(string name)
    {
        var app = new TestApp(name);
        app.NewProject("U");
        SampleData.SevenGarmentsInThreeSources(app.Session, app.Temp);
        app.Session.Compare(default, null);
        app.Session.Save();
        return app;
    }

    [Fact]
    public async Task The_bin_folder_and_the_thresholds()
    {
        using var app = Compared("settings");

        var state = await app.Call("project.settings.get");
        Assert.False(state.TryGetProperty("kosz", out var bin) && bin.ValueKind != JsonValueKind.Null);
        Assert.False(state.GetProperty("progiZmienione").GetBoolean());
        Assert.Equal(20, state.GetProperty("progi").GetProperty("textureHashDistance").GetInt32());
        Assert.Equal(20, state.GetProperty("progiDomyslne").GetProperty("textureHashDistance").GetInt32());
        Assert.True(state.GetProperty("cache").GetProperty("razem").GetProperty("pliki").GetInt32() >= 0);
        Assert.EndsWith(".duble.cache", state.GetProperty("folderCache").GetString());

        var chosenBin = Path.Combine(app.Temp, "bin");
        state = await app.Call("project.settings.set", $"{{\"kosz\":{JsonSerializer.Serialize(chosenBin)}}}");
        Assert.Equal(chosenBin, state.GetProperty("kosz").GetString());
        Assert.Contains(JsonSerializer.Serialize(chosenBin).Trim('"'), File.ReadAllText(app.Session.Project.Path));

        state = await app.Call("project.settings.set", "{\"kosz\":\"\"}");
        Assert.False(state.TryGetProperty("kosz", out bin) && bin.ValueKind != JsonValueKind.Null);
        Assert.True(app.Saw("settings.changed"));

        // some of the thresholds: what was sent changes, the rest stays, and a comparison starts
        app.Sent.Clear();
        state = await app.Call("project.settings.set", "{\"progi\":{\"textureHashDistance\":24,\"textureColorDistance\":3.5}}");
        Assert.True(state.GetProperty("progiZmienione").GetBoolean());
        Assert.Equal(24, state.GetProperty("progi").GetProperty("textureHashDistance").GetInt32());
        Assert.Equal(3.5, state.GetProperty("progi").GetProperty("textureColorDistance").GetDouble());
        Assert.Equal(0.02, state.GetProperty("progi").GetProperty("geometryIdentical").GetDouble());
        Assert.True(state.GetProperty("porownanie").GetBoolean());
        await app.WaitFor("compare.done");
        Assert.Equal(24, app.Session.Project.Settings.Thresholds.TextureHashDistance);

        // the same values again change nothing, so no comparison is started
        state = await app.Call("project.settings.set", "{\"progi\":{\"textureHashDistance\":24}}");
        Assert.False(state.TryGetProperty("porownanie", out var comparing) && comparing.ValueKind != JsonValueKind.Null);

        // an impossible threshold names the field and leaves the settings alone
        var error = await app.Failing("project.settings.set", "{\"progi\":{\"textureHashDistance\":999}}");
        Assert.Equal(BridgeErrors.BadArguments, error.GetProperty("code").GetString());
        Assert.Contains("TextureHashDistance", error.GetProperty("message").GetString());
        Assert.Equal(24, app.Session.Project.Settings.Thresholds.TextureHashDistance);

        // resetting goes back to the defaults, which are stored as "nothing chosen"
        await app.WaitForIdle();
        app.Sent.Clear();
        state = await app.Call("project.settings.resetProgi");
        Assert.False(state.GetProperty("progiZmienione").GetBoolean());
        Assert.Null(app.Session.Project.Settings.Thresholds);
        Assert.True(state.GetProperty("porownanie").GetBoolean());
        await app.WaitFor("compare.done");
    }

    [Fact]
    public async Task Clearing_the_cache_takes_the_previews_and_leaves_the_thumbnails()
    {
        using var app = Compared("settings-cache");
        var project = app.Session.Project;

        Directory.CreateDirectory(project.TextureFolder);
        File.WriteAllBytes(Path.Combine(project.TextureFolder, "a.png"), new byte[300]);
        Directory.CreateDirectory(project.ThumbnailFolder);
        File.WriteAllBytes(Path.Combine(project.ThumbnailFolder, "b.png"), new byte[100]);

        var state = await app.Call("project.settings.get");
        Assert.Equal(300, state.GetProperty("cache").GetProperty("tex").GetProperty("bajty").GetInt64());

        var cleared = await app.Call("cache.clear", "{}");
        Assert.Equal(1, cleared.GetProperty("usunieto").GetInt32());
        Assert.Equal(300, cleared.GetProperty("bajty").GetInt64());
        Assert.False(File.Exists(Path.Combine(project.TextureFolder, "a.png")));
        Assert.True(File.Exists(Path.Combine(project.ThumbnailFolder, "b.png")));
    }

    [Fact]
    public async Task Calibration_measures_the_catalog_and_proposes_thresholds()
    {
        using var app = Compared("settings-calibration");

        var started = await app.Call("calibrate.run");
        Assert.True(started.GetProperty("uruchomiono").GetBoolean());
        await app.WaitFor("calibrate.done");

        var result = app.EventData("calibrate.done").GetProperty("wynik");
        Assert.Equal(7, result.GetProperty("garments").GetInt32());
        Assert.True(result.GetProperty("geoNearestForeign").GetProperty("buckets").GetArrayLength() > 0);
        Assert.True(result.GetProperty("proposal").GetProperty("textureHashDistance").GetInt32() >= 4);
        Assert.Equal(20, result.GetProperty("usedThresholds").GetProperty("textureHashDistance").GetInt32());
    }

    [Fact]
    public async Task Calibration_needs_something_to_measure()
    {
        using var app = new TestApp("settings-empty-calibration");
        app.NewProject("E");

        var error = await app.Failing("calibrate.run");

        Assert.Equal(BridgeErrors.NotFound, error.GetProperty("code").GetString());
    }
}
