using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Duble.App;
using Duble.Core;
using Xunit;

namespace Duble.Tests;

/// <summary>The names on the two sides of the bridge, compared. See <see cref="Contract"/> for why.</summary>
public class ContractTests
{
    [Fact]
    public void The_contract_lists_exactly_the_commands_the_application_registers()
    {
        using var app = new TestApp("contract-commands");

        var registered = app.Bridge.Commands.ToHashSet(StringComparer.Ordinal);
        var declared = Contract.Names("Commands");

        Assert.Empty(declared.Except(registered));     // the interface would call a command nobody answers
        Assert.Empty(registered.Except(declared));     // a command no screen can reach
    }

    [Fact]
    public void The_contract_lists_exactly_the_events_the_application_raises()
    {
        var raised = Directory.EnumerateFiles(Path.Combine(TestPaths.Root, "Duble.App"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"(?:Event|raise)\(""([a-z][a-zA-Z0-9.]*)"""))
            .Select(match => match.Groups[1].Value)
            .ToHashSet();
        var declared = Contract.Names("Events");

        Assert.Empty(declared.Except(raised));
        Assert.Empty(raised.Except(declared));
    }

    /// <summary>
    /// A job reports itself straight from the runner rather than through a command, so its fields are checked
    /// here: the start, one progress report and the end of a job that failed.
    /// </summary>
    [Fact]
    public async Task A_job_event_carries_only_fields_the_contract_names()
    {
        var events = new List<JsonElement>();
        var jobs = new JobRunner((name, data) =>
            events.Add(JsonDocument.Parse(JsonSerializer.Serialize(new { name, data }, Bridge.Json)).RootElement));

        await jobs.Run(JobKinds.Index, "A", (_, progress) =>
        {
            progress(new ProgressReport("models", 5, 10, "A"));
            throw new IOException("disk");
        });

        Assert.Equal(3, events.Count);
        foreach (var raised in events)
        {
            Assert.Equal("job", raised.GetProperty("name").GetString());
            Contract.CheckFields("event job", raised.GetProperty("data"));
        }
    }
}
