#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Duble.Core.Comparison;
using Duble.Core.Model;

namespace Duble.Core.Apply;

/// <summary>Works out what an apply would do, without doing any of it.</summary>
public interface IApplyPlanner
{
    /// <summary>
    /// The plan for rejecting those garments: which files move where, which are left alone because a garment
    /// that stays shares them, which sit inside an archive, and which are missing. The target callback says
    /// where each garment's bin is and what its paths are relative to; returning null for a garment marks its
    /// source as gone, and every one of its files ends up missing.
    /// </summary>
    ApplyPlan Plan(Catalog catalog, IEnumerable<string> rejectedIds, Func<Garment, BinTarget?> target);
}

/// <inheritdoc />
public sealed class ApplyPlanner : IApplyPlanner
{
    public ApplyPlan Plan(Catalog catalog, IEnumerable<string> rejectedIds, Func<Garment, BinTarget?> target)
    {
        var plan = new ApplyPlan();
        var byId = catalog.Garments.Where(g => g.Id != null).ToDictionary(g => g.Id!);
        var rejected = new HashSet<string>(rejectedIds.Where(byId.ContainsKey));
        if (rejected.Count == 0) return plan;

        // Files used by garments THAT STAY. Nothing on this list is ever moved: two garments with the same
        // slot and number share their textures, and moving "every file of the loser" would rob the winner.
        var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var garment in catalog.Garments.Where(g => g.Id != null && !rejected.Contains(g.Id)))
        {
            if (garment.ModelPath != null) protectedPaths.Add(garment.ModelPath);
            foreach (var texture in garment.Textures)
                if (texture.Path != null) protectedPaths.Add(texture.Path);
        }

        // One file can belong to two rejected garments (feet_050 and feet_050_1 both going): it moves once.
        var alreadyPlanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourcesGone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var inOrder = rejected.Select(id => byId[id])
            .OrderBy(g => g.PackName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Slot, StringComparer.Ordinal)
            .ThenBy(g => g.Number)
            .ThenBy(g => g.Suffix, StringComparer.Ordinal);

        foreach (var garment in inOrder)
        {
            var bin = target?.Invoke(garment);
            var planned = new PlannedGarment
            {
                Id = garment.Id!,
                Name = $"{garment.Slot}_{garment.Number:d3}",
                Suffix = garment.Suffix,
                Container = garment.Container ?? "",
                SourceName = bin?.SourceName ?? garment.PackName ?? "",
                SourceId = bin?.SourceId ?? garment.SourceId ?? "",
                BinFolder = bin?.BinFolder ?? "",
            };
            if (bin == null) sourcesGone.Add(planned.SourceName);

            var files = new List<(string Path, long Bytes)>();
            if (garment.ModelPath != null) files.Add((garment.ModelPath, garment.ModelSize));
            files.AddRange(garment.Textures.Where(t => t.Path != null).Select(t => (t.Path!, t.Size)));

            foreach (var (path, bytes) in files.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            {
                var move = new FileMove { GarmentId = garment.Id!, From = path, Bytes = bytes };

                if (path.Contains('|')) move.State = FileMoveState.InArchive;
                else if (protectedPaths.Contains(path)) move.State = FileMoveState.Shared;
                else if (bin == null || !File.Exists(path)) move.State = FileMoveState.Missing;
                else if (!alreadyPlanned.Add(path)) continue;   // already in another rejected garment's plan
                else
                {
                    move.State = FileMoveState.Move;
                    move.To = Path.Combine(bin.BinFolder, RelativeTo(bin.Root, path));
                }

                planned.Files.Add(move);
            }

            plan.Garments.Add(planned);
        }

        plan.MissingSources.AddRange(sourcesGone.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        return plan;
    }

    /// <summary>
    /// A file's path relative to its source root, so the bin keeps the container layout the pack had. A file
    /// from outside the root keeps its name alone.
    /// </summary>
    public static string RelativeTo(string root, string path)
    {
        if (string.IsNullOrEmpty(root)) return Path.GetFileName(path);
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);

            // the root is a file (an archive): measure from the folder it sits in
            if (File.Exists(fullRoot)) fullRoot = Path.GetDirectoryName(fullRoot) ?? fullRoot;

            var relative = Path.GetRelativePath(fullRoot, fullPath);
            return relative.StartsWith("..") || Path.IsPathRooted(relative) ? Path.GetFileName(path) : relative;
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(path);
        }
    }

    /// <summary>
    /// The decisions file the CLI writes after a comparison: one row per garment proposed for rejection, with
    /// TAK in the first column. A person edits TAK to NIE for anything they want to keep, and `duble zastosuj`
    /// reads it back.
    /// </summary>
    public static void WriteDecisions(ComparisonResult result, Catalog catalog, string path)
    {
        var text = new StringBuilder();
        text.AppendLine("# Lista pozycji, ktore `duble zastosuj` przeniesie do _odrzucone\\.");
        text.AppendLine("# Zmien TAK na NIE w pierwszej kolumnie przy tych, ktore chcesz zachowac.");
        text.AppendLine("# Kolumny rozdzielone TABEM. Linie z # sa pomijane.");
        text.AppendLine("odrzucic\twerdykt\tpozycja\tzostaje_zamiast\tpowod");

        foreach (var group in result.Groups.Where(g => g.Verdict == Verdict.Duplicate || g.Verdict == Verdict.Superset))
            foreach (var id in group.Members.Where(m => m != group.Winner))
            {
                var reason = Texts.Reason(group.Pairs.FirstOrDefault()?.Reason ?? group.Reason, "pl");
                text.AppendLine($"TAK\t{group.Verdict.ToKey()}\t{id}\t{group.Winner}\t{reason.Replace('\t', ' ')}");
            }

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }
}
