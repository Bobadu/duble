using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using Duble.Core.Decisions;
using Duble.Core.Time;

namespace Duble.Core.Projects;

/// <summary>
/// A Duble project (*.duble): the sources, the user's decisions and the settings. Next to the file sits
/// &lt;file&gt;.cache\ — the catalog, thumbnails, textures, meshes and apply history. That folder is
/// reproducible by indexing again and can be deleted at any time.
/// </summary>
public class Project
{
    /// <summary>2 = English keys. An older file is refused rather than guessed at; there is no version 1 left.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public string? Name { get; set; }
    public string? Created { get; set; }
    public List<ProjectSource> Sources { get; set; } = new();

    /// <summary>Group id to the decision the user made about it. The keys outlive re-indexing and re-comparison.</summary>
    public Dictionary<string, Decision> Decisions { get; set; } = new();

    public ProjectSettings Settings { get; set; } = new();

    [JsonIgnore] public string? Path { get; set; }
    [JsonIgnore] public string CacheFolder => Path + ".cache";
    [JsonIgnore] public string CatalogFile => System.IO.Path.Combine(CacheFolder, "catalog.json");
    [JsonIgnore] public string ComparisonFile => System.IO.Path.Combine(CacheFolder, "duble.json");
    [JsonIgnore] public string ThumbnailFolder => System.IO.Path.Combine(CacheFolder, "thumbs");
    [JsonIgnore] public string TextureFolder => System.IO.Path.Combine(CacheFolder, "tex");
    [JsonIgnore] public string MeshFolder => System.IO.Path.Combine(CacheFolder, "mesh");
    [JsonIgnore] public string HistoryFolder => System.IO.Path.Combine(CacheFolder, "history");

    public static Project Create(string name, string path, DateTimeOffset now) => new()
    {
        Name = name,
        Path = System.IO.Path.GetFullPath(path),
        Created = now.ToString(Timestamps.Format, CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Adds a source, or returns the one already pointing at that path. The name is the folder or file name,
    /// made unique within the project (a second "stream" becomes "stream (2)"), because the catalog groups
    /// garments by pack name.
    /// </summary>
    public ProjectSource AddSource(string path, string id)
    {
        path = System.IO.Path.GetFullPath(path)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        var existing = Sources.Find(s => string.Equals(
            s.Path?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
            path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var source = new ProjectSource { Id = id, Path = path, Kind = DetectKind(path) };

        var baseName = source.Kind == SourceKind.Archive
            ? System.IO.Path.GetFileNameWithoutExtension(path)
            : System.IO.Path.GetFileName(path);
        // "dlc.rpf" says nothing — take the pack folder instead (dlcpacks\studio_body\dlc.rpf -> studio_body)
        if (source.Kind == SourceKind.Archive && baseName.Equals("dlc", StringComparison.OrdinalIgnoreCase))
            baseName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? baseName;
        if (string.IsNullOrEmpty(baseName)) baseName = path;

        var name = baseName;
        for (int n = 2; Sources.Exists(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{baseName} ({n})";
        source.Name = name;

        Sources.Add(source);
        return source;
    }

    /// <summary>An .rpf file is an archive; a folder with a FiveM manifest or a stream subfolder is a resource; anything else is a folder.</summary>
    public static SourceKind DetectKind(string path)
    {
        if (File.Exists(path))
            return path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) ? SourceKind.Archive : SourceKind.Folder;

        foreach (var marker in new[] { "fxmanifest.lua", "__resource.lua", "resource.toml", "__stream.cfg" })
            if (File.Exists(System.IO.Path.Combine(path, marker))) return SourceKind.FiveMResource;

        return Directory.Exists(System.IO.Path.Combine(path, "stream")) ? SourceKind.FiveMResource : SourceKind.Folder;
    }
}
