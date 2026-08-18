using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using Duble.Core.Decisions;
using Duble.Core.Time;

// This class has a Path of its own, which shadows System.IO.Path throughout it. The alias says which one is
// meant without spelling the namespace out at every call.
using IOPath = System.IO.Path;

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
    [JsonIgnore] public string CatalogFile => IOPath.Combine(CacheFolder, "catalog.json");
    [JsonIgnore] public string ComparisonFile => IOPath.Combine(CacheFolder, "duble.json");
    [JsonIgnore] public string ThumbnailFolder => IOPath.Combine(CacheFolder, "thumbs");
    [JsonIgnore] public string TextureFolder => IOPath.Combine(CacheFolder, "tex");
    [JsonIgnore] public string MeshFolder => IOPath.Combine(CacheFolder, "mesh");
    [JsonIgnore] public string HistoryFolder => IOPath.Combine(CacheFolder, "history");

    public static Project Create(string name, string path, DateTimeOffset now) => new()
    {
        Name = name,
        Path = IOPath.GetFullPath(path),
        Created = now.ToString(Timestamps.Format, CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Adds a source, or returns the one already pointing at that path. The name is the folder or file name,
    /// made unique within the project (a second "stream" becomes "stream (2)"), because the catalog groups
    /// garments by pack name.
    /// </summary>
    public ProjectSource AddSource(string path, string id)
    {
        path = WithoutTrailingSeparator(IOPath.GetFullPath(path));

        var existing = Sources.Find(s => s.Path != null
            && string.Equals(WithoutTrailingSeparator(s.Path), path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var source = new ProjectSource { Id = id, Path = path, Kind = DetectKind(path) };

        var baseName = source.Kind == SourceKind.Archive
            ? IOPath.GetFileNameWithoutExtension(path)
            : IOPath.GetFileName(path);
        // "dlc.rpf" says nothing — take the pack folder instead (dlcpacks\studio_body\dlc.rpf -> studio_body)
        if (source.Kind == SourceKind.Archive && baseName.Equals("dlc", StringComparison.OrdinalIgnoreCase))
            baseName = IOPath.GetFileName(IOPath.GetDirectoryName(path)) ?? baseName;
        if (string.IsNullOrEmpty(baseName)) baseName = path;

        var name = baseName;
        for (int n = 2; Sources.Exists(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{baseName} ({n})";
        source.Name = name;

        Sources.Add(source);
        return source;
    }

    /// <summary>"C:\packs\stream\" and "C:\packs\stream" are the same source.</summary>
    static string WithoutTrailingSeparator(string path)
        => path.TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);

    /// <summary>The files that mark a folder as a FiveM resource rather than a plain folder of packs.</summary>
    static readonly string[] FiveMMarkers = { "fxmanifest.lua", "__resource.lua", "resource.toml", "__stream.cfg" };

    /// <summary>An .rpf file is an archive; a folder with a FiveM manifest or a stream subfolder is a resource; anything else is a folder.</summary>
    public static SourceKind DetectKind(string path)
    {
        if (File.Exists(path))
            return path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) ? SourceKind.Archive : SourceKind.Folder;

        foreach (var marker in FiveMMarkers)
            if (File.Exists(IOPath.Combine(path, marker))) return SourceKind.FiveMResource;

        return Directory.Exists(IOPath.Combine(path, "stream")) ? SourceKind.FiveMResource : SourceKind.Folder;
    }
}
