namespace Duble.Cli.CommandLine;

/// <summary>
/// What the process returns. A script needs to tell "I asked for the wrong thing" from "the thing I asked for
/// failed", so those are separate codes.
/// </summary>
public static class ExitCode
{
    public const int Ok = 0;

    /// <summary>The command ran and could not finish: a file was missing, a model would not read.</summary>
    public const int Failed = 1;

    /// <summary>The command was called wrongly: unknown verb, unknown option, missing argument.</summary>
    public const int Misuse = 2;
}
