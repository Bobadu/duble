// Settings.cs — the settings of the PROGRAM, not of a project: language, theme, recent projects, window
// placement. They live in %AppData%\Bobadu\Duble\settings.json; WebView2 keeps its own data next to them in
// %LocalAppData%\Bobadu\Duble\WebView2.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duble.App;

/// <summary>One entry of the start screen's list of recent projects.</summary>
public sealed class RecentProject
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>When it was last opened, as "yyyy-MM-dd HH:mm:ss" — a label, not something to compute with.</summary>
    public string LastOpened { get; set; } = "";
}

/// <summary>Where the window was when it was last closed, so that it opens there again.</summary>
public sealed class WindowPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class Settings
{
    public const int MaxRecent = 10;
    const string SystemTheme = "system";

    /// <summary>pl, en, or null for "whatever Windows is set to".</summary>
    public string? Language { get; set; }

    /// <summary>system, dark or light.</summary>
    public string Theme { get; set; } = SystemTheme;

    /// <summary>Whether to ask GitHub for the newest release when the program starts.</summary>
    public bool CheckUpdates { get; set; } = true;

    public List<RecentProject> Recent { get; set; } = new();
    public WindowPlacement? Window { get; set; }

    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bobadu", "Duble");
    public static string FilePath => Path.Combine(DataFolder, "settings.json");
    public static string WebView2Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bobadu", "Duble", "WebView2");
    public static string ProjectsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Duble");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Settings Load(string? file = null)
    {
        file ??= FilePath;
        try
        {
            if (File.Exists(file)) return Read(File.ReadAllText(file)) ?? new Settings();
        }
        catch { /* an unreadable or damaged file must never stop the program from starting */ }
        return new Settings();
    }

    public void Save(string? file = null)
    {
        file ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>Puts a project at the top of the recent list, without letting it appear twice.</summary>
    public void Remember(string path, string name)
    {
        Recent.RemoveAll(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, new RecentProject
        {
            Path = path,
            Name = name,
            LastOpened = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        });
        if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }

    /// <summary>The language to use: the one that was chosen, or the one Windows is running in.</summary>
    [JsonIgnore]
    public string EffectiveLanguage => !string.IsNullOrEmpty(Language) ? Language
        : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";

    static Settings? Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var settings = JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings();
        ReadOldNames(document.RootElement, settings);
        return settings;
    }

    /// <summary>
    /// Fills in from a file written before these properties were renamed to English. Settings are small but
    /// not disposable: without this, updating would silently reset the language, the theme, the window
    /// position and the whole list of recent projects. The next save writes the current names.
    /// </summary>
    static void ReadOldNames(JsonElement root, Settings settings)
    {
        settings.Language ??= Text(root, "Jezyk");
        if (settings.Theme == SystemTheme && Text(root, "Motyw") is { } theme) settings.Theme = theme;

        if (settings.Recent.Count == 0 && root.TryGetProperty("Ostatnie", out var recent) && recent.ValueKind == JsonValueKind.Array)
            foreach (var entry in recent.EnumerateArray())
                settings.Recent.Add(new RecentProject
                {
                    Path = Text(entry, "Sciezka") ?? "",
                    Name = Text(entry, "Name") ?? "",
                    LastOpened = Text(entry, "Ostatnio") ?? "",
                });

        if (settings.Window == null && root.TryGetProperty("Okno", out var window) && window.ValueKind == JsonValueKind.Object)
            settings.Window = new WindowPlacement
            {
                X = Number(window, "X"), Y = Number(window, "Y"),
                Width = Number(window, "W"), Height = Number(window, "H"),
                Maximized = window.TryGetProperty("Maks", out var maximized) && maximized.ValueKind == JsonValueKind.True,
            };
    }

    static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    static double Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
}
