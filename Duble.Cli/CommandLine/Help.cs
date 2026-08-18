using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duble.Cli.CommandLine;

/// <summary>
/// The help text, built from the commands themselves. Nothing here is written twice: a command's options are
/// declared once and both the parser and this read them, so help cannot drift from what the tool accepts.
/// </summary>
public static class Help
{
    public static string Overview(IReadOnlyList<CliCommand> commands)
    {
        var text = new StringBuilder();
        text.AppendLine("duble — find duplicate clothing in GTA V packs (Legacy and Enhanced)");
        text.AppendLine();
        text.AppendLine("usage: duble <command> [arguments] [options]");
        text.AppendLine();

        int width = commands.Max(command => command.Name.Length);
        foreach (var command in commands)
            text.AppendLine($"  {command.Name.PadRight(width)}  {command.Summary}");

        text.AppendLine();
        text.AppendLine("A source is a folder of an unpacked pack, or an .rpf archive.");
        text.AppendLine("The catalog is persistent: every new pack is compared against everything indexed so far.");
        text.AppendLine();
        text.AppendLine("duble help <command>   what one command accepts");
        return text.ToString().TrimEnd();
    }

    public static string Usage(CliCommand command)
    {
        var text = new StringBuilder();
        text.Append("usage: duble ").Append(command.Name);
        if (command.Arguments.Length > 0) text.Append(' ').Append(command.Arguments);
        if (command.Options.Count > 0) text.Append(" [options]");
        text.AppendLine();

        if (command.Options.Count == 0) return text.ToString().TrimEnd();

        text.AppendLine();
        int width = command.Options.Max(option => Signature(option).Length);
        foreach (var option in command.Options)
            text.AppendLine($"  {Signature(option).PadRight(width)}  {option.Description}");

        return text.ToString().TrimEnd();
    }

    public static string Full(CliCommand command)
        => command.Summary + Environment.NewLine + Environment.NewLine + Usage(command);

    static string Signature(CliOption option)
        => option.IsFlag ? option.Name : $"{option.Name} <{option.ValueName}>";
}
