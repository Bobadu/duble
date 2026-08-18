// StartupOptions.cs — the command line of the desktop application.
//
//   Duble.exe [file.duble] [--dev] [--ui-folder <folder>] [--project <file.duble>] [--view <view>]
//             [--lang pl|en] [--theme dark|light|system] [--screenshot <file.png>] [--screenshot-delay <ms>]
//             [--exec <js>] [--dev-icon <file.ico>]
//
// Everything past --lang is for the screenshot runs that produce the pictures in the README: they drive the
// interface from outside and must not touch the user's own settings.
using System;
using System.Collections.Generic;

namespace Duble.App;

public sealed class StartupOptions
{
    /// <summary>Developer mode: the interface comes from ui\ on disk, dev tools and the context menu are on.</summary>
    public bool Dev { get; init; }

    /// <summary>Where to read the interface from in developer mode; without it the folder is searched for.</summary>
    public string? UiFolder { get; init; }

    /// <summary>A project to open once the interface is up (--project, or a .duble file double clicked in Explorer).</summary>
    public string? ProjectFile { get; init; }

    /// <summary>The view to start on, as the interface names it: sources, duplicates, catalog…</summary>
    public string? View { get; init; }

    /// <summary>pl or en. Passed to the interface as a query parameter; it does NOT change the saved setting.</summary>
    public string? Language { get; init; }

    /// <summary>dark, light or system. Like <see cref="Language"/>, for this run only.</summary>
    public string? Theme { get; init; }

    /// <summary>Where to write a PNG of the window, after which the program exits.</summary>
    public string? ScreenshotFile { get; init; }

    /// <summary>How long to wait after the interface reports it is ready before taking the screenshot.</summary>
    public int ScreenshotDelayMs { get; init; } = 700;

    /// <summary>JavaScript to run once the interface is ready and before the screenshot.</summary>
    public string? Exec { get; init; }

    /// <summary>Write the application icon to this .ico and exit; see <see cref="IconGenerator"/>.</summary>
    public string? DevIconFile { get; init; }

    public static StartupOptions Parse(string[]? args)
    {
        var rest = new List<string>(args ?? Array.Empty<string>());

        string? Value(string name)
        {
            int at = rest.IndexOf(name);
            if (at < 0 || at + 1 >= rest.Count) return null;
            var value = rest[at + 1];
            rest.RemoveRange(at, 2);
            return value;
        }

        bool dev = rest.Remove("--dev");
        var uiFolder = Value("--ui-folder");
        var project = Value("--project");
        var view = Value("--view");
        var language = Value("--lang");
        var theme = Value("--theme");
        var screenshot = Value("--screenshot");
        var exec = Value("--exec");
        var devIcon = Value("--dev-icon");
        var delay = Value("--screenshot-delay");

        // a double click on a project file hands it over as a bare argument
        project ??= rest.Find(a => a.EndsWith(".duble", StringComparison.OrdinalIgnoreCase));

        return new StartupOptions
        {
            Dev = dev,
            UiFolder = uiFolder,
            ProjectFile = project,
            View = view,
            Language = language,
            Theme = theme,
            ScreenshotFile = screenshot,
            ScreenshotDelayMs = delay != null && int.TryParse(delay, out var ms) ? ms : 700,
            Exec = exec,
            DevIconFile = devIcon,
        };
    }
}
