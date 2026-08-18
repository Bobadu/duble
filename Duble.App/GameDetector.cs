// GameDetector.cs — finding an installed GTA V (Enhanced or Legacy): Steam (libraryfolders.vdf), the Rockstar
// Games registry keys, the Epic Games manifests and the GTAV_ENHANCED variable. Each install then gets the
// folders worth indexing suggested.
//
// Duble does not need the game — it works on the pack files — so nothing here is required to be found.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Duble.App;

/// <summary>A folder of an install worth offering as a source.</summary>
public sealed record GameFolder(string Name, string Path, string Kind);

/// <summary>An install that was found. Edition is "enhanced" or "legacy", as the interface names them.</summary>
public sealed record DetectedGame(string Edition, string Path, List<GameFolder> Folders);

public static class GameDetector
{
    const string Enhanced = "enhanced";
    const string Legacy = "legacy";

    static readonly Regex LibraryPath = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>The library folders out of Steam's libraryfolders.vdf, whose format is its own little thing.</summary>
    public static List<string> ParseLibraryFolders(string? vdf)
        => LibraryPath.Matches(vdf ?? "")
            .Select(match => match.Groups[1].Value.Replace("\\\\", "\\"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every install found, Enhanced first. Anything that throws on the way is simply not found.</summary>
    public static List<DetectedGame> Detect()
    {
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)) candidates.Add(path);
        }

        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                     ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            var libraries = new List<string>();
            if (steam != null)
            {
                libraries.Add(steam);
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
            }
            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Add(Path.Combine(library, "steamapps", "common", "Grand Theft Auto V Enhanced"));
                Add(Path.Combine(library, "steamapps", "common", "Grand Theft Auto V"));
            }
        }
        catch { /* no Steam, or no permission to read its keys */ }

        try
        {
            Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", "InstallFolder", null) as string);
            Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\GTAV Enhanced", "InstallFolder", null) as string);
            Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Rockstar Games\Grand Theft Auto V", "InstallFolder", null) as string);
        }
        catch { /* as above */ }

        try
        {
            var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifests))
                foreach (var manifest in Directory.EnumerateFiles(manifests, "*.item"))
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                        var root = document.RootElement;
                        var name = root.TryGetProperty("DisplayName", out var displayName) ? displayName.GetString() : null;
                        if (name != null && name.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase)
                            && root.TryGetProperty("InstallLocation", out var location))
                            Add(location.GetString());
                    }
                    catch { /* one unreadable manifest does not stop the rest */ }
        }
        catch { /* no Epic launcher */ }

        Add(Environment.GetEnvironmentVariable("GTAV_ENHANCED"));

        var found = new List<DetectedGame>();
        foreach (var folder in candidates.Select(c => Path.GetFullPath(c).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            var edition = File.Exists(Path.Combine(folder, "GTA5_Enhanced.exe")) ? Enhanced
                : File.Exists(Path.Combine(folder, "GTA5.exe")) ? Legacy
                : null;
            if (edition == null) continue;
            found.Add(new DetectedGame(edition, folder, FoldersOf(folder, edition)));
        }
        return found.OrderBy(game => game.Edition == Enhanced ? 0 : 1).ToList();
    }

    /// <summary>
    /// The folders of an install worth indexing, and only the ones that exist. The game's own archives are
    /// never offered: they are encrypted, and they hold no duplicates to remove.
    /// </summary>
    public static List<GameFolder> FoldersOf(string gameFolder, string edition)
    {
        var candidates = edition == Enhanced
            ? new[] { ("onigiri\\dlcpacks", "onigiri (NVE) dlcpacks"), ("mods\\update\\x64\\dlcpacks", "mods dlcpacks"), ("mods", "mods") }
            : new[] { ("mods\\update\\x64\\dlcpacks", "mods dlcpacks"), ("mods", "mods") };

        var folders = new List<GameFolder>();
        foreach (var (relative, name) in candidates)
        {
            var path = Path.Combine(gameFolder, relative);
            if (!Directory.Exists(path)) continue;
            // "mods" only when nothing more precise inside it has already been offered
            if (relative == "mods" && folders.Any(f => f.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase))) continue;
            folders.Add(new GameFolder(name, path, "folder"));
        }
        return folders;
    }
}
