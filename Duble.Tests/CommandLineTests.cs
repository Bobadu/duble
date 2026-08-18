#nullable enable
using System;
using System.Linq;
using Duble.Cli.CommandLine;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// Parsing what was typed. The point of declaring a command's options is that a misspelt one is an error
/// rather than something quietly ignored — before this, `--katlog x` left the command running against the
/// default while the user believed they had overridden it.
/// </summary>
public class CommandLineTests
{
    static readonly CliOption Catalog = CliOption.Value("--catalog", "file", "the catalog");
    static readonly CliOption Force = CliOption.Flag("--force", "do it all again");
    static readonly CliOption[] Accepted = { Catalog, Force };

    static ParsedArguments Parse(params string[] arguments)
    {
        Assert.True(ParsedArguments.TryParse(arguments, Accepted, out var parsed, out var error), error);
        return parsed;
    }

    [Fact]
    public void Positionals_options_and_flags_are_told_apart()
    {
        var parsed = Parse("first", "--catalog", "c.json", "second", "--force");

        Assert.Equal(new[] { "first", "second" }, parsed.Positional);
        Assert.Equal("c.json", parsed.Value(Catalog.Name));
        Assert.True(parsed.Flag(Force.Name));
    }

    [Fact]
    public void An_option_that_was_not_given_falls_back()
    {
        var parsed = Parse("source");

        Assert.Equal("default.json", parsed.Value(Catalog.Name, "default.json"));
        Assert.Null(parsed.Value(Catalog.Name));
        Assert.False(parsed.Flag(Force.Name));
    }

    [Fact]
    public void A_misspelt_option_is_refused_rather_than_ignored()
    {
        Assert.False(ParsedArguments.TryParse(new[] { "--katlog", "c.json" }, Accepted, out _, out var error));
        Assert.Contains("--katlog", error);
    }

    [Fact]
    public void An_option_at_the_end_with_no_value_is_refused()
    {
        Assert.False(ParsedArguments.TryParse(new[] { "--catalog" }, Accepted, out _, out var error));
        Assert.Contains("--catalog", error);
        Assert.Contains("file", error);   // says what the missing value is
    }

    [Fact]
    public void A_flag_does_not_swallow_the_argument_after_it()
    {
        var parsed = Parse("--force", "source.rpf");

        Assert.True(parsed.Flag(Force.Name));
        Assert.Equal(new[] { "source.rpf" }, parsed.Positional);
    }

    [Fact]
    public void Every_command_has_a_name_a_summary_and_no_repeated_options()
    {
        Assert.NotEmpty(CommandRegistry.Commands);

        foreach (var command in CommandRegistry.Commands)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Name), "a command without a name");
            Assert.False(string.IsNullOrWhiteSpace(command.Summary), $"{command.Name} has no summary");

            var names = command.Options.Select(option => option.Name).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.All(command.Options, option => Assert.StartsWith("--", option.Name));
            Assert.All(command.Options, option => Assert.False(string.IsNullOrWhiteSpace(option.Description)));
        }
    }

    [Fact]
    public void Commands_are_found_by_name_whatever_the_case()
    {
        Assert.Equal("index", CommandRegistry.Find("INDEX")?.Name);
        Assert.Null(CommandRegistry.Find("indeks"));   // the Polish verb is gone, and says so rather than doing something
    }

    [Fact]
    public void Help_lists_every_command_and_every_option_of_one()
    {
        var overview = Help.Overview(CommandRegistry.Commands);
        foreach (var command in CommandRegistry.Commands)
            Assert.Contains(command.Name, overview);

        var index = CommandRegistry.Find("index")!;
        var usage = Help.Usage(index);
        Assert.Contains("usage: duble index <source>...", usage);
        foreach (var option in index.Options)
            Assert.Contains(option.Name, usage);
    }
}
