using System;
using System.Collections.Generic;

namespace Duble.Cli.CommandLine;

/// <summary>Everything one verb needs: how to describe it, what it accepts, and what it does.</summary>
/// <param name="Name">The verb, as typed.</param>
/// <param name="Summary">One line for the command list.</param>
/// <param name="Arguments">The positional part of the usage line, "&lt;source&gt;..." — empty when there is none.</param>
/// <param name="Options">Everything after the positionals; also what the parser validates against.</param>
/// <param name="Run">Does the work and returns the process exit code.</param>
public sealed record CliCommand(
    string Name,
    string Summary,
    string Arguments,
    IReadOnlyList<CliOption> Options,
    Func<CommandContext, int> Run);

/// <summary>What a command is handed: what was typed, where the working files live, and the engine.</summary>
/// <param name="Command">The command being run, so it can print its own usage on a bad call.</param>
/// <param name="Arguments">The parsed arguments.</param>
/// <param name="Paths">Where the catalog, the comparison and the rest live.</param>
/// <param name="Services">Duble.Core, resolved on demand — a command pays only for what it asks for.</param>
/// <param name="Output">Where to print.</param>
public sealed record CommandContext(
    CliCommand Command,
    ParsedArguments Arguments,
    CliPaths Paths,
    IServiceProvider Services,
    Output Output)
{
    public T Service<T>() where T : notnull
        => (T)(Services.GetService(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered"));

    /// <summary>Reports a call the command cannot act on, and prints how it should be called.</summary>
    public int Misuse(string problem)
    {
        Output.Error(problem);
        Output.Line(Help.Usage(Command));
        return ExitCode.Misuse;
    }
}
