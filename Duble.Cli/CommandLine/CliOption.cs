namespace Duble.Cli.CommandLine;

/// <summary>
/// One option a command accepts. Declaring them rather than fishing for them in the argument list is what
/// lets the parser tell an unknown option from a positional argument, and what `duble help &lt;command&gt;`
/// prints.
/// </summary>
/// <param name="Name">With the leading dashes, as it is typed: "--catalog".</param>
/// <param name="ValueName">What the value is called in the help text, or null for a flag that takes none.</param>
/// <param name="Description">One line, shown by help.</param>
public sealed record CliOption(string Name, string? ValueName, string Description)
{
    public bool IsFlag => ValueName == null;

    /// <summary>An option that takes a value: --name &lt;value&gt;.</summary>
    public static CliOption Value(string name, string valueName, string description) => new(name, valueName, description);

    /// <summary>An option that is either present or not: --force.</summary>
    public static CliOption Flag(string name, string description) => new(name, null, description);
}
