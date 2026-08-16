// Gry.cs — wykrywanie instalacji GTA V (Enhanced/Legacy): Steam (libraryfolders.vdf), rejestr Rockstar Games,
// manifesty Epic Games, zmienna GTAV_ENHANCED. Do kazdej gry proponujemy foldery z modami (istniejace).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Duble.App;

public static class Gry
{
    public sealed record Propozycja(string Nazwa, string Sciezka, string Typ);
    public sealed record Wykryta(string Gra, string Sciezka, List<Propozycja> Propozycje);

    static readonly Regex RePath = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);

    public static List<string> ParsujLibraryFolders(string tekstVdf)
        => RePath.Matches(tekstVdf ?? "").Select(m => m.Groups[1].Value.Replace("\\\\", "\\")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<Wykryta> Wykryj()
    {
        var kandydaci = new List<string>();
        void Dodaj(string p) { if (!string.IsNullOrWhiteSpace(p)) kandydaci.Add(p); }
        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                     ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            var biblioteki = new List<string>();
            if (steam != null)
            {
                biblioteki.Add(steam);
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) biblioteki.AddRange(ParsujLibraryFolders(File.ReadAllText(vdf)));
            }
            foreach (var b in biblioteki.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Dodaj(Path.Combine(b, "steamapps", "common", "Grand Theft Auto V Enhanced"));
                Dodaj(Path.Combine(b, "steamapps", "common", "Grand Theft Auto V"));
            }
        }
        catch { }
        try
        {
            Dodaj(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", "InstallFolder", null) as string);
            Dodaj(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\GTAV Enhanced", "InstallFolder", null) as string);
            Dodaj(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Rockstar Games\Grand Theft Auto V", "InstallFolder", null) as string);
        }
        catch { }
        try
        {
            var manifesty = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifesty))
                foreach (var f in Directory.EnumerateFiles(manifesty, "*.item"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(f));
                        var r = doc.RootElement;
                        var nazwa = r.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : "";
                        if (nazwa != null && nazwa.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase) && r.TryGetProperty("InstallLocation", out var il)) Dodaj(il.GetString());
                    }
                    catch { }
                }
        }
        catch { }
        Dodaj(Environment.GetEnvironmentVariable("GTAV_ENHANCED"));

        var wynik = new List<Wykryta>();
        foreach (var k in kandydaci.Select(k => Path.GetFullPath(k).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(k)) continue;
            string gra = File.Exists(Path.Combine(k, "GTA5_Enhanced.exe")) ? "enhanced" : File.Exists(Path.Combine(k, "GTA5.exe")) ? "legacy" : null;
            if (gra == null) continue;
            wynik.Add(new Wykryta(gra, k, PropozycjeDla(k, gra)));
        }
        return wynik.OrderBy(w => w.Gra == "enhanced" ? 0 : 1).ToList();
    }

    /// <summary>Foldery z modami warte zaindeksowania — tylko istniejace. Waniliowych archiwow gry nie proponujemy (zaszyfrowane, bez duplikatow do usuwania).</summary>
    public static List<Propozycja> PropozycjeDla(string folderGry, string gra)
    {
        var wy = new List<Propozycja>();
        var kandydaci = gra == "enhanced"
            ? new[] { ("onigiri\\dlcpacks", "onigiri (NVE) dlcpacks"), ("mods\\update\\x64\\dlcpacks", "mods dlcpacks"), ("mods", "mods") }
            : new[] { ("mods\\update\\x64\\dlcpacks", "mods dlcpacks"), ("mods", "mods") };
        foreach (var (rel, nazwa) in kandydaci)
        {
            var p = Path.Combine(folderGry, rel);
            if (!Directory.Exists(p)) continue;
            // "mods" tylko gdy nie ma juz precyzyjniejszego dlcpacks w srodku
            if (rel == "mods" && wy.Any(x => x.Sciezka.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            wy.Add(new Propozycja(nazwa, p, "folder"));
        }
        return wy;
    }
}
