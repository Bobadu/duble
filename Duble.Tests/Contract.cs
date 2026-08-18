using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The interface's contract (web\src\bridge\contract.ts) read as text, so the C# side can be held to it.
///
/// The two sides of the bridge are never compiled together: C# builds its payloads by hand and TypeScript
/// declares what it expects. A name renamed on one side alone is a hole no compiler sees — in 1.0.0 the
/// calibration charts drew nothing because the interface read `od`, `do` and `kubelki` while the engine sent
/// `from`, `to` and `buckets`.
/// </summary>
public static class Contract
{
    static readonly string Text = File.ReadAllText(Path.Combine(TestPaths.Ui, "bridge", "contract.ts"));

    /// <summary>Every field named anywhere in the contract, interfaces and inline object types alike.</summary>
    static readonly HashSet<string> Fields = Regex
        .Matches(Text, @"[{;\n]\s*'?([a-zA-Z][a-zA-Z0-9]*)'?\??:")
        .Select(match => match.Groups[1].Value)
        .ToHashSet();

    /// <summary>
    /// Fields whose keys are data rather than names: the parameters of a sentence, the clothing slots of a
    /// source, the parts of the cache. The contract types them as Record&lt;string, …&gt;, so of those only the
    /// values are checked.
    /// </summary>
    static readonly HashSet<string> Dictionaries = new() { "parameters", "perSlot", "cache" };

    /// <summary>The keys of one `export interface X { … }` block.</summary>
    public static HashSet<string> Names(string block)
    {
        var body = Regex.Match(Text, @"export interface " + block + @" \{(.*?)\n\}", RegexOptions.Singleline);
        Assert.True(body.Success, "no " + block + " interface in contract.ts");

        return Regex.Matches(body.Groups[1].Value, @"^\s{2}'?([a-zA-Z][a-zA-Z0-9.]*)'?\s*[?:]", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToHashSet();
    }

    /// <summary>
    /// Every field of a payload has to be one the contract names. TestApp runs this over every response, so
    /// the ordinary command tests double as a check that the interface can read what they assert.
    /// </summary>
    public static void CheckFields(string where, JsonElement payload)
    {
        switch (payload.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var field in payload.EnumerateObject())
                {
                    Assert.True(Fields.Contains(field.Name), $"{where}: the contract does not name `{field.Name}`");

                    if (Dictionaries.Contains(field.Name))
                        foreach (var entry in field.Value.EnumerateObject()) CheckFields(where, entry.Value);
                    else
                        CheckFields(where, field.Value);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in payload.EnumerateArray()) CheckFields(where, item);
                break;
        }
    }
}
