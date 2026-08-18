#nullable enable
using System;
using Duble.Core.Comparison;
using Duble.Core.Decisions;
using Duble.Core.Model;

namespace Duble.Core.Reporting;

/// <summary>Everything about a report other than what it is a report of.</summary>
public sealed record ReportOptions
{
    /// <summary>Which of Core's dictionaries the report speaks; anything but "pl" falls back to English.</summary>
    public string Language { get; init; } = "pl";

    /// <summary>The project name, shown in the page heading. Without one the report carries Duble's own title.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Who stays in each group and who does not. Without one, each group gets the comparison's own proposal —
    /// the application passes the user's decisions here instead.
    /// </summary>
    public Func<DuplicateGroup, Resolution>? Resolve { get; init; }

    /// <summary>Progress lines, one every ten groups. Nothing is written without one.</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>Builds the HTML report: every group with its thumbnails, verdict and reason.</summary>
public interface IHtmlReportBuilder
{
    /// <summary>Writes the report to <paramref name="path" />, creating the directory if it is missing.</summary>
    void Build(Catalog catalog, ComparisonResult result, string path, ReportOptions? options = null);
}

/// <summary>Writes the same groups and decisions as a spreadsheet, one row per member of a group.</summary>
public interface ICsvExporter
{
    /// <summary>
    /// The table as CSV text. The separator follows the language, because Excel in a Polish locale reads
    /// a comma as a decimal point and would put every row in one cell.
    /// </summary>
    string Export(Catalog catalog, ComparisonResult result,
                  Func<DuplicateGroup, Resolution>? resolve = null, string language = "pl");
}
