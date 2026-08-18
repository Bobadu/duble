// duble — find duplicate clothing in GTA V packs (Legacy and Enhanced).
//
// A thin command line over Duble.Core, the same engine the desktop application runs. Everything this file does
// is choose a command, hand it what was typed, and turn what comes back into an exit code; the commands
// themselves live in Commands/ and Tools/, and `duble help` lists them.
using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Cli.Commands;
using Duble.Cli.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Duble.Cli;

public static class Program
{
    public static int Main(string[] arguments)
    {
        // Polish reasons carry diacritics; a Windows console starts in an OEM code page that mangles them
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }

        var output = Output.Console();

        if (arguments.Length == 0 || IsHelpFlag(arguments[0]))
        {
            output.Line(Help.Overview(CommandRegistry.Commands));
            return arguments.Length == 0 ? ExitCode.Misuse : ExitCode.Ok;
        }

        var command = CommandRegistry.Find(arguments[0]);
        if (command == null)
        {
            output.Error($"no such command: {arguments[0]}");
            output.Line(Help.Overview(CommandRegistry.Commands));
            return ExitCode.Misuse;
        }

        var rest = arguments.Skip(1).ToList();
        if (rest.Any(IsHelpFlag))
        {
            output.Line(Help.Full(command));
            return ExitCode.Ok;
        }

        if (!ParsedArguments.TryParse(rest, command.Options, out var parsed, out var error))
        {
            output.Error(error);
            output.Line(Help.Usage(command));
            return ExitCode.Misuse;
        }

        // Resolving CodeWalkerRuntime puts the library in gen9 mode, which reads both game formats, before any
        // command touches a file.
        using var services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        services.GetRequiredService<CodeWalkerRuntime>();
        LoadGameKeys(parsed, command, output);

        var context = new CommandContext(command, parsed, CliPaths.Resolve(parsed.Value(CliPaths.HomeOption.Name)),
                                         services, output);

        try
        {
            return command.Run(context);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            output.Error(e.Message);
            return ExitCode.Failed;
        }
        catch (OperationCanceledException)
        {
            output.Error("cancelled");
            return ExitCode.Failed;
        }
    }

    static bool IsHelpFlag(string argument)
        => argument is "--help" or "-h" or "-?" or "/?";

    /// <summary>
    /// R* encrypt the archives that ship with the game, and the keys to read them live in the game folder.
    /// Packs made by other people are not encrypted, so this only matters when indexing the game's own files.
    /// </summary>
    static void LoadGameKeys(ParsedArguments parsed, CliCommand command, Output output)
    {
        if (!command.Options.Contains(CatalogOptions.Game)) return;

        var folder = parsed.Value(CatalogOptions.Game.Name)
            ?? Environment.GetEnvironmentVariable("GTAV_ENHANCED");
        if (string.IsNullOrEmpty(folder)) return;

        if (!Directory.Exists(folder))
        {
            output.Warning($"no such game folder: {folder} — the game's own archives will not open");
            return;
        }

        try
        {
            GTA5Keys.LoadFromPath(folder, true, null);
        }
        catch (Exception e)
        {
            output.Warning($"could not read the keys from {folder}: {e.Message}");
        }
    }
}
