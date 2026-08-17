#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.Core.Comparison;

/// <summary>One pair of garments that a comparison had something to say about.</summary>
public class GarmentPair
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";

    public Verdict Verdict { get; set; }

    /// <summary>The code and its parameters; the sentence comes from Texts.Reason(reason, language).</summary>
    public Reason? Reason { get; set; }

    /// <summary>L1 distance between the two shape histograms; 0 means the same mesh.</summary>
    public double GeometryDistance { get; set; }

    /// <summary>Share of A's textures that were matched in B, and the other way round.</summary>
    public double CoverageA { get; set; }

    public double CoverageB { get; set; }

    public int SharedTextures { get; set; }
}

/// <summary>Garments that a comparison tied together, with the one it proposes keeping.</summary>
public class DuplicateGroup
{
    /// <summary>
    /// 16 hex characters of a SHA-256 over the sorted member ids. Stable, so a user's decision survives a
    /// re-comparison; frozen, because decisions in the project file hang off it.
    /// </summary>
    public string Id { get; set; } = "";

    public List<string> Members { get; set; } = new();

    public Verdict Verdict { get; set; }

    /// <summary>The garment proposed to stay: the highest quality score, ties broken by texture count then id.</summary>
    public string Winner { get; set; } = "";

    public Reason? Reason { get; set; }

    public List<GarmentPair> Pairs { get; set; } = new();

    /// <summary>Quality score per member, 0..100.</summary>
    public Dictionary<string, double> Scores { get; set; } = new();

    /// <summary>What each score is made of, so the interface can show why one copy won.</summary>
    public Dictionary<string, QualityScore> ScoreBreakdown { get; set; } = new();

    public static string ComputeId(IEnumerable<string> members)
    {
        var joined = string.Join("\n", members.OrderBy(x => x, StringComparer.Ordinal));
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash)[..16];
    }
}

/// <summary>Everything one comparison found.</summary>
public class ComparisonResult
{
    public string? Built { get; set; }

    public List<DuplicateGroup> Groups { get; set; } = new();

    /// <summary>How many groups carry each verdict.</summary>
    public Dictionary<Verdict, int> Counts { get; set; } = new();

    /// <summary>How many garments would be moved if every proposal were accepted as it stands.</summary>
    public int ProposedForRejection { get; set; }
}
