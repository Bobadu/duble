#nullable enable
namespace Duble.Core.Comparison;

/// <summary>
/// What Duble concluded about a pair or a group. The order matters: the interface sorts groups by it, so the
/// ones a user should act on come first.
/// </summary>
public enum Verdict
{
    /// <summary>Same model, same textures. Everything but the best copy is proposed for rejection.</summary>
    Duplicate,

    /// <summary>Same model, and one texture set contains the other. The smaller set is proposed for rejection.</summary>
    Superset,

    /// <summary>Similar model, or a partial texture overlap. Nothing is proposed; the user decides.</summary>
    NeedsReview,

    /// <summary>
    /// The same mesh with different textures. This is NOT a duplicate — in GTA packs it is the norm, and
    /// rejecting one takes a garment away. Never proposed for rejection.
    /// </summary>
    Retexture,
}

/// <summary>The key a verdict is looked up under in the dictionaries, and shown by in the interface.</summary>
public static class Verdicts
{
    /// <summary>Every verdict, in the order the interface and the report present them.</summary>
    public static readonly Verdict[] All =
    {
        Verdict.Duplicate, Verdict.Superset, Verdict.NeedsReview, Verdict.Retexture,
    };

    public static string ToKey(this Verdict verdict) => verdict switch
    {
        Verdict.Duplicate => "duplicate",
        Verdict.Superset => "superset",
        Verdict.NeedsReview => "needsReview",
        _ => "retexture",
    };
}
