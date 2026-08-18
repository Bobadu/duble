using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Duble.Core.Comparison;
using Duble.Core.Decisions;
using Duble.Core.Model;

namespace Duble.Core.Reporting;

/// <inheritdoc />
public sealed class CsvExporter : ICsvExporter
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The columns in order. Each name is also the second half of its heading's dictionary key.</summary>
    static readonly string[] Columns =
    {
        "group", "verdict", "reason", "item", "suffix", "source", "container", "file",
        "points", "state", "note", "vertices", "triangles", "textures", "bytes",
    };

    readonly IResolutionService resolutions;

    public CsvExporter(IResolutionService resolutions) => this.resolutions = resolutions;

    public string Export(Catalog catalog, ComparisonResult result,
                         Func<DuplicateGroup, Resolution>? resolve = null, string language = "pl")
    {
        resolve ??= group => resolutions.Resolve(group, null);
        var byId = catalog.Garments.ToDictionary(garment => garment.Id!);
        var separator = Separator(language);

        var csv = new StringBuilder();
        csv.Append('\uFEFF');   // the byte-order mark is how Excel knows the file is UTF-8
        csv.AppendLine(Row(Columns.Select(column => Texts.T(language, "report.csv." + column)), separator));

        int number = 0;
        foreach (var group in result.Groups.Where(group => group.Members.All(byId.ContainsKey)))
        {
            number++;
            var resolution = resolve(group);
            var reason = Texts.Reason(group.Pairs.FirstOrDefault()?.Reason ?? group.Reason, language);

            foreach (var id in group.Members)
            {
                var garment = byId[id];
                var fields = new object?[]
                {
                    number,
                    Texts.Verdict(group.Verdict, language),
                    reason,
                    $"{garment.Slot}_{garment.Number:d3}",
                    garment.Suffix,
                    garment.PackName,
                    garment.Container,
                    garment.ModelPath,
                    group.Scores.TryGetValue(id, out var score) ? score.ToString("F0", Invariant) : "",
                    Texts.T(language, "report.state." + State(resolution, id)),
                    resolution.Note ?? "",
                    garment.Geometry?.Vertices ?? 0,
                    garment.Geometry?.Triangles ?? 0,
                    garment.Textures.Count,
                    garment.ModelSize + garment.Textures.Sum(texture => texture.Size),
                };
                csv.AppendLine(Row(fields.Select(field => Convert.ToString(field, Invariant) ?? ""), separator));
            }
        }

        return csv.ToString();
    }

    static string State(Resolution resolution, string id)
    {
        if (resolution.Ignored) return "ignored";
        if (resolution.Winner == id && resolution.Rejected.Count > 0) return "stays";
        return resolution.Rejected.Contains(id) ? "rejected" : "unchanged";
    }

    /// <summary>
    /// Semicolon for Polish, comma for English. Excel in a Polish locale reads a comma as a decimal point and
    /// would put a whole row in one cell, so the separator has to follow the language.
    /// </summary>
    static char Separator(string language)
    {
        var configured = Texts.T(language, "report.csv.separator");
        return configured.Length == 1 ? configured[0] : ';';
    }

    static string Row(IEnumerable<string> fields, char separator)
        => string.Join(separator, fields.Select(field => Quote(field, separator)));

    static string Quote(string field, char separator)
        => field.Contains(separator) || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;
}
