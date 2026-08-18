using System;
using System.IO;
using System.Linq;
using Duble.Cli.CommandLine;

namespace Duble.Cli.Commands;

/// <summary>
/// `duble apply` and `duble undo`. Nothing is ever deleted: rejected files MOVE into a bin folder, and the
/// record of where each one came from is written before the command returns, so undo can always put them back.
/// </summary>
public static class ApplyCommand
{
    static readonly CliOption Bin = CliOption.Value("--bin", "folder",
        $"Where rejected files go (default: a {BinFolder.Name} folder beside each source)");

    public static CliCommand Apply { get; } = new(
        "apply",
        "Move the rejected garments to the bin folder",
        "",
        new[] { CatalogOptions.Catalog, CatalogOptions.Decisions, CatalogOptions.UndoLog, Bin, CliPaths.HomeOption },
        RunApply);

    public static CliCommand Undo { get; } = new(
        "undo",
        "Put back everything the last apply moved",
        "",
        new[] { CatalogOptions.UndoLog, CliPaths.HomeOption },
        RunUndo);

    static int RunApply(CommandContext context)
    {
        var decisionsFile = context.Arguments.Value(CatalogOptions.Decisions.Name, context.Paths.Decisions)!;
        if (!File.Exists(decisionsFile))
        {
            context.Output.Error($"no decisions table at {decisionsFile} — run `duble compare` first");
            return ExitCode.Failed;
        }

        var catalog = context.Service<ICatalogStore>()
            .Load(context.Arguments.Value(CatalogOptions.Catalog.Name, context.Paths.Catalog)!);

        // The catalog holds ABSOLUTE paths. Move the packs to another machine, or another drive letter, and
        // they are stale — without this the apply would quietly move nothing and look like a success.
        var missing = catalog.Sources
            .Where(source => !Directory.Exists(source.Value) && !File.Exists(source.Value))
            .ToList();
        if (missing.Count > 0)
        {
            context.Output.Error("sources in the catalog are not on this machine — index them again:");
            foreach (var source in missing) context.Output.Detail($"{source.Key} -> {source.Value}");
            return ExitCode.Failed;
        }

        var rejected = DecisionsTable.ReadRejected(decisionsFile, catalog);
        if (rejected.Count == 0)
        {
            context.Output.Line($"nothing marked {DecisionsTable.RejectMarker} in {decisionsFile}");
            return ExitCode.Ok;
        }

        var binRoot = context.Arguments.Value(Bin.Name);
        var plan = context.Service<IApplyPlanner>().Plan(catalog, rejected,
            garment => TargetFor(garment, catalog, binRoot));

        if (plan.MissingSources.Count > 0)
            foreach (var source in plan.MissingSources)
                context.Output.Warning($"{source}: the catalog does not say where this pack lives, so its files stay put");

        var undo = context.Service<IApplyExecutor>().Execute(plan, "duble apply");
        foreach (var garment in undo.Garments)
            context.Output.Detail($"rejected: {garment.SourceName} / {garment.Name}");

        int skipped = undo.SharedCount + undo.InArchiveCount + undo.MissingCount;
        context.Output.Line($"moved {undo.Moves.Count} files, left {skipped} alone "
            + $"({undo.SharedCount} shared with a garment that stays, {undo.InArchiveCount} inside an archive, {undo.MissingCount} missing)");

        var undoFile = context.Arguments.Value(CatalogOptions.UndoLog.Name, context.Paths.UndoLog)!;
        var saved = context.Service<IUndoStore>().Save(undo, undoFile);
        if (saved.IsFailure)
        {
            // the files have already moved, so failing to write this leaves the user without a way back
            context.Output.Error($"the files moved but the record of it could not be saved: {saved.Error}");
            return ExitCode.Failed;
        }

        context.Output.Line($"undo log: {undoFile}");
        return ExitCode.Ok;
    }

    static int RunUndo(CommandContext context)
    {
        var undoFile = context.Arguments.Value(CatalogOptions.UndoLog.Name, context.Paths.UndoLog)!;
        if (!File.Exists(undoFile))
        {
            context.Output.Error($"no undo log at {undoFile}");
            return ExitCode.Failed;
        }

        var store = context.Service<IUndoStore>();
        var loaded = store.Load(undoFile);
        if (loaded.IsFailure)
        {
            context.Output.Error(loaded.Error.ToString());
            return ExitCode.Failed;
        }

        var (restored, skipped) = context.Service<IApplyExecutor>().Undo(loaded.Value);
        context.Output.Line($"put back {restored} files"
            + (skipped > 0 ? $", left {skipped} alone (gone from the bin, or something is in their old place)" : ""));

        return store.Save(loaded.Value, undoFile).IsSuccess ? ExitCode.Ok : ExitCode.Failed;
    }

    /// <summary>
    /// Where one garment's files would go. The default matches what the application does: a bin folder BESIDE
    /// the source rather than inside it, so the bin is never itself indexed as a pack, with one subfolder per
    /// pack so two packs cannot collide.
    /// </summary>
    static BinTarget? TargetFor(Garment garment, Catalog catalog, string? binRoot)
    {
        var pack = garment.PackName ?? "";
        if (!catalog.Sources.TryGetValue(pack, out var source) || string.IsNullOrEmpty(source)) return null;

        var trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = binRoot ?? Path.Combine(Path.GetDirectoryName(trimmed) ?? trimmed, BinFolder.Name);

        return new BinTarget
        {
            Root = source,
            BinFolder = Path.Combine(root, SafeFolderName(pack)),
            SourceName = pack,
        };
    }

    static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return safe.Length == 0 ? "pack" : safe;
    }
}
