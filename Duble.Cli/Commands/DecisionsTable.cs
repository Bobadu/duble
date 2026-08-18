using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Duble.Cli.Commands;

/// <summary>
/// The table between `duble compare` and `duble apply`: one row per garment the comparison proposes rejecting,
/// with YES in the first column. A person changes YES to NO for anything they want to keep, and apply reads it
/// back.
///
/// It is a tab-separated file on purpose — it opens in a spreadsheet and edits in a text editor, and both
/// leave it in a shape this can read again.
/// </summary>
public static class DecisionsTable
{
    public const string RejectMarker = "YES";
    const string Header = "reject\tverdict\tgarment\tkeeps_instead\treason";

    public static void Write(ComparisonResult result, string path, string language)
    {
        var text = new StringBuilder();
        text.AppendLine("# Garments `duble apply` will move to the bin folder.");
        text.AppendLine($"# Change {RejectMarker} to NO in the first column for anything you want to keep.");
        text.AppendLine("# Columns are separated by TABs. Lines starting with # are ignored.");
        text.AppendLine(Header);

        foreach (var group in result.Groups.Where(g => g.Verdict == Verdict.Duplicate || g.Verdict == Verdict.Superset))
            foreach (var id in group.Members.Where(member => member != group.Winner))
            {
                var reason = Texts.Reason(group.Pairs.FirstOrDefault()?.Reason ?? group.Reason, language);
                text.AppendLine($"{RejectMarker}\t{group.Verdict.ToKey()}\t{id}\t{group.Winner}\t{reason.Replace('\t', ' ')}");
            }

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }

    /// <summary>The garment ids still marked for rejection. Anything the catalog does not know is skipped.</summary>
    public static List<string> ReadRejected(string path, Catalog catalog)
    {
        var known = catalog.Garments.Where(garment => garment.Id != null).Select(garment => garment.Id!).ToHashSet(StringComparer.Ordinal);
        var rejected = new List<string>();

        foreach (var raw in File.ReadAllLines(path))
        {
            // the file is written with a byte-order mark so a spreadsheet reads the Polish reasons correctly;
            // it lands on the first line and would otherwise hide the # that makes that line a comment
            var line = raw.TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(Header, StringComparison.Ordinal)) continue;

            var fields = line.Split('\t');
            if (fields.Length < 3) continue;
            if (!fields[0].Trim().Equals(RejectMarker, StringComparison.OrdinalIgnoreCase)) continue;
            if (known.Contains(fields[2])) rejected.Add(fields[2]);
        }

        return rejected;
    }
}
