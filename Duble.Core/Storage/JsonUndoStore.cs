#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duble.Core.Apply;
using Duble.Core.Results;

namespace Duble.Core.Storage;

/// <summary>Reads and writes the undo logs an apply leaves in the project's history folder.</summary>
public interface IUndoStore
{
    Result<UndoLog> Load(string path);

    Result Save(UndoLog log, string path);

    /// <summary>Every log in a folder, newest first. A file that will not parse is left out rather than thrown.</summary>
    IReadOnlyList<(string Path, UndoLog Log)> List(string folder);
}

/// <summary>
/// The log as indented JSON. Unlike the catalog, this one is not reproducible: it describes files that
/// physically moved, so a broken one is a failure the caller has to hear about.
/// </summary>
public sealed class JsonUndoStore : IUndoStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    public Result<UndoLog> Load(string path)
    {
        try
        {
            var log = JsonSerializer.Deserialize<UndoLog>(File.ReadAllText(path), Options) ?? new UndoLog();
            log.Moves ??= new();
            log.Garments ??= new();
            return Result<UndoLog>.Ok(log);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return Result<UndoLog>.Fail(ErrorCodes.ApplyIo, $"{path}: {e.Message}");
        }
    }

    public Result Save(UndoLog log, string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            // write beside it and move into place: a half-written log is worse than none
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(log, Options));
            File.Move(temporary, path, true);
            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail(ErrorCodes.ApplyIo, $"{path}: {e.Message}");
        }
    }

    public IReadOnlyList<(string Path, UndoLog Log)> List(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<(string, UndoLog)>();

        return Directory.EnumerateFiles(folder, "*.json")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Select(f => (Path: f, Loaded: Load(f)))
            .Where(x => x.Loaded.IsSuccess)
            .Select(x => (x.Path, x.Loaded.Value))
            .ToList();
    }
}
