#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duble.Core.Model;
using Duble.Core.Results;
using Duble.Core.Time;

namespace Duble.Core.Storage;

/// <summary>The catalog as one JSON file: camelCase keys, enums as strings, no indentation (it gets large).</summary>
public sealed class JsonCatalogStore : ICatalogStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };

    readonly IClock clock;

    public JsonCatalogStore(IClock clock) => this.clock = clock;

    public Catalog Load(string path)
    {
        if (!File.Exists(path)) return new Catalog();
        try
        {
            var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), Options);
            return catalog is null || catalog.Version != Catalog.CurrentVersion ? new Catalog() : catalog;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Catalog();
        }
    }

    public Result Save(Catalog catalog, string path)
    {
        catalog.Version = Catalog.CurrentVersion;
        catalog.Built = clock.Stamp();
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(catalog, Options));
            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail(ErrorCodes.CatalogUnwritable, $"{path}: {e.Message}");
        }
    }
}
