// Session.cs — what the window is working on: the open project (*.duble), its catalog of fingerprints, the
// comparison held in memory, and the caches that serve the interface.
//
// On disk that is the project file (JSON) plus catalog.json and duble.json in <project>.duble.cache\, where
// thumbnails, full-size textures and 3D previews also live. Everything here is guarded by one lock, because
// indexing runs on a worker thread while the interface keeps asking questions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeWalker.GameFiles;

namespace Duble.App;

public sealed class Session
{
    readonly object gate = new();
    readonly ICatalogStore catalogs;
    readonly IProjectStore projects;
    readonly IComparisonStore comparisons;
    readonly IDuplicateFinder duplicateFinder;
    readonly IResolutionService resolutions;
    readonly IApplyPlanner planner;
    readonly IArchiveCache archives;
    readonly IMeshPreviewBuilder meshes;
    readonly IClock clock;

    /// <summary>sha -> texture, built on demand and dropped whenever the catalog changes.</summary>
    Dictionary<string, TextureInfo>? texturesBySha;

    public Session(ICatalogStore catalogs, IProjectStore projects, IComparisonStore comparisons,
                   IDuplicateFinder duplicateFinder, IResolutionService resolutions, IApplyPlanner planner,
                   IArchiveCache archives, IMeshPreviewBuilder meshes, IClock clock)
    {
        this.catalogs = catalogs;
        this.projects = projects;
        this.comparisons = comparisons;
        this.duplicateFinder = duplicateFinder;
        this.resolutions = resolutions;
        this.planner = planner;
        this.archives = archives;
        this.meshes = meshes;
        this.clock = clock;
    }

    public Project? Project { get; private set; }
    public Catalog Catalog { get; private set; } = new();

    /// <summary>The last comparison, or null when nothing has been compared yet.</summary>
    public ComparisonResult? Comparison { get; private set; }

    public bool IsOpen => Project != null;

    /// <summary>The project, its catalog or the comparison changed. The window uses it to refresh.</summary>
    public event Action? Changed;

    // ---------------- opening, saving, closing ----------------

    public void New(string name, string file)
    {
        var project = Duble.Core.Projects.Project.Create(name, file, clock.Now);
        Directory.CreateDirectory(project.CacheFolder);
        Fail(projects.Save(project));
        lock (gate) { Project = project; Catalog = new Catalog(); Comparison = null; texturesBySha = null; }
        Changed?.Invoke();
    }

    public void Open(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("project file not found", file);
        var loaded = projects.Load(file);
        if (loaded.IsFailure) throw new IOException(loaded.Error.Message);

        var project = loaded.Value;
        Directory.CreateDirectory(project.CacheFolder);
        var catalog = catalogs.Load(project.CatalogFile);
        var comparison = File.Exists(project.ComparisonFile) ? comparisons.Load(project.ComparisonFile) : null;

        lock (gate) { Project = project; Catalog = catalog; Comparison = comparison; texturesBySha = null; }
        Changed?.Invoke();
    }

    /// <summary>Writes the project file alone — decisions, sources, settings — without the catalog.</summary>
    public void SaveProject()
    {
        var project = Project;
        if (project == null) return;
        Fail(projects.Save(project));
    }

    /// <summary>Writes everything: the project, its catalog and the comparison.</summary>
    public void Save()
    {
        lock (gate)
        {
            if (Project == null) return;
            Directory.CreateDirectory(Project.CacheFolder);
            Fail(projects.Save(Project));
            Fail(catalogs.Save(Catalog, Project.CatalogFile));
            if (Comparison != null) Fail(comparisons.Save(Comparison, Project.ComparisonFile));
        }
        Changed?.Invoke();
    }

    public void Close()
    {
        lock (gate) { Project = null; Catalog = new Catalog(); Comparison = null; texturesBySha = null; }
        Changed?.Invoke();
    }

    /// <summary>Changes the catalog under the lock — indexing does this from a worker thread.</summary>
    public void EditCatalog(Action<Catalog> change)
    {
        lock (gate) { change(Catalog); texturesBySha = null; }
    }

    /// <summary>A copy holding only the garments of enabled sources: what is compared and calibrated.</summary>
    public Catalog EnabledCatalog()
    {
        lock (gate)
        {
            var project = Project ?? throw new InvalidOperationException("no project is open");
            var enabled = new HashSet<string>(project.Sources.Where(source => source.Enabled).Select(source => source.Id ?? ""));
            return new Catalog
            {
                Garments = Catalog.Garments.Where(g => g.SourceId == null || enabled.Contains(g.SourceId)).ToList(),
            };
        }
    }

    /// <summary>The project's comparison thresholds, or the defaults.</summary>
    public Thresholds Thresholds => Project?.Settings?.Thresholds ?? Duble.Core.Comparison.Thresholds.Default;

    // ---------------- comparison ----------------

    /// <summary>
    /// Compares the garments of the enabled sources with the project's thresholds, then remembers and saves
    /// the result. Decisions carry over to the new, smaller groups, so nothing the user already settled comes
    /// back as "to reject" after applying or re-indexing.
    /// </summary>
    public void Compare(CancellationToken cancellation, Action<ProgressReport>? progress)
    {
        var project = Project ?? throw new InvalidOperationException("no project is open");
        var catalog = EnabledCatalog();
        var thresholds = project.Settings?.Thresholds ?? Duble.Core.Comparison.Thresholds.Default;
        var result = duplicateFinder.Find(catalog, thresholds,
            progress == null ? null : new Progress<ProgressReport>(progress), cancellation);

        lock (gate)
        {
            if (Comparison != null && project.Decisions.Count > 0
                && resolutions.CarryOver(project.Decisions, Comparison.Groups, result.Groups) > 0)
                Fail(projects.Save(project));
            Comparison = result;
            Directory.CreateDirectory(project.CacheFolder);
            Fail(comparisons.Save(result, project.ComparisonFile));
        }
        Changed?.Invoke();
    }

    // ---------------- applying: source of a garment, its bin, the plan ----------------

    /// <summary>The project source a garment came from; older catalogs only know the name of the pack.</summary>
    public ProjectSource? SourceOf(Garment? garment)
    {
        var project = Project;
        if (project == null || garment == null) return null;
        return project.Sources.Find(source => source.Id == garment.SourceId)
            ?? project.Sources.Find(source => string.Equals(source.Name, garment.PackName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where rejected files of a source go: the folder chosen in the project settings, or `_rejected`
    /// next to the source. Either way with a subfolder named after the source, so two sources never mix.</summary>
    public string? BinFolderFor(ProjectSource? source)
        => Project is { } project && source != null ? BinPathFor(project, source) : null;

    static string BinPathFor(Project project, ProjectSource source)
    {
        var bin = project.Settings?.BinFolder;
        if (string.IsNullOrWhiteSpace(bin))
        {
            var path = (source.Path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bin = Path.Combine(Path.GetDirectoryName(path) ?? path, BinFolder.Name);
        }
        return Path.Combine(bin, SafeFolderName(source.Name));
    }

    /// <summary>Where a garment would move to; null when its source is not in the project or not on disk.</summary>
    public BinTarget? BinTargetFor(Garment garment)
    {
        var project = Project;
        var source = SourceOf(garment);
        if (project == null || source?.Path == null || !(Directory.Exists(source.Path) || File.Exists(source.Path))) return null;
        return new BinTarget
        {
            Root = source.Path,
            BinFolder = BinPathFor(project, source),
            SourceName = source.Name ?? "",
            SourceId = source.Id ?? "",
        };
    }

    public ApplyPlan Plan(IEnumerable<string> rejected)
    {
        lock (gate) return planner.Plan(Catalog, rejected, BinTargetFor);
    }

    /// <summary>The undo logs of this project, newest first.</summary>
    public List<string> HistoryFiles()
    {
        var project = Project;
        if (project == null || !Directory.Exists(project.HistoryFolder)) return new List<string>();
        return Directory.GetFiles(project.HistoryFolder, "*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A free file name for the next undo log; the name is the time, so the sort above is the order.</summary>
    public string NewHistoryFile()
    {
        var project = Project ?? throw new InvalidOperationException("no project is open");
        Directory.CreateDirectory(project.HistoryFolder);
        var stem = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var file = Path.Combine(project.HistoryFolder, stem + ".json");
        for (int n = 2; File.Exists(file); n++) file = Path.Combine(project.HistoryFolder, $"{stem}-{n}.json");
        return file;
    }

    // ---------------- what the interface asks about ----------------

    /// <summary>The one-line summary of the project shown in the status bar; null when nothing is open.</summary>
    public object? Summary()
    {
        lock (gate)
        {
            if (Project == null) return null;
            int? duplicates = Comparison?.Groups.Count(g => g.Verdict is Verdict.Duplicate or Verdict.Superset);
            return new
            {
                name = Project.Name,
                path = Project.Path,
                sources = Project.Sources.Count,
                garments = Catalog.Garments.Count,
                textures = Catalog.Garments.Sum(g => g.Textures.Count),
                duplicates = duplicates,
                compared = Comparison?.Built,
            };
        }
    }

    /// <summary>What one source contributed. InArchive counts garments whose .ydd sits inside a .rpf and so
    /// cannot be moved.</summary>
    public (int Garments, int Textures, Dictionary<string, int> PerSlot, int Bc7, string? Format, int InArchive) Statistics(string sourceId)
    {
        lock (gate)
        {
            var garments = Catalog.Garments.Where(g => g.SourceId == sourceId).ToList();
            return (
                garments.Count,
                garments.Sum(g => g.Textures.Count),
                garments.GroupBy(g => g.Slot ?? "").OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count()),
                garments.Sum(g => g.Textures.Count(t => t.Format == "BC7")),
                SourceFormats.Of(garments).ToLabel(),
                garments.Count(g => g.ModelPath != null && g.ModelPath.Contains('|')));
        }
    }

    public Garment? FindGarment(string id)
    {
        lock (gate) return Catalog.Garments.FirstOrDefault(g => g.Id == id);
    }

    public TextureInfo? FindTexture(string sha)
    {
        if (string.IsNullOrEmpty(sha)) return null;
        lock (gate)
        {
            if (texturesBySha == null)
            {
                texturesBySha = new Dictionary<string, TextureInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var texture in Catalog.Garments.SelectMany(g => g.Textures))
                    if (texture.Sha256 != null) texturesBySha.TryAdd(texture.Sha256, texture);
            }
            return texturesBySha.GetValueOrDefault(sha);
        }
    }

    // ---------------- the project cache ----------------

    /// <summary>How much each part of the cache holds, by the names the settings screen lists them under.</summary>
    public Dictionary<string, (int Files, long Bytes)> CacheSize()
    {
        var sizes = new Dictionary<string, (int, long)>();
        var project = Project;
        if (project == null) return sizes;

        int totalFiles = 0;
        long totalBytes = 0;
        foreach (var (name, folder) in new[]
                 {
                     ("thumbs", project.ThumbnailFolder), ("tex", project.TextureFolder),
                     ("mesh", project.MeshFolder), ("history", project.HistoryFolder),
                 })
        {
            int files = 0;
            long bytes = 0;
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    files++;
                    try { bytes += new FileInfo(file).Length; } catch { /* deleted underneath us */ }
                }
            sizes[name] = (files, bytes);
            totalFiles += files;
            totalBytes += bytes;
        }
        sizes["total"] = (totalFiles, totalBytes);
        return sizes;
    }

    /// <summary>Deletes the previews that are rebuilt on demand (tex\ and mesh\). Returns what went.</summary>
    public (int Files, long Bytes) ClearCache(bool textures, bool meshes)
    {
        var project = Project;
        if (project == null) return (0, 0);

        int deleted = 0;
        long bytes = 0;
        foreach (var folder in new[] { textures ? project.TextureFolder : null, meshes ? project.MeshFolder : null })
        {
            if (folder == null || !Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder))
                try
                {
                    var size = new FileInfo(file).Length;
                    File.Delete(file);
                    deleted++;
                    bytes += size;
                }
                catch { /* in use by the interface right now; it will go with the next sweep */ }
        }
        return (deleted, bytes);
    }

    /// <summary>
    /// The bytes behind https://duble.data/&lt;category&gt;/&lt;key&gt;[?query]: a thumbnail from the cache, a
    /// full-size texture (built on first ask), or the GLB of a garment — key = garment id, query "w=&lt;letter&gt;"
    /// picks the colour variant. Null means 404.
    /// </summary>
    public Stream? Asset(string category, string key, string? query = null)
    {
        var project = Project;
        if (project == null || string.IsNullOrEmpty(key) || key.Contains("..") || key.Contains('/') || key.Contains('\\')) return null;

        string? file;
        switch (category)
        {
            case "thumb":
                file = Path.Combine(project.ThumbnailFolder, key + ".png");
                break;
            case "tex":
                file = Path.Combine(project.TextureFolder, key + ".png");
                if (!File.Exists(file) && !BuildTexture(key, file)) return null;
                break;
            case "mesh":
                file = BuildMesh(key, QueryValue(query, "w"));
                break;
            default:
                return null;
        }
        if (file == null || !File.Exists(file)) return null;
        return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    /// <summary>
    /// The GLB of a garment (highest LOD plus the texture of one variant), cached as
    /// mesh\&lt;modelSha16&gt;_&lt;textureSha16&gt;.glb. The name follows the content, so re-indexing changed
    /// files invalidates the cache by itself. Returns the file, or null when it cannot be built.
    /// </summary>
    string? BuildMesh(string garmentId, string? letter)
    {
        try
        {
            var project = Project;
            var garment = FindGarment(garmentId);
            if (project == null || garment == null || string.IsNullOrEmpty(garment.ModelPath)) return null;

            var texture = garment.Textures.FirstOrDefault(t => letter != null
                              && string.Equals(ClothingFileName.ParseTexture(t.FileName)?.Letter, letter, StringComparison.OrdinalIgnoreCase))
                          ?? garment.Textures.FirstOrDefault();

            var file = Path.Combine(project.MeshFolder, $"{Short(garment.ModelSha256)}_{Short(texture?.Sha256)}.glb");
            if (File.Exists(file)) return file;

            var preview = meshes.Build(garment, texture == null ? null : ClothingFileName.ParseTexture(texture.FileName)?.Letter);
            if (preview.IsFailure) return null;

            Directory.CreateDirectory(project.MeshFolder);
            return WriteAtomically(file, preview.Value) ? file : null;
        }
        catch { return null; }

        static string Short(string? sha) => string.IsNullOrEmpty(sha) ? "none" : sha.Length > 16 ? sha.Substring(0, 16) : sha;
    }

    /// <summary>The full-size texture (at most 1024 px on the long side) decoded from the game file into the
    /// tex\ cache. false = no such texture, or it cannot be decoded.</summary>
    bool BuildTexture(string sha, string file)
    {
        try
        {
            var texture = FindTexture(sha);
            if (texture?.Path == null) return false;

            var read = archives.Read(texture.Path);
            if (read.IsFailure) return false;

            CodeWalkerRuntime.Initialize();
            var dictionary = new YtdFile();
            RpfFile.LoadResourceFile(dictionary, read.Value, 13);
            var first = dictionary.TextureDict?.Textures?.data_items?.FirstOrDefault();
            var png = first == null ? null : TextureDecoder.PngRgba(first, 1024);
            if (png == null) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            return WriteAtomically(file, png);
        }
        catch { return false; }
    }

    /// <summary>Writes through a temporary file: two requests for the same asset can arrive at once, and a
    /// half-written PNG served to the interface looks like a corrupted texture.</summary>
    static bool WriteAtomically(string file, byte[] content)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
        File.WriteAllBytes(temporary, content);
        try { File.Move(temporary, file, true); }
        catch { try { File.Delete(temporary); } catch { /* nothing else to try */ } }
        return File.Exists(file);
    }

    static string? QueryValue(string? query, string name)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.Split('&'))
        {
            var equals = part.IndexOf('=');
            if (equals > 0 && part.Substring(0, equals) == name) return Uri.UnescapeDataString(part.Substring(equals + 1));
        }
        return null;
    }

    /// <summary>A source name is free text but ends up as a folder name inside the bin.</summary>
    static string SafeFolderName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((name ?? "source").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "source" : safe;
    }

    /// <summary>Turns a failed write into an exception: a command that cannot save has to say so.</summary>
    static void Fail(Result result)
    {
        if (result.IsFailure) throw new IOException(result.Error.Message);
    }
}
