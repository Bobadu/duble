using Duble.Cli.CommandLine;

namespace Duble.Cli.Commands;

/// <summary>Options more than one command accepts, declared once so they read the same everywhere.</summary>
public static class CatalogOptions
{
    public static readonly CliOption Catalog =
        CliOption.Value("--catalog", "file", "The catalog to read and write (default: <home>/catalog.json)");

    public static readonly CliOption Comparison =
        CliOption.Value("--comparison", "file", "The comparison result (default: <home>/comparison.json)");

    public static readonly CliOption Decisions =
        CliOption.Value("--decisions", "file", "The decisions table (default: <home>/decisions.tsv)");

    public static readonly CliOption UndoLog =
        CliOption.Value("--undo-log", "file", "The record of what apply moved (default: <home>/undo.json)");

    public static readonly CliOption Language =
        CliOption.Value("--lang", "pl|en", "Language of the reasons and of the report (default: en)");

    /// <summary>
    /// R* encrypt the archives that ship with the game; the keys to read them live in the game folder. Packs
    /// made by other people are not encrypted, so this is only needed to index the game's own files.
    /// </summary>
    public static readonly CliOption Game =
        CliOption.Value("--game", "folder", "GTA V folder, for the keys that open the game's own archives");

    public static readonly CliOption Out =
        CliOption.Value("--out", "path", "Where to write the result");
}
