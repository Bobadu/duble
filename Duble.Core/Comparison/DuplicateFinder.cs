#nullable enable
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
    readonly ILogger<DuplicateFinder> log;

    public DuplicateFinder(IQualityScorer scorer, ILogger<DuplicateFinder> log)
    {
        this.scorer = scorer;
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

        // ===== grupowanie =====
        // Laczymy TYLKO po werdyktach duplikatu — gdybysmy laczyli po "do wgladu",
        // wszystko zlalo by sie w jedna wielka grupe i raport bylby bezuzyteczny.
        var byId = garments.ToDictionary(g => g.Id!);
        var parent = garments.ToDictionary(g => g.Id!, g => g.Id!);
        string Find(string x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(string x, string y) { var rx = Find(x); var ry = Find(y); if (rx != ry) parent[rx] = ry; }
        foreach (var p in pairs.Where(p => p.Verdict == Verdict.Duplicate || p.Verdict == Verdict.Superset)) Union(p.A, p.B);

        var result = new ComparisonResult { Built = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        var duplicateGroups = pairs.Where(p => p.Verdict == Verdict.Duplicate || p.Verdict == Verdict.Superset)
                             .GroupBy(p => Find(p.A));
        foreach (var g in duplicateGroups)
        {
            var ids = g.SelectMany(p => new[] { p.A, p.B }).Distinct().ToList();
            var group = new DuplicateGroup
            {
                Members = ids,
                Pairs = g.ToList(),
                Verdict = g.Any(p => p.Verdict == Verdict.Superset) ? Verdict.Superset : Verdict.Duplicate
            };
            group.Id = DuplicateGroup.ComputeId(ids);
            foreach (var id in ids)
            {
                var score = scorer.Score(byId[id]);
                group.Scores[id] = score.Total;
                group.ScoreBreakdown[id] = score;
            }
            group.Winner = ids.OrderByDescending(x => group.Scores[x])
                                 .ThenByDescending(x => byId[x].Textures.Count)
                                 .ThenBy(x => x, StringComparer.Ordinal).First();
            var losers = ids.Where(x => x != group.Winner).ToList();
            group.Reason = new Reason("WINNER",
                ("zw", group.Scores[group.Winner].ToString("F0", CultureInfo.InvariantCulture)),
                ("przegrani", string.Join(", ", losers.Select(x => group.Scores[x].ToString("F0", CultureInfo.InvariantCulture)))));
            result.Groups.Add(group);
        }

        // pairs do obejrzenia i przemalowania trafiaja jako grupy jednoparowe
        foreach (var p in pairs.Where(p => p.Verdict == Verdict.NeedsReview || p.Verdict == Verdict.Retexture))
        {
            var group = new DuplicateGroup { Members = new List<string> { p.A, p.B }, Pairs = new List<GarmentPair> { p }, Verdict = p.Verdict, Reason = p.Reason };
            group.Id = DuplicateGroup.ComputeId(group.Members);
            foreach (var id in group.Members) { var score = scorer.Score(byId[id]); group.Scores[id] = score.Total; group.ScoreBreakdown[id] = score; }
            group.Winner = group.Members.OrderByDescending(x => group.Scores[x]).First();
            result.Groups.Add(group);
        }

        foreach (var verdict in new[] { Verdict.Duplicate, Verdict.Superset, Verdict.NeedsReview, Verdict.Retexture })
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

    /// <summary>"identyczna" / "podobna" / null gdy pair w ogole nie jest kandydatem.</summary>
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

        double maxTri = Math.Max(a.Geometry.Triangles, b.Geometry.Triangles);
        if (maxTri < 1) return GeometryMatch.None;
        double roznicaTri = Math.Abs(a.Geometry.Triangles - b.Geometry.Triangles) / maxTri;
        if (roznicaTri > thresholds.GeometryTriangleTolerance) return GeometryMatch.None;
        if (Distance.BoundingBox(a.Geometry.BoundingBox, b.Geometry.BoundingBox) > thresholds.GeometryBoundsTolerance)
            return GeometryMatch.None;
        return GeometryMatch.Similar;
    }

    /// <summary>Czy dwie tekstury to ta sama grafika (ten sam kolor, nie tylko ten sam wzor).</summary>
    public static bool SameGraphic(TextureInfo x, TextureInfo y, Thresholds? thresholds = null)
    {
        thresholds ??= Thresholds.Default;
        if (x.Sha256 == y.Sha256) return true;
        if (!x.IsDecoded || !y.IsDecoded) return false;
        double kol = Distance.Color(x.ColorSignature, y.ColorSignature);
        // Plaska tekstura (np. jednolity kolor) daje PHash z szumu — wtedy ufamy samemu
        // kolorowi, ale wymagamy scislejszej zgodnosci.
        if (x.Variance < thresholds.FlatTextureVariance || y.Variance < thresholds.FlatTextureVariance)
            return kol <= thresholds.FlatTextureColorDistance;
        int ph = Distance.Hamming(x.PerceptualHash, y.PerceptualHash);
        return ph >= 0 && ph <= thresholds.TextureHashDistance && kol <= thresholds.TextureColorDistance;
    }

    GarmentPair? Judge(Garment a, Garment b, GeometryMatch match, double dist, Thresholds thresholds)
    {
        var ta = a.Textures ?? new List<TextureInfo>();
        var tb = b.Textures ?? new List<TextureInfo>();
        var pair = new GarmentPair { A = a.Id!, B = b.Id!, GeometryDistance = dist };

        if (ta.Count == 0 || tb.Count == 0)
        {
            pair.Verdict = Verdict.NeedsReview;
            pair.Reason = new Reason("NO_TEXTURES", ("geo", match == GeometryMatch.Identical ? "@geo.identyczna" : "@geo.podobna"));
            return pair;
        }

        var uzyteB = new bool[tb.Count];
        int dopasowaneA = 0;
        foreach (var x in ta)
        {
            for (int k = 0; k < tb.Count; k++)
            {
                if (uzyteB[k]) continue;
                if (SameGraphic(x, tb[k], thresholds)) { uzyteB[k] = true; dopasowaneA++; break; }
            }
        }
        int dopasowaneB = uzyteB.Count(v => v);
        pair.SharedTextures = dopasowaneA;
        pair.CoverageA = (double)dopasowaneA / ta.Count;
        pair.CoverageB = (double)dopasowaneB / tb.Count;
        double maxPokrycie = Math.Max(pair.CoverageA, pair.CoverageB);
        bool pelneA = pair.CoverageA >= thresholds.FullCoverage;
        bool pelneB = pair.CoverageB >= thresholds.FullCoverage;

        // parametry wspolne wszystkich powodow: ile tekstur wspolnych po obu stronach ({a}/{na} i {b}/{nb})
        (string, object)[] Tex(params (string, object)[] extra)
        {
            var w = new List<(string, object)> { ("a", dopasowaneA), ("na", ta.Count), ("b", dopasowaneB), ("nb", tb.Count) };
            w.AddRange(extra);
            return w.ToArray();
        }
        string distTekst = dist.ToString("F3", CultureInfo.InvariantCulture);

        if (match == GeometryMatch.Identical)
        {
            if (pelneA && pelneB)
            {
                pair.Verdict = Verdict.Duplicate;
                pair.Reason = new Reason("SAME_MODEL_SAME_TEX", Tex());
            }
            else if (pelneA || pelneB)
            {
                pair.Verdict = Verdict.Superset;
                pair.Reason = new Reason("SAME_MODEL_SUBSET", Tex());
            }
            else if (maxPokrycie >= thresholds.PartialCoverage)
            {
                pair.Verdict = Verdict.NeedsReview;
                pair.Reason = new Reason("SAME_MODEL_PARTIAL", Tex());
            }
            else
            {
                // TEN SAM MESH, INNE TEKSTURY = przemalowanie. To NIE jest duplikat —
                // w paczkach do GTA to norma i skasowanie takiej pozycji zabiera ciuch.
                pair.Verdict = Verdict.Retexture;
                pair.Reason = new Reason("SAME_MODEL_OTHER_TEX", Tex());
            }
        }
        else
        {
            if (pelneA && pelneB)
            {
                pair.Verdict = Verdict.NeedsReview;
                pair.Reason = new Reason("SIMILAR_MODEL_SAME_TEX", Tex(("dist", distTekst)));
            }
            else if (maxPokrycie >= thresholds.PartialCoverage)
            {
                pair.Verdict = Verdict.NeedsReview;
                pair.Reason = new Reason("SIMILAR_MODEL_PARTIAL", Tex(("dist", distTekst)));
            }
            else return null;    // podobny model + inne tekstury = po prostu inny ciuch
        }
        return pair;
    }

}
