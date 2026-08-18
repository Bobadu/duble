// Commands/SourceCommands.cs — sources.list / add / remove / toggle / index / cancel / detectGames /
// pickFolder / pickRpf / unpack.
//
// Indexing and unpacking are long jobs, so they go through the JobRunner: progress reaches the interface as
// "job" events, and the interface can cancel them. Indexing is always followed by a comparison, which is what
// CatalogWorkflow is for.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Duble.App.Commands;

public sealed class SourceCommands : CommandModule
{
    readonly JobRunner jobs;
    readonly CatalogWorkflow workflow;
    readonly IArchiveExtractor extractor;

    public SourceCommands(Bridge bridge, Session session, JobRunner jobs, CatalogWorkflow workflow, IArchiveExtractor extractor)
        : base(bridge, session)
    {
        this.jobs = jobs;
        this.workflow = workflow;
        this.extractor = extractor;
    }

    public override void Register()
    {
        Bridge.Register("sources.list", _ => new { sources = Project.Sources.Select(Describe).ToList() });
        Bridge.Register("sources.add", args => Add(args.Strings("paths")));
        Bridge.Register("sources.pickFolder", _ => AddFromDialog(() => Pick(Bridge.Dialogs.PickFolder(null, null))));
        Bridge.Register("sources.pickRpf", _ => AddFromDialog(() => Bridge.Dialogs.PickFiles(null, "rpf", true, null)));
        Bridge.Register("sources.remove", Remove);
        Bridge.Register("sources.toggle", Toggle);
        Bridge.Register("sources.cancel", _ => { jobs.Cancel(); return new { }; });
        Bridge.Register("sources.detectGames", _ => DetectedGames());
        Bridge.Register("sources.index", StartIndexing);
        Bridge.Register("sources.unpack", StartUnpacking);
    }

    /// <summary>A short id that stays in the project file and in the catalog (Garment.SourceId).</summary>
    static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    static string[] Pick(string? picked) => picked == null ? Array.Empty<string>() : new[] { picked };

    /// <summary>A file dialog is only worth opening when there is a project to add the source to.</summary>
    object AddFromDialog(Func<string[]> pick)
    {
        RequireProject();
        return Add(pick());
    }

    object Describe(ProjectSource source)
    {
        var statistics = Session.Statistics(source.Id ?? "");
        return new
        {
            id = source.Id,
            name = source.Name,
            path = source.Path,
            kind = source.Kind.ToLabel(),
            format = statistics.Format ?? source.Format.ToLabel(),
            enabled = source.Enabled,
            indexedAt = source.IndexedAt,
            exists = Directory.Exists(source.Path) || File.Exists(source.Path),
            garments = statistics.Garments,
            textures = statistics.Textures,
            perSlot = statistics.PerSlot,
            bc7 = statistics.Bc7,
            inArchives = statistics.InArchive,
            bin = Session.BinFolderFor(source),
        };
    }

    void SourcesChanged(string? id = null)
    {
        Bridge.Event("sources.changed", new { id });
        ProjectChanged();
    }

    object Add(IEnumerable<string> paths)
    {
        var project = Project;
        var added = new List<object>();
        var skipped = new List<string>();

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!Directory.Exists(path) && !File.Exists(path)) { skipped.Add(path); continue; }
            if (File.Exists(path) && !path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) { skipped.Add(path); continue; }

            int before = project.Sources.Count;
            var source = project.AddSource(path, NewId());
            if (project.Sources.Count == before) { skipped.Add(path); continue; }   // already in the project
            added.Add(Describe(source));
        }

        if (added.Count > 0)
        {
            Session.SaveProject();
            SourcesChanged();
        }
        return new { added = added, skipped = skipped };
    }

    object Remove(JsonElement args)
    {
        var source = Required(args);
        Project.Sources.Remove(source);
        Session.EditCatalog(catalog =>
        {
            catalog.Garments.RemoveAll(garment => garment.SourceId == source.Id);
            catalog.Sources.Remove(source.Name ?? "");
        });
        Session.Save();
        SourcesChanged(source.Id);
        return new { };
    }

    object Toggle(JsonElement args)
    {
        var source = Required(args);
        source.Enabled = args.Flag("enabled", !source.Enabled);
        Session.SaveProject();
        SourcesChanged(source.Id);
        return new { enabled = source.Enabled };
    }

    object DetectedGames() => new
    {
        games = GameDetector.Detect().Select(game => new
        {
            edition = game.Edition,
            path = game.Path,
            folders = game.Folders.Select(folder => new { name = folder.Name, path = folder.Path, kind = folder.Kind }).ToList(),
        }).ToList(),
    };

    object StartIndexing(JsonElement args)
    {
        var ids = args.Strings("ids");
        bool force = args.Flag("force");
        // no ids at all means "everything that is switched on", which is what the Index all button sends
        var sources = Project.Sources.Where(source => ids.Count == 0 ? source.Enabled : ids.Contains(source.Id ?? "")).ToList();
        if (sources.Count == 0) return new { started = false };

        var description = string.Join(", ", sources.Select(source => source.Name));
        bool started = jobs.TryStart(JobKinds.Index, description, async (cancellation, progress) =>
        {
            await Task.Yield();
            workflow.Index(sources, force, cancellation, progress);
            workflow.CompareAndSave(cancellation, progress);
        });
        if (!started) throw Busy();

        return new { started = true, sources = sources.Select(source => source.Id).ToList() };
    }

    /// <summary>
    /// Unpacks an archived source into a plain folder of files, and by default adds the copy as a source of
    /// its own with the original switched off — the point of unpacking is being able to move files at all.
    /// </summary>
    object StartUnpacking(JsonElement args)
    {
        var source = Required(args);
        var folder = args.Required("folder");
        bool addAsSource = args.Flag("addAsSource", true);

        if (!Directory.Exists(source.Path) && !File.Exists(source.Path))
            throw new BridgeException(BridgeErrors.NotFound, source.Path ?? "");

        var target = Path.Combine(folder, CopyFolderName(source));
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new BridgeException(BridgeErrors.Io, "the folder already exists and is not empty: " + target);

        bool started = jobs.TryStart(JobKinds.Unpack, source.Name ?? "", async (cancellation, progress) =>
        {
            await Task.Yield();
            var unpacked = extractor.ExtractSource(source.Path, target, new Progress<ProgressReport>(progress), cancellation);

            string? addedId = null;
            if (addAsSource && unpacked.Files > 0)
            {
                var copy = Project.AddSource(target, NewId());
                source.Enabled = false;
                addedId = copy.Id;
                Session.SaveProject();
                Bridge.Event("sources.changed", new { id = (string?)null });
                workflow.Index(new[] { copy }, false, cancellation, progress);
                workflow.CompareAndSave(cancellation, progress);
            }

            Bridge.Event("unpack.done", new
            {
                id = source.Id,
                folder = target,
                files = unpacked.Files,
                inArchives = unpacked.Archives,
                bytes = unpacked.Bytes,
                errors = unpacked.Errors.Take(20).ToList(),
                added = addedId,
            });
        });
        if (!started) throw Busy();

        return new { started = true, folder = target };
    }

    /// <summary>
    /// What the unpacked copy is called: for a `dlc.rpf` the name of the source (which is the pack's folder),
    /// for any other archive the file name (so `x.rpf` becomes a folder of that name), for a folder its name.
    /// </summary>
    public static string CopyFolderName(ProjectSource source)
    {
        var name = source.Name ?? "";
        if (source.Kind != SourceKind.Archive) return name;
        var file = Path.GetFileName(source.Path) ?? "";
        return file.Equals("dlc.rpf", StringComparison.OrdinalIgnoreCase) ? name : file;
    }

    ProjectSource Required(JsonElement args)
    {
        var id = args.Required("id");
        return Project.Sources.Find(source => source.Id == id) ?? throw new BridgeException(BridgeErrors.NotFound, id);
    }
}
