using System.IO;
using Duble.Cli.CommandLine;

namespace Duble.Cli.Commands;

/// <summary>`duble report`: the comparison as one self-contained HTML file.</summary>
public static class ReportCommand
{
    static readonly CliOption Csv = CliOption.Flag("--csv", "Write a spreadsheet of the groups and decisions instead of the page");

    public static CliCommand Command { get; } = new(
        "report",
        "Build the HTML report of the comparison",
        "",
        new[] { CatalogOptions.Catalog, CatalogOptions.Comparison, CatalogOptions.Out,
                CatalogOptions.Language, Csv, CliPaths.HomeOption },
        Run);

    static int Run(CommandContext context)
    {
        var catalog = context.Service<ICatalogStore>()
            .Load(context.Arguments.Value(CatalogOptions.Catalog.Name, context.Paths.Catalog)!);

        var comparisonFile = context.Arguments.Value(CatalogOptions.Comparison.Name, context.Paths.Comparison)!;
        if (!File.Exists(comparisonFile))
        {
            context.Output.Error($"no comparison at {comparisonFile} — run `duble compare` first");
            return ExitCode.Failed;
        }

        var result = context.Service<IComparisonStore>().Load(comparisonFile);
        // an empty report is a real answer — it says this catalog holds no duplicates
        if (result.Groups.Count == 0) context.Output.Warning("the comparison found no duplicates");

        var language = context.Arguments.Value(CatalogOptions.Language.Name, "en")!;
        bool csv = context.Arguments.Flag(Csv.Name);
        var path = context.Arguments.Value(CatalogOptions.Out.Name)
            ?? (csv ? Path.ChangeExtension(context.Paths.Report, ".csv") : context.Paths.Report);

        if (csv)
        {
            var text = context.Service<ICsvExporter>().Export(catalog, result, null, language);
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            // the exporter puts a byte-order mark in the text itself, so the file must not gain a second one
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        }
        else
        {
            context.Service<IHtmlReportBuilder>().Build(catalog, result, path,
                new ReportOptions { Language = language, Log = context.Output.Line });
        }

        context.Output.Line($"report: {path}");
        return ExitCode.Ok;
    }
}
