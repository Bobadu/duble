using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duble.Core.Projects;
using Duble.Core.Results;

namespace Duble.Core.Storage;

/// <summary>The .duble file as JSON: camelCase, enums as words, and indented — people open this one.</summary>
public sealed class JsonProjectStore : IProjectStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    public Result<Project> Load(string path)
    {
        if (!File.Exists(path))
            return Result<Project>.Fail(ErrorCodes.ProjectUnreadable, "no such project: " + path);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return Result<Project>.Fail(ErrorCodes.ProjectUnreadable, $"{path}: {e.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            int version = Version(root);
            if (version != Project.CurrentVersion)
                return Result<Project>.Fail(ErrorCodes.ProjectUnsupportedVersion,
                    $"{path}: project version {version}, this Duble writes version {Project.CurrentVersion}");

            Project? project;
            try
            {
                project = JsonSerializer.Deserialize<Project>(root.GetRawText(), Options);
            }
            catch (JsonException e)
            {
                return Result<Project>.Fail(ErrorCodes.ProjectUnreadable, $"{path}: {e.Message}");
            }

            if (project == null)
                return Result<Project>.Fail(ErrorCodes.ProjectUnreadable, path + ": empty file");

            project.Path = Path.GetFullPath(path);
            project.Sources ??= new();
            project.Decisions ??= new();
            project.Settings ??= new ProjectSettings();
            return Result<Project>.Ok(project);
        }
    }

    public Result Save(Project project)
    {
        if (string.IsNullOrEmpty(project.Path))
            return Result.Fail(ErrorCodes.ProjectUnreadable, "the project has no path");
        try
        {
            var folder = Path.GetDirectoryName(project.Path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            project.Version = Project.CurrentVersion;
            File.WriteAllText(project.Path, JsonSerializer.Serialize(project, Options));
            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail(ErrorCodes.ProjectUnreadable, $"{project.Path}: {e.Message}");
        }
    }

    /// <summary>A file without a version is from before Duble had one.</summary>
    static int Version(JsonElement root)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("version", out var v)
           && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 1;
}
