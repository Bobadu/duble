#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duble.Core.Comparison;
using Duble.Core.Results;

namespace Duble.Core.Storage;

/// <summary>Reads and writes the comparison result that sits in the project's cache folder.</summary>
public interface IComparisonStore
{
    /// <summary>
    /// The result at that path, or an empty one when the file is missing or unreadable. Like the catalog, it
    /// is reproducible — comparing again costs seconds — so an unreadable file is not an error.
    /// </summary>
    ComparisonResult Load(string path);

    Result Save(ComparisonResult result, string path);
}

/// <inheritdoc />
public sealed class JsonComparisonStore : IComparisonStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };

    public ComparisonResult Load(string path)
    {
        if (!File.Exists(path)) return new ComparisonResult();
        try
        {
            return JsonSerializer.Deserialize<ComparisonResult>(File.ReadAllText(path), Options) ?? new ComparisonResult();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ComparisonResult();
        }
    }

    public Result Save(ComparisonResult result, string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(result, Options));
            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail(ErrorCodes.CatalogUnwritable, $"{path}: {e.Message}");
        }
    }
}
