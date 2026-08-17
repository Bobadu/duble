#nullable enable
namespace Duble.Core.Projects;

/// <summary>What a source actually is on disk.</summary>
public enum SourceKind
{
    /// <summary>A folder of files, possibly with .rpf archives inside it.</summary>
    Folder,

    /// <summary>A single .rpf archive, read-only.</summary>
    Archive,

    /// <summary>A FiveM resource: a folder with fxmanifest.lua, __resource.lua, resource.toml or a stream subfolder.</summary>
    FiveMResource,
}

/// <summary>The words the interface already uses for a source kind.</summary>
public static class SourceKinds
{
    public static string ToLabel(this SourceKind kind) => kind switch
    {
        SourceKind.Archive => "rpf",
        SourceKind.FiveMResource => "fivem",
        _ => "folder",
    };
}
