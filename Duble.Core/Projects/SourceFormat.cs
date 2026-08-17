#nullable enable
namespace Duble.Core.Projects;

/// <summary>Which game build a whole source turned out to hold, once it has been indexed.</summary>
public enum SourceFormat
{
    /// <summary>Not indexed yet, or nothing recognisable in it.</summary>
    Unknown,

    Legacy,
    Enhanced,

    /// <summary>Both, which a folder holding several .rpf archives can well be.</summary>
    Mixed,
}

/// <summary>The words the interface already uses for a source format; unknown stays absent rather than becoming a word.</summary>
public static class SourceFormats
{
    public static string? ToLabel(this SourceFormat format) => format switch
    {
        SourceFormat.Legacy => "legacy",
        SourceFormat.Enhanced => "gen9",
        SourceFormat.Mixed => "mieszany",
        _ => null,
    };
}
