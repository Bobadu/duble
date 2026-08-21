// Updates.cs — whether a newer Duble has been released, and getting it installed.
//
// The one place the program talks to the network. GitHub is asked for the latest release and nothing is sent
// beyond the request itself; Settings can turn the check at start off entirely. The application asks through
// GitHubUpdateSource and installs through VelopackInstaller; the tests bring their own IUpdateSource and
// IUpdateInstaller, so the suite never reaches the network.
using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Duble.App;

/// <summary>The newest release, as the update check needs it: which version, where to get it, what it says.</summary>
public sealed record Release(string Version, string Url, string? Notes, string? Published);

/// <summary>Where the update check asks. The application asks GitHub; the tests answer themselves.</summary>
public interface IUpdateSource
{
    Task<Release> Latest(CancellationToken cancel = default);
}

/// <summary>
/// The repository's latest release, from GitHub's API. `releases/latest` already leaves out drafts and
/// prereleases, so whatever it names is something a person can be sent to.
/// </summary>
public sealed class GitHubUpdateSource : IUpdateSource
{
    // one client for the process: the check runs once at start and then only when a person presses the button
    static readonly HttpClient Client = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub answers 403 to a request that does not say who is asking
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Duble", Commands.AppCommands.Version()));
        return client;
    }

    public async Task<Release> Latest(CancellationToken cancel = default)
    {
        var repository = Commands.AppCommands.Repository
            .Replace("https://github.com/", "https://api.github.com/repos/", StringComparison.Ordinal);

        using var response = await Client.GetAsync(repository + "/releases/latest", cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        var root = json.RootElement;
        return new Release(
            Version: root.GetProperty("tag_name").GetString() ?? "",
            Url: root.GetProperty("html_url").GetString() ?? Commands.AppCommands.Repository + "/releases/latest",
            Notes: root.TryGetProperty("body", out var body) ? body.GetString() : null,
            Published: root.TryGetProperty("published_at", out var published) ? published.GetString() : null);
    }
}

/// <summary>Getting the newer release installed in place. The installed program can; the portable exe cannot.</summary>
public interface IUpdateInstaller
{
    /// <summary>Whether this running copy can update itself — true when it was put here by the Setup.</summary>
    bool CanApply { get; }

    /// <summary>
    /// Downloads the newest release, reporting percent done, then restarts into it. Every happy path ends
    /// with the process being replaced — when this returns at all, the restart did not happen.
    /// </summary>
    Task Apply(Action<int> progress, CancellationToken cancel = default);
}

/// <summary>
/// Velopack, against this repository's GitHub releases: the same place the Setup came from. It reads
/// `releases.win.json` from the release assets, downloads the package, and hands the swap to Update.exe.
/// </summary>
public sealed class VelopackInstaller : IUpdateInstaller
{
    static UpdateManager Manager => new(new GithubSource(Commands.AppCommands.Repository, null, prerelease: false));

    public bool CanApply
    {
        get
        {
            try { return Manager.IsInstalled; }
            catch { return false; } // an odd install layout must read as "cannot", never as a crash at start
        }
    }

    public async Task Apply(Action<int> progress, CancellationToken cancel = default)
    {
        var manager = Manager;
        var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("there is no newer release to install");

        await manager.DownloadUpdatesAsync(update, progress, cancel).ConfigureAwait(false);
        manager.ApplyUpdatesAndRestart(update);
    }
}

/// <summary>Version numbers, the way the release tags write them.</summary>
public static class Updates
{
    static readonly Regex Numbers = new(@"^[vV]?(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether <paramref name="candidate"/> names a newer version than <paramref name="current"/>. Either may
    /// wear a leading `v` and trail anything ("2.1.0-rc1", "2.0.0+a1b2c3"); the three numbers decide, as
    /// numbers — "10.0.0" is newer than "9.0.0". A tag without three of them is never "newer": better to
    /// stay quiet about one odd tag than to nag on every start because of it.
    /// </summary>
    public static bool IsNewer(string current, string candidate)
        => Parse(current) is { } mine && Parse(candidate) is { } theirs && theirs.CompareTo(mine) > 0;

    /// <summary>"v2.1.0" as a person reads it aloud: without the v.</summary>
    public static string Plain(string version) => version.TrimStart('v', 'V');

    static (int Major, int Minor, int Patch)? Parse(string version)
    {
        var match = Numbers.Match(version.Trim());
        if (!match.Success) return null;

        int Number(int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
        return (Number(1), Number(2), Number(3));
    }
}
