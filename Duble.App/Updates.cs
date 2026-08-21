// Updates.cs — whether a newer Duble has been released, and getting it installed.
//
// The one place the program talks to the network. GitHub is asked for the latest release and nothing is sent
// beyond the requests themselves; Settings can turn the check at start off entirely. The application asks
// through GitHubUpdateSource and installs through InnoUpdateInstaller; the tests bring their own
// IUpdateSource and IUpdateInstaller, so the suite never reaches the network.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Duble.App;

/// <summary>The newest release, as the update check needs it: which version, where to get it, what it says.</summary>
public sealed record Release(string Version, string Url, string? Notes, string? Published)
{
    /// <summary>Where the release's Setup downloads from, when the release carries one.</summary>
    public string? SetupUrl { get; init; }

    /// <summary>Where the Setup's SHA-256 file downloads from — a download that does not agree is discarded.</summary>
    public string? ChecksumUrl { get; init; }
}

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
    // one client for the process, shared with the installer's downloads: the check runs once at start and
    // then only when a person presses a button
    internal static readonly HttpClient Client = CreateClient();

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

        string? setup = null, checksum = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var download = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (name == "Duble-Setup.exe") setup = download;
                if (name == "Duble-Setup.exe.sha256") checksum = download;
            }

        return new Release(
            Version: root.GetProperty("tag_name").GetString() ?? "",
            Url: root.GetProperty("html_url").GetString() ?? Commands.AppCommands.Repository + "/releases/latest",
            Notes: root.TryGetProperty("body", out var body) ? body.GetString() : null,
            Published: root.TryGetProperty("published_at", out var published) ? published.GetString() : null)
        {
            SetupUrl = setup,
            ChecksumUrl = checksum,
        };
    }
}

/// <summary>Getting the newer release installed in place. The installed program can; a loose copy cannot.</summary>
public interface IUpdateInstaller
{
    /// <summary>Whether this running copy can update itself — true when it was put here by the Setup.</summary>
    bool CanApply { get; }

    /// <summary>
    /// Downloads the newest release's Setup, reporting percent done, checks it against its published SHA-256
    /// and hands it the swap. Returning is not success yet: the caller closes the program, the Setup replaces
    /// its files, and the new version starts.
    /// </summary>
    Task Apply(Action<int> progress, CancellationToken cancel = default);
}

/// <summary>
/// The Setup, run again: installer\Duble.iss compiled by the release workflow, downloaded from the newest
/// release and started with /VERYSILENT once this program is on its way out. A detached shell bridges the
/// gap — waits for this process to be gone, runs the Setup, starts the new version.
/// </summary>
public sealed class InnoUpdateInstaller : IUpdateInstaller
{
    /// <summary>The uninstall entry the Setup writes: the AppId of installer\Duble.iss plus Inno's suffix.</summary>
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C42B95D-31A4-4E7B-9AEA-6E2D64F82D11}_is1";

    readonly IUpdateSource source;

    public InnoUpdateInstaller(IUpdateSource source) => this.source = source;

    /// <summary>Installed means: running from exactly the folder the uninstall entry says the Setup filled.</summary>
    public bool CanApply => InstalledTo() is { } location && SamePath(location, AppContext.BaseDirectory);

    public async Task Apply(Action<int> progress, CancellationToken cancel = default)
    {
        var release = await source.Latest(cancel).ConfigureAwait(false);
        if (release.SetupUrl == null) throw new InvalidOperationException("the newest release carries no Setup");

        var setup = Path.Combine(Path.GetTempPath(), "Duble-Setup-" + Updates.Plain(release.Version) + ".exe");
        await Download(release.SetupUrl, setup, progress, cancel).ConfigureAwait(false);
        await CheckAgainstPublishedHash(setup, release.ChecksumUrl, cancel).ConfigureAwait(false);

        HandOver(setup);
    }

    static string? InstalledTo()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            try
            {
                using var key = root.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string location && location.Length > 0) return location;
            }
            catch { /* a hive that cannot be read means "not installed there" */ }
        return null;
    }

    static bool SamePath(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    static async Task Download(string url, string file, Action<int> progress, CancellationToken cancel)
    {
        using var response = await GitHubUpdateSource.Client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        var buffer = new byte[81920];
        long done = 0;

        await using var from = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        await using var to = File.Create(file);
        int read;
        while ((read = await from.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            done += read;
            if (total > 0) progress((int)(done * 100 / total));
        }
    }

    /// <summary>The published SHA-256 has to agree — a download that does not is deleted, not run.</summary>
    static async Task CheckAgainstPublishedHash(string file, string? checksumUrl, CancellationToken cancel)
    {
        if (checksumUrl == null) return; // a release without the checksum file: nothing to hold the download to

        var published = (await GitHubUpdateSource.Client.GetStringAsync(checksumUrl, cancel).ConfigureAwait(false))
            .Trim().Split(' ')[0];

        await using var stream = File.OpenRead(file);
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false));

        if (!string.Equals(published, actual, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            throw new InvalidOperationException("the downloaded Setup does not match its published checksum");
        }
    }

    /// <summary>
    /// The Setup can only swap files once this process is gone, so a detached shell waits a moment, runs it
    /// silently and starts the new version. Written as a script rather than a /c one-liner: three quoted
    /// paths on one cmd line is how installers acquire folklore.
    /// </summary>
    static void HandOver(string setup)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Duble.exe");
        var script = Path.Combine(Path.GetTempPath(), "duble-update.cmd");
        File.WriteAllLines(script, new[]
        {
            "@echo off",
            "timeout /t 2 /nobreak >nul",
            $"\"{setup}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            $"start \"\" \"{exe}\"",
        });

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
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
