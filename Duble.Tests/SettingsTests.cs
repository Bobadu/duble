using System.IO;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class SettingsTests
{
    [Fact]
    public void Saving_and_loading_keeps_the_settings_and_the_recent_projects()
    {
        var temp = TestPaths.Temp("settings");
        try
        {
            var file = Path.Combine(temp, "settings.json");
            var settings = new Settings
            {
                Language = "en",
                Theme = "dark",
                Window = new WindowPlacement { X = 10, Y = 20, Width = 1200, Height = 800, Maximized = false },
            };
            settings.Remember(@"C:\a\A.duble", "A");
            settings.Remember(@"C:\b\B.duble", "B");
            settings.Remember(@"C:\a\A.duble", "A");   // the same one again: back to the top, not twice
            settings.Save(file);

            var loaded = Settings.Load(file);
            Assert.Equal("en", loaded.Language);
            Assert.Equal("dark", loaded.Theme);
            Assert.Equal(1200, loaded.Window.Width);
            Assert.Equal(2, loaded.Recent.Count);
            Assert.Equal(@"C:\a\A.duble", loaded.Recent[0].Path);
            Assert.Equal("B", loaded.Recent[1].Name);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Without_a_file_the_defaults_are_used()
    {
        var settings = Settings.Load(Path.Combine(TestPaths.Temp("no-settings"), "not-there.json"));

        Assert.Equal("system", settings.Theme);
        Assert.Empty(settings.Recent);
        Assert.Null(settings.Language);
    }

    [Fact]
    public void A_damaged_file_gives_the_defaults_rather_than_stopping_the_program()
    {
        var file = Path.Combine(TestPaths.Temp("broken-settings"), "settings.json");
        File.WriteAllText(file, "{ this is not json");

        Assert.Equal("system", Settings.Load(file).Theme);
    }

    [Fact]
    public void At_most_ten_projects_are_remembered()
    {
        var settings = new Settings();
        for (int i = 0; i < 15; i++) settings.Remember($@"C:\p{i}\P.duble", "P" + i);

        Assert.Equal(10, settings.Recent.Count);
        Assert.Equal("P14", settings.Recent[0].Name);
    }

    /// <summary>
    /// Settings written before these properties were renamed to English. Without this, updating Duble would
    /// silently reset the language, the theme, the window and every recent project — which is the sort of
    /// thing a user only notices once it has already happened.
    /// </summary>
    [Fact]
    public void Settings_written_under_the_old_names_are_still_read()
    {
        var file = Path.Combine(TestPaths.Temp("old-settings"), "settings.json");
        File.WriteAllText(file, """
            {
              "Jezyk": "pl",
              "Motyw": "light",
              "Ostatnie": [
                { "Sciezka": "C:\\p\\Studio.duble", "Name": "Studio", "Ostatnio": "2026-08-01 10:00:00" }
              ],
              "Okno": { "X": 100, "Y": 50, "W": 1400, "H": 900, "Maks": true }
            }
            """);

        var settings = Settings.Load(file);

        Assert.Equal("pl", settings.Language);
        Assert.Equal("light", settings.Theme);
        Assert.Equal(@"C:\p\Studio.duble", Assert.Single(settings.Recent).Path);
        Assert.Equal("Studio", settings.Recent[0].Name);
        Assert.Equal("2026-08-01 10:00:00", settings.Recent[0].LastOpened);
        Assert.Equal(1400, settings.Window.Width);
        Assert.Equal(900, settings.Window.Height);
        Assert.True(settings.Window.Maximized);
    }
}
