using System;
using System.Collections.Generic;
using System.Linq;
using Duble.Cli.Commands;
using Duble.Cli.Tools;

namespace Duble.Cli.CommandLine;

/// <summary>
/// Every verb the tool answers to, in the order a person meets them: index a pack, look at what came in,
/// compare, read the report, act on it — then the tools for looking at a single file.
/// </summary>
public static class CommandRegistry
{
    // declared before Commands on purpose: static initialisers run in source order, so listing it first is
    // what keeps the array from taking a null in its place
    static CliCommand HelpCommand { get; } = new(
        "help",
        "What a command accepts",
        "[command]",
        Array.Empty<CliOption>(),
        RunHelp);

    public static IReadOnlyList<CliCommand> Commands { get; } = new CliCommand[]
    {
        IndexCommand.Index,
        IndexCommand.Refresh,
        ListCommand.Command,
        CompareCommand.Command,
        ReportCommand.Command,
        ApplyCommand.Apply,
        ApplyCommand.Undo,
        CalibrateCommand.Command,

        // tools for one file at a time, for looking into a pack rather than tidying one
        TextureCommands.Preview,
        TextureCommands.Export,
        TextureCommands.Build,
        ModelCommands.Glb,
        ModelCommands.Hollow,
        ObjExportCommand.Command,

        HelpCommand,
    };

    public static CliCommand? Find(string name)
        => Commands.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase));

    static int RunHelp(CommandContext context)
    {
        if (context.Arguments.Positional.Count == 0)
        {
            context.Output.Line(Help.Overview(Commands));
            return ExitCode.Ok;
        }

        var name = context.Arguments.Positional[0];
        var command = Find(name);
        if (command == null)
        {
            context.Output.Error($"no such command: {name}");
            context.Output.Line(Help.Overview(Commands));
            return ExitCode.Misuse;
        }

        context.Output.Line(Help.Full(command));
        return ExitCode.Ok;
    }
}
