using System;
using System.IO;

namespace Duble.Cli.CommandLine;

/// <summary>
/// Where the working files live. The catalog is the point of the tool and it is PERSISTENT: every new pack is
/// compared against everything indexed so far, so the files have to be somewhere predictable between runs.
///
/// That somewhere is a `duble` folder in the current directory, or whatever DUBLE_HOME says. Each file can
/// also be pointed somewhere else on its own, which is what a script that keeps several catalogs does.
/// </summary>
public sealed class CliPaths
{
    public const string HomeVariable = "DUBLE_HOME";

    CliPaths(string home) => Home = home;

    public string Home { get; }

    public string Catalog => Path.Combine(Home, "catalog.json");
    public string Comparison => Path.Combine(Home, "comparison.json");
    public string Decisions => Path.Combine(Home, "decisions.tsv");
    public string UndoLog => Path.Combine(Home, "undo.json");
    public string Report => Path.Combine(Home, "report.html");

    public static CliPaths Resolve(string? fromOption)
    {
        var home = fromOption
            ?? Environment.GetEnvironmentVariable(HomeVariable)
            ?? Path.Combine(Environment.CurrentDirectory, "duble");
        return new CliPaths(Path.GetFullPath(home));
    }

    /// <summary>The shared options every command that touches the working files accepts.</summary>
    public static readonly CliOption HomeOption =
        CliOption.Value("--home", "folder", $"Where the working files live (default: ./duble, or ${HomeVariable})");
}
