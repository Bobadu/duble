using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Duble.Core.Time;

namespace Duble.Core.Apply;

/// <summary>Carries a plan out, and puts it back.</summary>
public interface IApplyExecutor
{
    /// <summary>
    /// Moves what the plan says to move and returns the log that undoes it. Cancelling stops between files:
    /// whatever has already moved stays moved and is in the log, which is marked as aborted. The caller must
    /// save that log either way — it is the only record of where those files went.
    /// </summary>
    UndoLog Execute(ApplyPlan plan, string description,
                    IProgress<ProgressReport>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Puts the files back where they came from. Without garmentIds the whole log is undone; with them, only
    /// those garments. Returns how many files came back and how many were skipped — a file is skipped when it
    /// is no longer in the bin, or when something else is already sitting where it would return to.
    /// </summary>
    (int restored, int skipped) Undo(UndoLog log, IEnumerable<string>? garmentIds = null,
                                     IProgress<ProgressReport>? progress = null);
}

/// <inheritdoc />
public sealed class ApplyExecutor : IApplyExecutor
{
    readonly IClock clock;

    public ApplyExecutor(IClock clock) => this.clock = clock;

    public UndoLog Execute(ApplyPlan plan, string description,
                           IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        var log = new UndoLog
        {
            When = clock.Stamp(),
            Description = description,
            SharedCount = plan.SharedCount,
            InArchiveCount = plan.InArchiveCount,
            MissingCount = plan.MissingCount,
        };

        var moves = plan.Garments
            .SelectMany(g => g.Files.Where(f => f.State == FileMoveState.Move).Select(f => (Garment: g, File: f)))
            .ToList();
        var undone = new Dictionary<string, UndoneGarment>();
        int done = 0;

        foreach (var (garment, file) in moves)
        {
            progress?.Report(new ProgressReport("apply", done, moves.Count, garment.Name));
            if (ct.IsCancellationRequested) { log.Aborted = true; break; }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.To) ?? "");
                // an older reject of the same name: overwrite it, this is the bin
                if (File.Exists(file.To)) File.Delete(file.To);
                File.Move(file.From, file.To);
            }
            catch (Exception e)
            {
                log.Aborted = true;
                log.Error = $"{file.From}: {e.Message}";
                break;
            }

            log.Moves.Add(new FileRestore { From = file.From, To = file.To, GarmentId = garment.Id, Bytes = file.Bytes });

            if (!undone.TryGetValue(garment.Id, out var entry))
                undone[garment.Id] = entry = new UndoneGarment
                {
                    Id = garment.Id,
                    Name = garment.Name + (string.IsNullOrEmpty(garment.Suffix) ? "" : " " + garment.Suffix),
                    SourceName = garment.SourceName,
                    SourceId = garment.SourceId,
                    BinFolder = garment.BinFolder,
                };
            entry.Files++;
            done++;
        }

        progress?.Report(new ProgressReport("apply", done, moves.Count, null));
        log.Garments = undone.Values.ToList();
        return log;
    }

    public (int restored, int skipped) Undo(UndoLog log, IEnumerable<string>? garmentIds = null,
                                            IProgress<ProgressReport>? progress = null)
    {
        var only = garmentIds == null ? null : new HashSet<string>(garmentIds);
        var toRestore = log.Moves.Where(m => !m.Undone && (only == null || only.Contains(m.GarmentId))).ToList();
        int restored = 0, skipped = 0, done = 0;

        foreach (var move in toRestore)
        {
            progress?.Report(new ProgressReport("undo", done++, toRestore.Count, Path.GetFileName(move.From)));

            if (!File.Exists(move.To))
            {
                // not in the bin but back in its old place: already undone in practice — for instance when the
                // log did not get saved after a move
                if (File.Exists(move.From)) move.Undone = true; else skipped++;
                continue;
            }

            // something else is sitting where it would return to; leave both alone
            if (File.Exists(move.From)) { skipped++; continue; }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.From) ?? "");
                File.Move(move.To, move.From);
                move.Undone = true;
                restored++;
                RemoveEmptyFolders(Path.GetDirectoryName(move.To) ?? "");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        if (log.Moves.Count > 0 && log.Moves.All(m => m.Undone))
            log.UndoneAt = clock.Stamp();

        return (restored, skipped);
    }

    /// <summary>
    /// Tidies empty bin folders after an undo, up to eight levels up, so undoing does not leave an empty
    /// _odrzucone skeleton behind.
    /// </summary>
    static void RemoveEmptyFolders(string folder)
    {
        for (int level = 0; level < 8 && !string.IsNullOrEmpty(folder); level++)
        {
            try
            {
                if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) return;
                Directory.Delete(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }
            folder = Path.GetDirectoryName(folder) ?? "";
        }
    }
}
