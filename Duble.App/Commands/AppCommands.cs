// Commands/AppCommands.cs — the program itself: what it is, when its interface is up, and the settings that
// belong to the program rather than to a project (language, theme, recent projects).
using System;
using System.Diagnostics;
using System.Reflection;

namespace Duble.App.Commands;

public sealed class AppCommands : ICommandModule
{
    /// <summary>The links behind the buttons in "About". Null would hide one.</summary>
    public const string ProjectSite = "https://qorion.net/duble";
    public const string Repository = "https://github.com/Bobadu/duble";
    public const string Licence = "MIT";

    readonly Bridge bridge;

    public AppCommands(Bridge bridge) => this.bridge = bridge;

    /// <summary>The interface has loaded its dictionaries and drawn once. The window waits for this before
    /// opening a project from the command line or taking a screenshot.</summary>
    public event Action? UiReady;

    public void Register()
    {
        bridge.Register("app.info", _ => Info());
        bridge.Register("ui.ready", _ => { UiReady?.Invoke(); return new { }; });
        bridge.Register("settings.get", _ => CurrentSettings());
        bridge.Register("settings.set", Change);
    }

    object Info() => new
    {
        nazwa = "Duble",
        by = "Bobadu",
        wersja = Version(),
        dev = bridge.Dev,
        strona = ProjectSite,
        repo = Repository,
        licencja = Licence,
        sciezki = new
        {
            ustawienia = bridge.SettingsFile ?? Settings.FilePath,
            webview2 = Settings.WebView2Folder,
            projekty = Settings.ProjectsFolder,
            exe = Process.GetCurrentProcess().MainModule?.FileName,
        },
    };

    object CurrentSettings() => new
    {
        jezyk = bridge.Settings.EffectiveLanguage,
        jezykUstawiony = bridge.Settings.Language,
        motyw = bridge.Settings.Theme,
        ostatnie = bridge.Settings.Recent,
    };

    object Change(System.Text.Json.JsonElement args)
    {
        var language = args.Text("jezyk");
        var theme = args.Text("motyw");

        // "" and "system" both mean "follow Windows", which is what null stands for in the file
        if (language != null) bridge.Settings.Language = language is "" or "system" ? null : language;
        if (theme != null) bridge.Settings.Theme = theme;

        try { bridge.Settings.Save(bridge.SettingsFile); }
        catch (Exception e) { throw new BridgeException(BridgeErrors.Io, e.Message); }

        return CurrentSettings();
    }

    /// <summary>The version shown in About: the informational one without the commit it was built from.</summary>
    public static string Version()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational)) return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
