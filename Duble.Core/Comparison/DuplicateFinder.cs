// ==================== WHERE THE THRESHOLDS CAME FROM ====================
// Calibration over 1132 garments and 9437 textures (15.08.2026, `duble kalibruj`):
//
//   TEXTURES, Hamming distance over 256 bits
//     files identical byte for byte ....... 0        (n=443)
//     colour variants of one garment ...... median 26, p05 = 2
//     random pairs ........................ p01 = 92, median 128
//   At a threshold of 24, 400 000 random pairs leave 29 — and on inspection MOST of those are real duplicates
//   (the same graphic under another name). A threshold of 20 is therefore 4.6x below the first percentile of
//   random pairs: safe with room to spare.
//
//   COLOUR, mean per-channel difference (0..255) over an 8x8 grid
//     identical files ..................... 0
//     colour variants ..................... median 13.7  (but p01 = 0.08!)
//     random pairs ........................ p01 = 3.05
//   Colour alone is NOT enough (two black garments sit close together) and the hash alone is not either
//   (colour variants have p05 = 2). It is the CONJUNCTION of the two that decides.
//
//   GEOMETRY, L1 distance between shape histograms
//     the same mesh ....................... 0
//     the nearest foreign mesh ............ p05 = 0.112, median 0.254
//   BUT: the histogram on its own MISLEADS — hand_000 (3560 triangles) and hand_025 (2480) are 0.007 apart,
//   because every glove has a similar outline. So "identical geometry" ALSO requires an equal triangle and
//   vertex count. Without that condition, deduplication would delete different gloves.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Duble.Core.Fingerprints;
using Duble.Core.Model;
using Duble.Core.Time;
using Microsoft.Extensions.Logging;

namespace Duble.Core.Comparison;

/// <summary>Compares every garment in a catalog with every other one and groups what it finds.</summary>
public interface IDuplicateFinder
{
    ComparisonResult Find(Catalog catalog, Thresholds? thresholds = null,
                          IProgress<ProgressReport>? progress = null, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class DuplicateFinder : IDuplicateFinder
{
    readonly IQualityScorer scorer;
    readonly IClock clock;
    readonly ILogger<DuplicateFinder> log;

    public DuplicateFinder(IQualityScorer scorer, IClock clock, ILogger<DuplicateFinder> log)
    {
        this.scorer = scorer;
        this.clock = clock;
        this.log = log;
    }

    public ComparisonResult Find(Catalog catalog, Thresholds? thresholds = null,
                                 IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        thresholds ??= Thresholds.Default;
        var garments = catalog.Garments
            .Where(g => g.Id != null && g.Geometry?.ShapeHistogram != null && g.Geometry.Vertices > 0)
            .ToList();
        log.LogInformation("comparing {Count} garments", garments.Count);

        var pairs = new List<GarmentPair>();
        int candidates = 0;
        for (int i = 0; i < garments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if ((i + 1) % 50 == 0 || i + 1 == garments.Count) progress?.Report(new ProgressReport("compare", i + 1, garments.Count, null));
            for (int j = i + 1; j < garments.Count; j++)
            {
                var a = garments[i]; var b = garments[j];
                var match = MatchGeometry(a, b, thresholds, out double dist);
                if (match == GeometryMatch.None) continue;
                candidates++;
                var pair = Judge(a, b, match, dist, thresholds);
                if (pair != null) pairs.Add(pair);
            }
        }
        log.LogInformation("{Candidates} pairs passed the geometry filter, {Pairs} got a verdict", candidates, pairs.Count);

        // ===== grouping =====
        // Only DUPLICATE verdicts join garments together. Joining on "needs review" as well would run
        // everything into one enormous group and leave the report useless.
        var byId = garments.ToDictionary(g => g.Id!);
        var parent = garments.ToDictionary(g => g.Id!, g => g.Id!);
        string Root(string id) { while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; } return id; }
        void Join(string x, string y) { var rx = Root(x); var ry = Root(y); if (rx != ry) parent[rx] = ry; }

        bool IsDuplicate(GarmentPair p) => p.Verdict == Verdict.Duplicate || p.Verdict == Verdict.Superset;
        foreach (var pair in pairs.Where(IsDuplicate)) Join(pair.A, pair.B);

        var result = new ComparisonResult { Built = clock.Stamp() };

        foreach (var joined in pairs.Where(IsDuplicate).GroupBy(p => Root(p.A)))
        {
            var ids = joined.SelectMany(p => new[] { p.A, p.B }).Distinct().ToList();
            var group = new DuplicateGroup
            {
                Id = DuplicateGroup.ComputeId(ids),
                Members = ids,
                Pairs = joined.ToList(),
                // one superset pair makes the whole group a superset: the sets are not equal after all
                Verdict = joined.Any(p => p.Verdict == Verdict.Superset) ? Verdict.Superset : Verdict.Duplicate,
            };
            Score(group, byId);

            group.Winner = BestOf(ids, group, byId);
            var losers = ids.Where(id => id != group.Winner).Select(id => Points(group.Scores[id]));
            group.Reason = new Reason("WINNER",
                ("winner", Points(group.Scores[group.Winner])),
                ("losers", string.Join(", ", losers)));

            result.Groups.Add(group);
        }

        // pairs to look at and retextures stand alone: they say something about two garments, not about a set
        foreach (var pair in pairs.Where(p => p.Verdict == Verdict.NeedsReview || p.Verdict == Verdict.Retexture))
        {
            var group = new DuplicateGroup
            {
                Id = DuplicateGroup.ComputeId(new[] { pair.A, pair.B }),
                Members = new List<string> { pair.A, pair.B },
                Pairs = new List<GarmentPair> { pair },
                Verdict = pair.Verdict,
                Reason = pair.Reason,
            };
            Score(group, byId);
            group.Winner = BestOf(group.Members, group, byId);
            result.Groups.Add(group);
        }

        foreach (var verdict in Verdicts.All)
        {
            int count = result.Groups.Count(g => g.Verdict == verdict);
            if (count > 0) result.Counts[verdict] = count;
        }
        result.ProposedForRejection = result.Groups
            .Where(g => g.Verdict == Verdict.Duplicate || g.Verdict == Verdict.Superset)
            .Sum(g => g.Members.Count - 1);

        log.LogInformation("{Groups} groups, {Rejected} garments proposed for rejection",
            result.Groups.Count, result.ProposedForRejection);

        return result;
    }

    /// <summary>
    /// The copy that stays: the best score, ties going to the one with more colour variants and then to the
    /// lower id. That last key is what makes the answer depend on the garments alone — whole packs of identical
    /// boots score the same to the last decimal, and without it the winner would follow whatever order the
    /// catalog happened to be in.
    /// </summary>
    static string BestOf(IEnumerable<string> ids, DuplicateGroup group, Dictionary<string, Garment> byId)
        => ids.OrderByDescending(id => group.Scores[id])
              .ThenByDescending(id => byId[id].Textures.Count)
              .ThenBy(id => id, StringComparer.Ordinal)
              .First();

    /// <summary>Rates every member of a group and stores both the total and what it is made of.</summary>
    void Score(DuplicateGroup group, Dictionary<string, Garment> byId)
    {
        foreach (var id in group.Members)
        {
            var score = scorer.Score(byId[id]);
            group.Scores[id] = score.Total;
            group.ScoreBreakdown[id] = score;
        }
    }

    /// <summary>A quality score as it appears in a sentence: whole points, never a local decimal comma.</summary>
    static string Points(double score) => score.ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>How close two meshes are, as far as the geometry stage can tell.</summary>
    enum GeometryMatch
    {
        /// <summary>Not even a candidate — the pair goes no further.</summary>
        None,

        /// <summary>The same mesh: an equal position hash, or a histogram distance under the threshold with equal counts.</summary>
        Identical,

        /// <summary>Close enough that comparing textures is worth the time.</summary>
        Similar,
    }

    static GeometryMatch MatchGeometry(Garment a, Garment b, Thresholds thresholds, out double dist)
    {
        dist = double.MaxValue;
        // Slots do not have to agree: a garment from one pack turns up in another under a different slot
        // (accs_007 was jbib_015 in ours). Props and clothing, on the other hand, are never mixed.
        if (a.IsProp != b.IsProp) return GeometryMatch.None;

        if (a.Geometry!.PositionHash != null && a.Geometry.PositionHash == b.Geometry!.PositionHash)
        {
            dist = 0;
            return GeometryMatch.Identical;
        }

        dist = Distance.ShapeHistogram(a.Geometry.ShapeHistogram, b.Geometry!.ShapeHistogram);
        if (dist > thresholds.GeometrySimilar) return GeometryMatch.None;

        if (dist <= thresholds.GeometryIdentical
            && a.Geometry.Triangles == b.Geometry.Triangles
            && a.Geometry.Vertices == b.Geometry.Vertices
            && a.Geometry.Triangles > 0) return GeometryMatch.Identical;

        double maxTriangles = Math.Max(a.Geometry.Triangles, b.Geometry.Triangles);
        if (maxTriangles < 1) return GeometryMatch.None;
        double triangleDifference = Math.Abs(a.Geometry.Triangles - b.Geometry.Triangles) / maxTriangles;
        if (triangleDifference > thresholds.GeometryTriangleTolerance) return GeometryMatch.None;
        if (Distance.BoundingBox(a.Geometry.BoundingBox, b.Geometry.BoundingBox) > thresholds.GeometryBoundsTolerance)
            return GeometryMatch.None;
        return GeometryMatch.Similar;
    }

    /// <summary>
    /// Whether two textures are the same graphic — the same colour of it, not merely the same pattern. The
    /// report uses this too, to line the textures of a group up next to each other.
    /// </summary>
    public static bool SameGraphic(TextureInfo x, TextureInfo y, Thresholds? thresholds = null)
    {
        thresholds ??= Thresholds.Default;
        if (x.Sha256 == y.Sha256) return true;
        if (!x.IsDecoded || !y.IsDecoded) return false;

        double colour = Distance.Color(x.ColorSignature, y.ColorSignature);

        // A flat texture — one solid colour, say — has a perceptual hash made of noise. Then only the colour
        // can be trusted, and it has to agree more closely than it otherwise would.
        if (x.Variance < thresholds.FlatTextureVariance || y.Variance < thresholds.FlatTextureVariance)
            return colour <= thresholds.FlatTextureColorDistance;

        int hash = Distance.Hamming(x.PerceptualHash, y.PerceptualHash);
        return hash >= 0 && hash <= thresholds.TextureHashDistance && colour <= thresholds.TextureColorDistance;
    }

    GarmentPair? Judge(Garment a, Garment b, GeometryMatch match, double dist, Thresholds thresholds)
    {
        var texturesA = a.Textures;
        var texturesB = b.Textures;
        var pair = new GarmentPair { A = a.Id!, B = b.Id!, GeometryDistance = dist };

        if (texturesA.Count == 0 || texturesB.Count == 0)
        {
            pair.Verdict = Verdict.NeedsReview;
            pair.Reason = new Reason("NO_TEXTURES",
                ("geo", match == GeometryMatch.Identical ? "@geo.identical" : "@geo.similar"));
            return pair;
        }

        // Greedy matching, each texture on the B side claimed at most once: two garments that both repeat one
        // texture must not have it counted twice.
        var claimed = new bool[texturesB.Count];
        int matchedA = 0;
        foreach (var texture in texturesA)
            for (int k = 0; k < texturesB.Count; k++)
            {
                if (claimed[k]) continue;
                if (SameGraphic(texture, texturesB[k], thresholds)) { claimed[k] = true; matchedA++; break; }
            }
        int matchedB = claimed.Count(taken => taken);

        pair.SharedTextures = matchedA;
        pair.CoverageA = (double)matchedA / texturesA.Count;
        pair.CoverageB = (double)matchedB / texturesB.Count;

        double bestCoverage = Math.Max(pair.CoverageA, pair.CoverageB);
        bool coversA = pair.CoverageA >= thresholds.FullCoverage;
        bool coversB = pair.CoverageB >= thresholds.FullCoverage;

        // every reason carries the same counts: how many textures are shared, out of how many, on each side
        (string, object)[] Shared(params (string, object)[] extra)
        {
            var parameters = new List<(string, object)>
            {
                ("a", matchedA), ("na", texturesA.Count), ("b", matchedB), ("nb", texturesB.Count),
            };
            parameters.AddRange(extra);
            return parameters.ToArray();
        }
        string distance = dist.ToString("F3", CultureInfo.InvariantCulture);

        if (match == GeometryMatch.Identical)
        {
            if (coversA && coversB)
            {
                pair.Verdict = Verdict.Duplicate;
                pair.Reason = new Reason("SAME_MODEL_SAME_TEX", Shared());
            }
            else if (coversA || coversB)
            {
                pair.Verdict = Verdict.Superset;
                pair.Reason = new Reason("SAME_MODEL_SUBSET", Shared());
            }
            else if (bestCoverage >= thresholds.PartialCoverage)
            {
                pair.Verdict = Verdict.NeedsReview;
                pair.Reason = new Reason("SAME_MODEL_PARTIAL", Shared());
            }
            else
            {
                // THE SAME MESH WITH DIFFERENT TEXTURES IS A RETEXTURE, not a duplicate. In GTA packs that is
                // the norm, and rejecting one takes a garment away from the wardrobe.
                pair.Verdict = Verdict.Retexture;
                pair.Reason = new Reason("SAME_MODEL_OTHER_TEX", Shared());
            }
        }
        else
        {
            if (coversA && coversB)
            {
                pair.Verdict = Verdict.NeedsReview;
                pair.Reason = new Reason("SIMILAR_MODEL_SAME_TEX", Shared(("dist", distance)));
            }
            else if (bestCoverage >= thresholds.PartialCoverage)
            {
                pair.Verdict = Verdict.NeedsReview;
                pair.Reason = new Reason("SIMILAR_MODEL_PARTIAL", Shared(("dist", distance)));
            }
            else return null;    // a similar model with different textures is simply a different garment
        }

        return pair;
    }
}
