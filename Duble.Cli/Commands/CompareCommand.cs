using Duble.Cli.CommandLine;

namespace Duble.Cli.Commands;

/// <summary>
/// `duble compare`: compares everything in the catalog and writes both the result and the decisions table a
/// person edits before `duble apply`.
/// </summary>
public static class CompareCommand
{
    public static CliCommand Command { get; } = new(
        "compare",
        "Compare everything in the catalog and write the decisions table",
        "",
        new[] { CatalogOptions.Catalog, CatalogOptions.Comparison, CatalogOptions.Decisions,
                CatalogOptions.Language, CliPaths.HomeOption },
        Run);

    static int Run(CommandContext context)
    {
        var catalogFile = context.Arguments.Value(CatalogOptions.Catalog.Name, context.Paths.Catalog)!;
        var catalog = context.Service<ICatalogStore>().Load(catalogFile);
        if (catalog.Garments.Count == 0)
        {
            context.Output.Error($"the catalog is empty ({catalogFile}) — run `duble index` first");
            return ExitCode.Failed;
        }

        var result = context.Service<IDuplicateFinder>().Find(catalog);

        var comparisonFile = context.Arguments.Value(CatalogOptions.Comparison.Name, context.Paths.Comparison)!;
        var saved = context.Service<IComparisonStore>().Save(result, comparisonFile);
        if (saved.IsFailure)
        {
            context.Output.Error(saved.Error.ToString());
            return ExitCode.Failed;
        }

        var decisionsFile = context.Arguments.Value(CatalogOptions.Decisions.Name, context.Paths.Decisions)!;
        DecisionsTable.Write(result, decisionsFile, context.Arguments.Value(CatalogOptions.Language.Name, "en")!);

        foreach (var verdict in Verdicts.All)
            if (result.Counts.TryGetValue(verdict, out int count))
                context.Output.Line($"{verdict.ToKey(),-12} {count,5}");

        context.Output.Line();
        context.Output.Line($"comparison: {comparisonFile}");
        context.Output.Line($"decisions:  {decisionsFile}  ({result.ProposedForRejection} proposed for rejection — edit before `duble apply`)");
        return ExitCode.Ok;
    }
}
