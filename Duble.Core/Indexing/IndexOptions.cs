using System.Collections.Generic;
using Duble.Core.Model;

namespace Duble.Core.Indexing;

/// <summary>What indexing should do beyond reading the files.</summary>
public sealed class IndexOptions
{
    /// <summary>
    /// The previous catalog. A file whose path and change stamp are unchanged is taken from it instead of
    /// being read and fingerprinted again.
    /// </summary>
    public Catalog? PreviousCatalog { get; init; }

    /// <summary>Ignore <see cref="PreviousCatalog"/> and read everything.</summary>
    public bool Force { get; init; }

    /// <summary>When set, every decoded texture leaves a &lt;sha&gt;.png thumbnail here (128 px, alpha on a chequerboard).</summary>
    public string? ThumbnailFolder { get; init; }

    /// <summary>Files per parallel batch; between batches indexing reports progress and checks for cancellation.</summary>
    public int BatchSize { get; init; } = 200;
}

/// <summary>What one run of indexing produced.</summary>
/// <param name="Garments">The garments found, ordered by slot and number.</param>
/// <param name="SkippedFiles">
/// Files whose names do not follow the R* convention. Worth showing, but not a problem: they are usually the
/// leftovers of an export, and a leftover is exactly what a duplicate finder is looking for.
/// </param>
/// <param name="UnreadableFiles">
/// Files that ARE clothing by their name and could not be read — locked by the running game, or on a drive
/// that hiccupped. This is a different thing from a skipped file and has to be told apart from it: the catalog
/// that comes back is quietly incomplete, and a garment short of a texture scores lower, which can change
/// which copy of a duplicate the comparison proposes keeping.
/// </param>
/// <param name="ReusedModels">Models taken from the previous catalog rather than read again.</param>
/// <param name="ReusedTextures">Textures taken from the previous catalog rather than read again.</param>
public sealed record IndexReport(
    IReadOnlyList<Garment> Garments,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<string> UnreadableFiles,
    int ReusedModels,
    int ReusedTextures);
