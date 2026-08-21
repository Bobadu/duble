// Commands/AppCommands.cs — the program itself: what it is, when its interface is up, the settings that
// belong to the program rather than to a project (language, theme, recent projects), and whether a newer
// Duble has been released.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Duble.App.Commands;

public sealed class AppCommands : ICommandModule
{
    /// <summary>The links behind the buttons in "About". Null would hide one.</summary>
    public const string ProjectSite = "https://qorion.net/duble";
    public const string Repository = "https://github.com/Bobadu/duble";
    public const string Licence = "MIT";

    readonly Bridge bridge;
    readonly IUpdateSource updates;
    readonly IUpdateInstaller installer;

    public AppCommands(Bridge bridge, IUpdateSource updates, IUpdateInstaller installer)
    {
        this.bridge = bridge;
        this.updates = updates;
        this.installer = installer;
    }

    /// <summary>The interface has loaded its dictionaries and drawn once. The window waits for this before
    /// opening a project from the command line or taking a screenshot.</summary>
    public event Action? UiReady;

    public void Register()
    {
        bridge.Register("app.info", _ => Info());
        bridge.Register("app.changelog", _ => new { markdown = ChangelogText() });
        bridge.Register("ui.ready", _ => { UiReady?.Invoke(); return new { }; });
        bridge.Register("settings.get", _ => CurrentSettings());
        bridge.Register("settings.set", Change);
        bridge.Register("update.check", _ => CheckNow());
        bridge.Register("update.apply", _ => ApplyUpdate());
    }

    object Info() => new
    {
        name = "Duble",
        by = "Bobadu",
        version = Version(),
        dev = bridge.Dev,
        website = ProjectSite,
        repository = Repository,
        licence = Licence,
        paths = new
        {
            settings = bridge.SettingsFile ?? Settings.FilePath,
            webView2 = Settings.WebView2Folder,
            projects = Settings.ProjectsFolder,
            executable = Process.GetCurrentProcess().MainModule?.FileName,
        },
    };

    object CurrentSettings() => new
    {
        language = bridge.Settings.EffectiveLanguage,
        chosenLanguage = bridge.Settings.Language,
        theme = bridge.Settings.Theme,
        recent = bridge.Settings.Recent,
        checkUpdates = bridge.Settings.CheckUpdates,
    };

    object Change(System.Text.Json.JsonElement args)
    {
        var language = args.Text("language");
        var theme = args.Text("theme");

        // "" and "system" both mean "follow Windows", which is what null stands for in the file
        if (language != null) bridge.Settings.Language = language is "" or "system" ? null : language;
        if (theme != null) bridge.Settings.Theme = theme;
        if (args.OptionalFlag("checkUpdates") is { } check) bridge.Settings.CheckUpdates = check;

        try { bridge.Settings.Save(bridge.SettingsFile); }
        catch (Exception e) { throw new BridgeException(BridgeErrors.Io, e.Message); }

        return CurrentSettings();
    }

    /// <summary>
    /// The check behind Settings' toggle, run once the interface is up. Quiet on purpose, both ways: being
    /// offline at start is normal and being up to date needs no toast — only a genuinely newer release is
    /// worth announcing.
    /// </summary>
    public async Task AnnounceUpdate()
    {
        if (!bridge.Settings.CheckUpdates) return;
        try
        {
            var release = await updates.Latest().ConfigureAwait(false);
            if (!Updates.IsNewer(Version(), release.Version)) return;

            bridge.Event("update.available", new
            {
                version = Updates.Plain(release.Version),
                url = release.Url,
                notes = release.Notes,
                canApply = installer.CanApply,
            });
        }
        catch { /* no network, rate-limited, GitHub down: the start of the program is no place to say so */ }
    }

    /// <summary>The check behind the button — a person pressed it, so a failure is answered, not swallowed.</summary>
    async Task<object> CheckNow()
    {
        Release release;
        try { release = await updates.Latest().ConfigureAwait(false); }
        catch (Exception e) { throw new BridgeException(BridgeErrors.Io, e.Message); }

        return new
        {
            version = Updates.Plain(release.Version),
            newer = Updates.IsNewer(Version(), release.Version),
            url = release.Url,
            notes = release.Notes,
            published = release.Published,
            canApply = installer.CanApply,
        };
    }

    /// <summary>
    /// The Install button. Downloading says how far it is; the happy path never answers, because the process
    /// is replaced by the new version. What does come back is a failure, and a person is watching for it.
    /// </summary>
    async Task<object> ApplyUpdate()
    {
        try
        {
            await installer.Apply(percent => bridge.Event("update.progress", new { percent })).ConfigureAwait(false);
            return new { };
        }
        catch (Exception e) { throw new BridgeException(BridgeErrors.Io, e.Message); }
    }

    /// <summary>CHANGELOG.md, embedded at build time: "what's new" needs no network and never drifts from the
    /// build it ships in.</summary>
    static string ChangelogText()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md");
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
