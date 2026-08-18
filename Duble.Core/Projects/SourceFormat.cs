using System.Collections.Generic;
using System.Linq;
using Duble.Core.Model;

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

public static class SourceFormats
{
    /// <summary>
    /// The format of a source, from the garments indexing found in it. It lives here rather than at each call
    /// site because the same three-way question is asked wherever a source is described.
    /// </summary>
    public static SourceFormat Of(IEnumerable<Garment> garments)
    {
        bool any = false, legacy = false, enhanced = false;
        foreach (var garment in garments)
        {
            any = true;
            if (garment.GameFormat == GameFormat.Enhanced) enhanced = true; else legacy = true;
            if (legacy && enhanced) return SourceFormat.Mixed;
        }

        if (!any) return SourceFormat.Unknown;
        return enhanced ? SourceFormat.Enhanced : SourceFormat.Legacy;
    }

    /// <summary>
    /// The word the interface looks this format up by. Unknown has none: a source that has not been indexed
    /// shows nothing rather than a label saying so.
    /// </summary>
    public static string? ToLabel(this SourceFormat format) => format switch
    {
        SourceFormat.Legacy => "legacy",
        SourceFormat.Enhanced => "gen9",
        SourceFormat.Mixed => "mixed",
        _ => null,
    };
}
