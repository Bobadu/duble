#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duble.Core.Apply;
using Duble.Core.Model;
using Duble.Core.Storage;

namespace Duble.Cli;

/// <summary>
/// `duble zastosuj` and `duble cofnij`: the command line reads the decisions from the TSV that `porownaj`
/// wrote, and hands the work to the same planner and executor the app uses.
/// </summary>
public static class ApplyCommands
{
    public static int Apply(IApplyPlanner planner, IApplyExecutor executor, IUndoStore undoStore,
                            Catalog catalog, string decisionsFile, string binRoot, string undoFile,
                            Action<string> log)
    {
        if (!File.Exists(decisionsFile))
        {
            log($"[blad] brak pliku decyzji: {decisionsFile} — najpierw `duble porownaj`");
            return 1;
        }

        // The catalog holds ABSOLUTE paths. Move a project to another machine (a different drive letter) and
        // they are stale — without this guard the apply would quietly move nothing and look like a success.
        var deadSources = catalog.Sources.Where(s => !Directory.Exists(s.Value) && !File.Exists(s.Value)).ToList();
        if (deadSources.Count > 0)
        {
            log("[blad] zrodla z katalogu nie istnieja na tej maszynie — przeindeksuj (`duble indeks`):");
            foreach (var source in deadSources) log($"    {source.Key} -> {source.Value}");
            return 1;
        }

        var byId = catalog.Garments.Where(g => g.Id != null).ToDictionary(g => g.Id!);
        var rejected = new List<string>();
        foreach (var line in File.ReadAllLines(decisionsFile))
        {
            if (line.Length == 0 || line.StartsWith("odrzucic")) continue;
            var fields = line.Split('\t');
            if (fields.Length < 3) continue;
            if (!fields[0].Trim().Equals("TAK", StringComparison.OrdinalIgnoreCase)) continue;
            if (byId.ContainsKey(fields[2])) rejected.Add(fields[2]);
        }
        if (rejected.Count == 0) { log("nic do odrzucenia"); return 0; }

        var plan = planner.Plan(catalog, rejected, garment => new BinTarget
        {
            Root = catalog.Sources.TryGetValue(garment.PackName ?? "", out var root) ? root : "",
            BinFolder = binRoot,
            SourceName = garment.PackName ?? "",
        });

        var undo = executor.Execute(plan, "duble zastosuj");
        foreach (var garment in undo.Garments) log($"  odrzucone: {garment.SourceName} / {garment.Name}");
        log($"przeniesione pliki: {undo.Moves.Count}, pominiete: {undo.SharedCount + undo.InArchiveCount + undo.MissingCount}");

        var saved = undoStore.Save(undo, undoFile);
        if (saved.IsFailure) { log("[blad] " + saved.Error); return 1; }
        log($"cofka: {undoFile}");
        return 0;
    }

    public static int Undo(IApplyExecutor executor, IUndoStore undoStore, string undoFile, Action<string> log)
    {
        if (!File.Exists(undoFile)) { log($"[blad] brak pliku cofki: {undoFile}"); return 1; }

        var loaded = undoStore.Load(undoFile);
        if (loaded.IsFailure) { log("[blad] " + loaded.Error); return 1; }

        var (restored, skipped) = executor.Undo(loaded.Value);
        log($"przywrocone: {restored}, pominiete: {skipped}");

        var saved = undoStore.Save(loaded.Value, undoFile);
        return saved.IsSuccess ? 0 : 1;
    }
}
