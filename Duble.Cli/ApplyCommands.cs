#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Duble.Core.Apply;
using Duble.Core.Comparison;
using Duble.Core.Model;
using Duble.Core.Storage;

namespace Duble.Cli;

/// <summary>
/// `duble zastosuj` and `duble cofnij`: the command line reads the decisions from the TSV that `porownaj`
/// wrote, and hands the work to the same planner and executor the app uses.
/// </summary>
public static class ApplyCommands
{
    /// <summary>
    /// The decisions file `duble porownaj` writes: one row per garment proposed for rejection, with TAK in the
    /// first column. A person edits TAK to NIE for anything they want to keep, and `duble zastosuj` reads it
    /// back. It lives here rather than in the engine because it is Polish prose for one command-line verb.
    /// </summary>
    public static void WriteDecisions(ComparisonResult result, string path)
    {
        var text = new StringBuilder();
        text.AppendLine("# Lista pozycji, ktore `duble zastosuj` przeniesie do _odrzucone\\.");
        text.AppendLine("# Zmien TAK na NIE w pierwszej kolumnie przy tych, ktore chcesz zachowac.");
        text.AppendLine("# Kolumny rozdzielone TABEM. Linie z # sa pomijane.");
        text.AppendLine("odrzucic\twerdykt\tpozycja\tzostaje_zamiast\tpowod");

        foreach (var group in result.Groups.Where(g => g.Verdict == Verdict.Duplicate || g.Verdict == Verdict.Superset))
            foreach (var id in group.Members.Where(member => member != group.Winner))
            {
                var reason = Texts.Reason(group.Pairs.FirstOrDefault()?.Reason ?? group.Reason, "pl");
                text.AppendLine($"TAK\t{group.Verdict.ToKey()}\t{id}\t{group.Winner}\t{reason.Replace('\t', ' ')}");
            }

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }

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
