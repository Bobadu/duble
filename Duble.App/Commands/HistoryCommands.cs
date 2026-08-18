// Commands/HistoryCommands.cs — history.list / get / undo over the undo logs in <cache>\history\*.json.
//
// Undoing runs as a job: move the files back (all of them, or the ones of chosen garments), save the log
// again — it is the record of what came back — then index the sources of those garments and compare.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Duble.App.Commands;

public sealed class HistoryCommands : CommandModule
{
    readonly JobRunner jobs;
    readonly CatalogWorkflow workflow;
    readonly IApplyExecutor executor;
    readonly IUndoStore undoLogs;

    public HistoryCommands(Bridge bridge, Session session, JobRunner jobs, CatalogWorkflow workflow,
                           IApplyExecutor executor, IUndoStore undoLogs)
        : base(bridge, session)
    {
        this.jobs = jobs;
        this.workflow = workflow;
        this.executor = executor;
        this.undoLogs = undoLogs;
    }

    public override void Register()
    {
        Bridge.Register("history.list", _ => new { entries = All() });
        Bridge.Register("history.get", Get);
        Bridge.Register("history.undo", Undo);
    }

    List<object> All()
    {
        RequireProject();   // with nothing open the interface wants no_project, not an empty list
        var entries = new List<object>();
        foreach (var file in Session.HistoryFiles())
        {
            var log = undoLogs.Load(file);
            entries.Add(log.IsSuccess
                ? Describe(file, log.Value, details: false)
                // a log that will not parse is still shown: the files it describes are sitting in a bin folder
                : new { file = file, name = Path.GetFileName(file), error = log.Error.Message, damaged = true });
        }
        return entries;
    }

    object Get(JsonElement args)
    {
        var file = LogFile(args);
        return new { entry = Describe(file, Load(file), details: true) };
    }

    object Undo(JsonElement args)
    {
        var file = LogFile(args);
        var garmentIds = args.Strings("garments");
        var log = Load(file);
        if (!log.CanUndo) return new { started = false, restored = 0, skipped = 0 };

        bool started = jobs.TryStart(JobKinds.Undo, Path.GetFileName(file), async (cancellation, progress) =>
        {
            await Task.Yield();
            int restored, skipped;
            try
            {
                (restored, skipped) = executor.Undo(log, garmentIds.Count > 0 ? garmentIds : null, new Progress<ProgressReport>(progress));
            }
            finally
            {
                // written even after a failure: the log on disk is the record of what moved back
                undoLogs.Save(log, file);
                Bridge.Event("history.changed", new { file = file });
            }

            var chosen = garmentIds.Count > 0 ? new HashSet<string>(garmentIds) : null;
            var touched = Project.Sources
                .Where(source => log.Garments.Any(garment => garment.SourceId == source.Id && (chosen == null || chosen.Contains(garment.Id))))
                .ToList();
            if (touched.Count > 0) workflow.Index(touched, false, cancellation, progress);
            workflow.CompareAndSave(cancellation, progress);

            Bridge.Event("undo.done", new { file = file, restored = restored, skipped = skipped, undoneAt = log.UndoneAt });
        });
        if (!started) throw Busy();

        return new { started = true };
    }

    /// <summary>
    /// One log of this project, by name or by full path. Anything outside the project's history folder is a
    /// not_found: the interface asks for files by name, and a path is not a way to reach the rest of the disk.
    /// </summary>
    string LogFile(JsonElement args)
    {
        var name = args.Required("file");
        var history = Project.HistoryFolder;
        var candidate = Path.GetFullPath(Path.IsPathRooted(name) ? name : Path.Combine(history, name));
        if (!candidate.StartsWith(Path.GetFullPath(history), StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            throw new BridgeException(BridgeErrors.NotFound, name);
        return candidate;
    }

    UndoLog Load(string file)
    {
        var log = undoLogs.Load(file);
        if (log.IsFailure) throw new BridgeException(BridgeErrors.Io, log.Error.Message);
        return log.Value;
    }

    object Describe(string file, UndoLog log, bool details)
    {
        var described = new Dictionary<string, object?>
        {
            ["file"] = file,
            ["name"] = Path.GetFileName(file),
            ["when"] = log.When,
            ["description"] = log.Description,
            ["garments"] = log.Garments.Count,
            ["files"] = log.Moves.Count,
            ["bytes"] = log.Bytes,
            ["bins"] = log.Garments.Select(garment => garment.BinFolder).Where(bin => bin != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ["shared"] = log.SharedCount,
            ["inArchive"] = log.InArchiveCount,
            ["missing"] = log.MissingCount,
            ["undoneAt"] = log.UndoneAt,
            ["partlyUndone"] = log.PartlyUndone,
            ["canUndo"] = log.CanUndo,
            ["aborted"] = log.Aborted,
            ["error"] = log.Error,
        };

        if (details)
            described["list"] = log.Garments.Select(garment => new
            {
                id = garment.Id,
                name = garment.Name,
                source = garment.SourceName,
                sourceId = garment.SourceId,
                bin = garment.BinFolder,
                files = log.Moves.Where(move => move.GarmentId == garment.Id).Select(move => new
                {
                    from = move.From,
                    to = move.To,
                    bytes = move.Bytes,
                    undone = move.Undone,
                    exists = File.Exists(move.To),
                }).ToList(),
                canUndo = log.CanRestoreGarment(garment.Id),
            }).ToList();

        return described;
    }
}
