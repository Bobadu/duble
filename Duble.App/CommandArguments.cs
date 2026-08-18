// CommandArguments.cs — reading the `args` object of a bridge request.
//
// The interface may send anything, including nothing at all (`"args": null`), so every reader here checks the
// kind of the value and falls back rather than throwing. The one exception is Required, which is how a command
// says "without this there is nothing to do" and turns a missing argument into a bad_args answer.
using System.Collections.Generic;
using System.Text.Json;

namespace Duble.App;

public static class CommandArguments
{
    /// <summary>A string argument, or null when it was not sent.</summary>
    public static string? Text(this JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    /// <summary>A string argument the command cannot work without.</summary>
    public static string Required(this JsonElement args, string name)
        => args.Text(name) ?? throw new BridgeException(BridgeErrors.BadArguments, "missing argument: " + name);

    /// <summary>A boolean argument, or <paramref name="fallback"/> when it was not sent.</summary>
    public static bool Flag(this JsonElement args, string name, bool fallback = false)
        => args.OptionalFlag(name) ?? fallback;

    /// <summary>A boolean argument, or null when it was not sent — for settings where "unchanged" is a third state.</summary>
    public static bool? OptionalFlag(this JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value)
           && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : null;

    /// <summary>An array of strings; an empty list when it was not sent. Non-string entries are dropped.</summary>
    public static List<string> Strings(this JsonElement args, string name)
    {
        var values = new List<string>();
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var entry in array.EnumerateArray())
                if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { } text) values.Add(text);
        return values;
    }

    /// <summary>Whether the argument was sent at all, whatever its value: "clear this" is not "leave it alone".</summary>
    public static bool Has(this JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out _);

    /// <summary>Whether an array was sent at all — an empty one means something different from none.</summary>
    public static bool HasArray(this JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array;

    /// <summary>An object argument, for the nested ones (thresholds); default when it was not sent.</summary>
    public static JsonElement Object(this JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : default;
}
