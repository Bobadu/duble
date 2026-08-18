using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.Cli.CommandLine;

/// <summary>What was typed after the command name, checked against what the command says it accepts.</summary>
public sealed class ParsedArguments
{
    readonly Dictionary<string, string?> options;

    ParsedArguments(IReadOnlyList<string> positional, Dictionary<string, string?> options)
    {
        Positional = positional;
        this.options = options;
    }

    /// <summary>The arguments that are not options: sources, file names.</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>The value of an option, or <paramref name="fallback" /> when it was not given.</summary>
    public string? Value(string name, string? fallback = null)
        => options.TryGetValue(name, out var value) && value != null ? value : fallback;

    public bool Flag(string name) => options.ContainsKey(name);

    /// <summary>
    /// Splits the arguments according to what the command accepts. A misspelt option is an error rather than
    /// something quietly ignored — that used to leave the command running against a default the user thought
    /// they had overridden.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string> arguments, IReadOnlyList<CliOption> accepted,
                                out ParsedArguments parsed, out string error)
    {
        var byName = accepted.ToDictionary(option => option.Name, StringComparer.Ordinal);
        var positional = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        parsed = new ParsedArguments(positional, options);
        error = "";

        for (int i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (!argument.StartsWith('-')) { positional.Add(argument); continue; }

            if (!byName.TryGetValue(argument, out var option))
            {
                error = $"unknown option {argument}";
                return false;
            }

            if (option.IsFlag) { options[option.Name] = null; continue; }

            if (i + 1 >= arguments.Count)
            {
                error = $"{argument} needs a value ({option.ValueName})";
                return false;
            }

            options[option.Name] = arguments[++i];
        }

        return true;
    }
}
