using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The bridge itself and the commands that need nothing but a window: what a request and its answer look
/// like, and what happens when one goes wrong. The interface is built on this shape.
/// </summary>
public class BridgeTests
{
    [Fact]
    public async Task An_unknown_command_comes_back_as_an_error_carrying_the_request_id()
    {
        using var app = new TestApp("bridge");

        var response = JsonDocument.Parse(await app.Bridge.Handle("{\"id\":\"7\",\"cmd\":\"no.such\",\"args\":null}")).RootElement;

        Assert.Equal("7", response.GetProperty("id").GetString());
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(BridgeErrors.UnknownCommand, response.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_exception_in_a_handler_becomes_internal_instead_of_taking_the_program_down()
    {
        using var app = new TestApp("bridge");
        app.Bridge.Register("test.boom", _ => throw new InvalidOperationException("boom"));

        var error = await app.Failing("test.boom");

        Assert.Equal(BridgeErrors.Internal, error.GetProperty("code").GetString());
        Assert.Contains("boom", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_missing_argument_is_bad_args()
    {
        using var app = new TestApp("bridge");

        var error = await app.Failing("shell.openFolder", "{}");

        Assert.Equal(BridgeErrors.BadArguments, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_window_buttons_reach_the_window()
    {
        using var app = new TestApp("bridge");

        await app.Call("window.minimize");
        var state = await app.Call("window.maximize");

        Assert.Equal(new[] { "min", "max" }, app.Window.Calls);
        Assert.True(state.GetProperty("maks").GetBoolean());
    }

    [Fact]
    public async Task Language_and_theme_are_read_back_and_written_to_the_settings_file()
    {
        using var app = new TestApp("bridge", new Settings { Language = "pl", Theme = "dark" });

        Assert.Equal("pl", (await app.Call("settings.get")).GetProperty("jezyk").GetString());

        await app.Call("settings.set", "{\"motyw\":\"light\",\"jezyk\":\"en\"}");
        var settings = await app.Call("settings.get");

        Assert.Equal("light", settings.GetProperty("motyw").GetString());
        Assert.Equal("en", settings.GetProperty("jezyk").GetString());
        Assert.True(File.Exists(app.Bridge.SettingsFile));
    }

    [Fact]
    public async Task Setting_the_language_to_system_means_following_Windows()
    {
        using var app = new TestApp("bridge", new Settings { Language = "pl" });

        var settings = await app.Call("settings.set", "{\"jezyk\":\"system\"}");

        // null is left out of the JSON (WhenWritingNull), so "no key" is how "not chosen" reaches the interface
        Assert.False(settings.TryGetProperty("jezykUstawiony", out var chosen) && chosen.ValueKind != JsonValueKind.Null);
        Assert.Contains(settings.GetProperty("jezyk").GetString(), new[] { "pl", "en" });
    }

    [Fact]
    public void Events_reach_the_interface_with_their_name_and_data()
    {
        using var app = new TestApp("bridge");

        app.Bridge.Event("test.ping", new { x = 1 });

        Assert.True(app.Saw("test.ping"));
        Assert.Equal(1, app.EventData("test.ping").GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task The_dialogs_go_through_the_interface_the_window_provides()
    {
        using var app = new TestApp("bridge");
        app.Dialogs.Folder = @"C:\picked";

        var folder = await app.Call("dialogs.pickFolder", "{\"tytul\":\"x\"}");
        Assert.Equal(@"C:\picked", folder.GetProperty("sciezka").GetString());

        var files = await app.Call("dialogs.pickFiles", "{\"filtr\":\"rpf\"}");
        Assert.Equal(1, files.GetProperty("sciezki").GetArrayLength());
    }

    [Fact]
    public async Task Only_http_links_are_opened()
    {
        using var app = new TestApp("bridge");

        var error = await app.Failing("shell.openUrl", "{\"url\":\"file:///C:/Windows/System32/cmd.exe\"}");

        Assert.Equal(BridgeErrors.BadArguments, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task App_info_carries_the_name_and_a_version()
    {
        using var app = new TestApp("bridge");

        var info = await app.Call("app.info");

        Assert.Equal("Duble", info.GetProperty("nazwa").GetString());
        Assert.Equal("Bobadu", info.GetProperty("by").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+", info.GetProperty("wersja").GetString());
        Assert.Equal("MIT", info.GetProperty("licencja").GetString());
    }
}
