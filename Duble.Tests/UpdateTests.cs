using System;
using System.IO;
using System.Threading.Tasks;
using Duble.App;
using Duble.App.Commands;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The update check: how version numbers compare, what the manual check answers, and when the quiet check at
/// start speaks up. All of it against <see cref="FakeUpdateSource"/> — no test here reaches the network.
/// </summary>
public class UpdateTests
{
    // ---------------- comparing versions ----------------

    [Theory]
    [InlineData("2.0.0", "v2.0.1", true)]
    [InlineData("2.0.0", "v2.1.0", true)]
    [InlineData("2.0.0", "v3.0.0", true)]
    [InlineData("2.0.0", "v2.0.0", false)]
    [InlineData("2.1.0", "v2.0.9", false)]
    [InlineData("9.0.0", "v10.0.0", true)]        // numbers, not text: "10" beats "9"
    [InlineData("2.0.0", "2.1.0", true)]          // a tag without the leading v
    [InlineData("v2.0.0", "v2.1.0", true)]        // a current version wearing one
    [InlineData("2.0.0", "v2.1.0-rc1", true)]     // whatever trails the numbers is not compared
    [InlineData("2.0.0+a1b2c3", "v2.1.0", true)]
    [InlineData("2.0.0", "nightly", false)]       // a tag with no version in it is never "newer"
    [InlineData("?", "v9.9.9", false)]            // nor is anything newer than an unknown current version
    public void A_version_is_newer_when_its_three_numbers_say_so(string current, string candidate, bool newer)
        => Assert.Equal(newer, Updates.IsNewer(current, candidate));

    [Fact]
    public void A_tag_is_read_aloud_without_its_v()
    {
        Assert.Equal("2.1.0", Updates.Plain("v2.1.0"));
        Assert.Equal("2.1.0", Updates.Plain("2.1.0"));
    }

    // ---------------- the manual check ----------------

    [Fact]
    public async Task The_check_names_a_newer_release_and_where_to_get_it()
    {
        using var app = new TestApp("update-newer");
        app.Updates.Release = new Release("v99.0.0", "https://example.test/v99", "## What changed", "2026-08-21T10:00:00Z");

        var result = await app.Call("update.check");

        Assert.True(result.GetProperty("newer").GetBoolean());
        Assert.Equal("99.0.0", result.GetProperty("version").GetString());
        Assert.Equal("https://example.test/v99", result.GetProperty("url").GetString());
        Assert.Equal("## What changed", result.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task The_check_says_so_when_this_is_the_newest_version()
    {
        using var app = new TestApp("update-current");

        var result = await app.Call("update.check");

        Assert.False(result.GetProperty("newer").GetBoolean());
    }

    [Fact]
    public async Task A_check_a_person_asked_for_reports_its_failure()
    {
        using var app = new TestApp("update-failing");
        app.Updates.Failure = new IOException("no route to host");

        var error = await app.Failing("update.check");

        Assert.Equal("io", error.GetProperty("code").GetString());
    }

    // ---------------- the quiet check at start ----------------

    [Fact]
    public async Task A_newer_release_is_announced_at_start()
    {
        using var app = new TestApp("update-announce");
        app.Updates.Release = new Release("v99.0.0", "https://example.test/v99", "notes", null);

        await app.Module<AppCommands>().AnnounceUpdate();

        var data = app.EventData("update.available");
        Assert.Equal("99.0.0", data.GetProperty("version").GetString());
        Assert.Equal("https://example.test/v99", data.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Being_up_to_date_is_not_announced()
    {
        using var app = new TestApp("update-quiet");

        await app.Module<AppCommands>().AnnounceUpdate();

        Assert.False(app.Saw("update.available"));
    }

    [Fact]
    public async Task The_toggle_in_settings_turns_the_start_check_off()
    {
        using var app = new TestApp("update-off", new Settings { CheckUpdates = false });
        app.Updates.Release = new Release("v99.0.0", "https://example.test/v99", null, null);

        await app.Module<AppCommands>().AnnounceUpdate();

        Assert.False(app.Saw("update.available"));
    }

    [Fact]
    public async Task A_failure_at_start_is_silence_not_an_error()
    {
        using var app = new TestApp("update-offline");
        app.Updates.Failure = new IOException("offline");

        await app.Module<AppCommands>().AnnounceUpdate();

        Assert.False(app.Saw("update.available"));
    }

    // ---------------- installing in place ----------------

    [Fact]
    public async Task The_check_says_whether_this_copy_can_install_the_update_itself()
    {
        using var app = new TestApp("update-canapply");
        app.Installer.CanApply = true;

        var result = await app.Call("update.check");

        Assert.True(result.GetProperty("canApply").GetBoolean());
    }

    [Fact]
    public async Task The_portable_exe_cannot_install_itself_and_says_so()
    {
        using var app = new TestApp("update-portable");

        var result = await app.Call("update.check");

        Assert.False(result.GetProperty("canApply").GetBoolean());
    }

    [Fact]
    public async Task Applying_reports_its_progress_and_hands_over_to_the_installer()
    {
        using var app = new TestApp("update-apply");
        app.Installer.CanApply = true;

        await app.Call("update.apply");

        Assert.True(app.Installer.Applied);
        Assert.Equal(100, app.EventData("update.progress").GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task An_apply_that_fails_is_answered_not_swallowed()
    {
        using var app = new TestApp("update-apply-fails");
        app.Installer.Failure = new IOException("download interrupted");

        var error = await app.Failing("update.apply");

        Assert.Equal("io", error.GetProperty("code").GetString());
    }

    // ---------------- the setting itself ----------------

    [Fact]
    public async Task The_toggle_is_on_by_default_saved_when_changed_and_read_back()
    {
        using var app = new TestApp("update-setting");

        var before = await app.Call("settings.get");
        Assert.True(before.GetProperty("checkUpdates").GetBoolean());

        var after = await app.Call("settings.set", "{\"checkUpdates\":false}");
        Assert.False(after.GetProperty("checkUpdates").GetBoolean());
        Assert.False(Settings.Load(app.Bridge.SettingsFile).CheckUpdates);
    }

    // ---------------- the changelog in the program ----------------

    [Fact]
    public async Task The_changelog_rides_inside_the_program()
    {
        using var app = new TestApp("update-changelog");

        var result = await app.Call("app.changelog");

        var markdown = result.GetProperty("markdown").GetString();
        Assert.StartsWith("# Changelog", markdown);
        Assert.Contains("## [", markdown);
    }
}
