using Duble.App;
using Xunit;

namespace Duble.Tests;

public class StartupOptionsTests
{
    [Fact]
    public void Every_switch_is_read()
    {
        var options = StartupOptions.Parse(new[]
        {
            "--dev", "--ui-folder", @"C:\ui", "--project", @"C:\p\Studio.duble", "--view", "sources",
            "--lang", "en", "--theme", "dark", "--screenshot", @"C:\shot.png", "--screenshot-delay", "1500",
        });

        Assert.True(options.Dev);
        Assert.Equal(@"C:\ui", options.UiFolder);
        Assert.Equal(@"C:\p\Studio.duble", options.ProjectFile);
        Assert.Equal("sources", options.View);
        Assert.Equal("en", options.Language);
        Assert.Equal("dark", options.Theme);
        Assert.Equal(@"C:\shot.png", options.ScreenshotFile);
        Assert.Equal(1500, options.ScreenshotDelayMs);
    }

    [Fact]
    public void Without_arguments_nothing_is_set()
    {
        var options = StartupOptions.Parse(new string[0]);

        Assert.False(options.Dev);
        Assert.Null(options.UiFolder);
        Assert.Null(options.ProjectFile);
        Assert.Null(options.View);
        Assert.Null(options.ScreenshotFile);
        Assert.Equal(700, options.ScreenshotDelayMs);
    }

    [Fact]
    public void A_duble_file_on_its_own_is_the_project_to_open()
    {
        var options = StartupOptions.Parse(new[] { @"C:\p\Mine.duble" });   // a double click in Explorer
        Assert.Equal(@"C:\p\Mine.duble", options.ProjectFile);
    }

    [Fact]
    public void A_switch_without_its_value_is_ignored_rather_than_crashing()
    {
        var options = StartupOptions.Parse(new[] { "--project" });
        Assert.Null(options.ProjectFile);
    }
}
