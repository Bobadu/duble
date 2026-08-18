using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Duble.Core.Comparison;
using Duble.Core.Decisions;
using Duble.Core.Model;
using Duble.Core.Sources;

namespace Duble.Core.Reporting;

/// <summary>
/// Renders one report. A writer is built per report and thrown away with it, so two reports never share
/// thumbnails, counters or anything else.
/// </summary>
sealed class ReportWriter
{
    /// <summary>Side of a thumbnail in pixels. The tiles are 110 px, so this is a little sharper than needed.</summary>
    const int ThumbnailSide = 96;

    /// <summary>Texture rows drawn per group. The rest are still matched — only the pictures are left out.</summary>
    const int MaxTextureRows = 12;

    /// <summary>Textures shown in a member's "only here" strip.</summary>
    const int MaxUniqueTextures = 8;

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    readonly Catalog catalog;
    readonly ComparisonResult result;
    readonly ReportThumbnails thumbnails;
    readonly string language;
    readonly string? title;
    readonly Action<string> log;

    readonly Dictionary<string, Garment> byId;
    readonly List<DuplicateGroup> groups;
    readonly Dictionary<DuplicateGroup, Resolution> resolutions;

    public ReportWriter(Catalog catalog, ComparisonResult result, ReportOptions options,
                        IArchiveCache archives, IResolutionService resolutionService)
    {
        this.catalog = catalog;
        this.result = result;
        thumbnails = new ReportThumbnails(archives, ThumbnailSide);
        language = options.Language;
        title = options.Title;
        log = options.Log ?? (_ => { });

        var resolve = options.Resolve ?? (group => resolutionService.Resolve(group, null));
        byId = catalog.Garments.ToDictionary(garment => garment.Id!);

        // A group whose members are no longer in the catalog cannot be drawn at all. The verdict order is the
        // order of the enum: the ones a user should act on first come first.
        groups = result.Groups
            .Where(group => group.Members.All(byId.ContainsKey))
            .OrderBy(group => (int)group.Verdict)
            .ThenByDescending(group => group.Members.Count)
            .ToList();
        resolutions = groups.ToDictionary(group => group, resolve);
    }

    public string Render()
    {
        var (rejectedCount, reclaimableBytes) = Totals();

        var page = new StringBuilder();
        page.Append(Head(rejectedCount, reclaimableBytes));

        int done = 0;
        foreach (var group in groups)
        {
            page.Append(Group(group));
            if (++done % 10 == 0) log($"  groups: {done}/{groups.Count}");
        }

        page.Append($"""
        </main>
        <footer>
          <p>{Html("report.footer")}</p>
        </footer>
        <script>
        {ReportAssets.Script}
        </script>
        </body>
        </html>
        """);

        log($"  groups in the report: {groups.Count}, thumbnails: {thumbnails.Rendered}"
            + (thumbnails.Undecodable > 0 ? $", no preview: {thumbnails.Undecodable}" : "")
            + (thumbnails.MissingFiles > 0 ? $", FILE NOT FOUND: {thumbnails.MissingFiles}" : ""));

        return page.ToString();
    }

    /// <summary>How many garments would move, and how many bytes that frees, under the current decisions.</summary>
    (int Rejected, long Bytes) Totals()
    {
        int rejected = 0;
        long bytes = 0;
        foreach (var group in groups)
        {
            var resolution = resolutions[group];
            if (resolution.Ignored) continue;
            foreach (var id in resolution.Rejected.Where(byId.ContainsKey))
            {
                rejected++;
                bytes += byId[id].ModelSize + byId[id].Textures.Sum(texture => texture.Size);
            }
        }
        return (rejected, bytes);
    }

    // ===================== one group =====================

    string Group(DuplicateGroup group)
    {
        var resolution = resolutions[group];
        var winner = resolution.Winner ?? group.Winner;

        // the winner first, then by score — a reader should see what stays before what goes
        var members = group.Members
            .OrderByDescending(id => id == winner ? 1 : 0)
            .ThenByDescending(id => group.Scores.TryGetValue(id, out var score) ? score : 0)
            .ToList();
        var reference = byId[members[0]];

        var html = new StringBuilder();
        var searchable = string.Join(" ", members.Select(id => byId[id].Label)).ToLowerInvariant();
        html.Append($"<article class=\"group {VerdictClass(group.Verdict)}\" "
                    + $"data-verdict=\"{E(group.Verdict.ToKey())}\" data-search=\"{E(searchable)}\">");

        html.Append(GroupHead(group, members, resolution));
        html.Append(Panels(group, members, resolution, winner));
        html.Append(Textures(group, members, reference, resolution));

        html.Append("</article>");
        return html.ToString();
    }

    string GroupHead(DuplicateGroup group, List<string> members, Resolution resolution)
    {
        var html = new StringBuilder();
        html.Append("<header class=\"group-head\">");
        html.Append($"<span class=\"badge {VerdictClass(group.Verdict)}\">{E(Texts.Verdict(group.Verdict, language))}</span>");

        if (resolution.Ignored)
            html.Append($" <span class=\"badge v-other\">{Html("report.badge.notDuplicate")}</span>");
        else if (!resolution.IsDefault)
            html.Append($" <span class=\"badge v-other\">{Html("report.badge.yourDecision")}</span>");

        var names = members.Select(id =>
            $"<span class=\"name\">{E(byId[id].Label)}<sub>{E(byId[id].Suffix)}</sub></span>");
        html.Append($"<h2>{string.Join(" <span class=\"equals\">=</span> ", names)}</h2>");

        var reason = group.Pairs.FirstOrDefault()?.Reason ?? group.Reason;
        html.Append($"<p class=\"reason\">{E(Texts.Reason(reason, language))}</p>");
        if (!string.IsNullOrWhiteSpace(resolution.Note))
            html.Append($"<p class=\"reason note\">{Html("report.note")}: {E(resolution.Note)}</p>");

        html.Append("</header>");
        return html.ToString();
    }

    string Panels(DuplicateGroup group, List<string> members, Resolution resolution, string winner)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"panels\">");

        foreach (var id in members)
        {
            var garment = byId[id];
            bool stays = !resolution.Ignored && id == winner && resolution.Rejected.Count > 0;
            bool rejected = !resolution.Ignored && resolution.Rejected.Contains(id);
            string state = stays ? "winner" : rejected ? "rejected" : "";

            html.Append($"<section class=\"panel {state}\">");
            html.Append("<div class=\"panel-head\">");
            html.Append($"<span class=\"pack\">{E(garment.PackName)}</span>");
            if (stays) html.Append($"<span class=\"state winner\">{Html("report.badge.stays")}</span>");
            else if (rejected) html.Append($"<span class=\"state rejected\">{Html("report.badge.rejected")}</span>");
            html.Append("</div>");

            html.Append($"<div class=\"item-name\">{E(garment.Slot)}_{garment.Number:d3}<sub>{E(garment.Suffix)}</sub></div>");

            if (group.Scores.TryGetValue(id, out var score))
            {
                html.Append($"<div class=\"score\"><b>{score.ToString("F0", Invariant)}</b>"
                            + $"<span>{Html("report.qualityPoints")}</span></div>");
                if (group.ScoreBreakdown.TryGetValue(id, out var breakdown))
                    html.Append($"<div class=\"breakdown\">{E(breakdown.Text(language))}</div>");
            }

            html.Append(Tags(garment));
            html.Append("</section>");
        }

        html.Append("</div>");
        return html.ToString();
    }

    string Tags(Garment garment)
    {
        var html = new StringBuilder();
        html.Append("<ul class=\"tags\">");
        html.Append($"<li>{Html("report.textureCount", ("n", garment.Textures.Count))}</li>");

        // the median texture by area says more about a garment than the largest one does
        if (garment.Textures.Count > 0)
        {
            var median = garment.Textures
                .OrderBy(texture => (long)texture.Width * texture.Height)
                .ElementAt(garment.Textures.Count / 2);
            html.Append($"<li>{median.Width}×{median.Height}</li>");
        }

        html.Append($"<li>{(garment.Geometry?.Triangles ?? 0).ToString("N0", Invariant)} tri</li>");
        html.Append($"<li>LOD {garment.Geometry?.LodLevels ?? 0}</li>");

        int withoutMips = garment.Textures.Count(texture => texture.MipLevels <= 1);
        if (withoutMips > 0) html.Append($"<li class=\"warn\">{Html("report.noMipsCount", ("n", withoutMips))}</li>");

        html.Append("</ul>");
        return html.ToString();
    }

    // ===================== the textures, side by side =====================

    string Textures(DuplicateGroup group, List<string> members, Garment reference, Resolution resolution)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"textures\">");
        html.Append($"<h3>{Html("report.texturesSideBySide")}</h3>");

        var matched = members.ToDictionary(id => id, _ => new HashSet<int>());
        int rows = 0, skipped = 0;

        html.Append($"<div class=\"grid\" style=\"--columns:{members.Count}\">");
        for (int i = 0; i < reference.Textures.Count; i++)
        {
            var texture = reference.Textures[i];

            // The matching runs for EVERY reference texture, even once the rows stop being drawn — otherwise
            // the ones left out would show up falsely in the "only here" strips below.
            matched[members[0]].Add(i);
            var counterparts = MatchRow(members, texture, matched);

            if (rows >= MaxTextureRows) { skipped++; continue; }
            rows++;

            html.Append("<div class=\"row\">");
            html.Append(Tile(texture));
            for (int m = 1; m < members.Count; m++)
            {
                var other = byId[members[m]];
                html.Append(Tile(counterparts[m] >= 0 ? other.Textures[counterparts[m]] : null));
            }
            html.Append("</div>");
        }
        html.Append("</div>");

        if (skipped > 0)
            html.Append($"<p class=\"hint\">{Html("report.showingSome",
                ("n", MaxTextureRows), ("m", reference.Textures.Count), ("r", skipped))}</p>");

        html.Append(OnlyHere(members, matched, resolution));
        html.Append("</div>");
        return html.ToString();
    }

    /// <summary>
    /// For one reference texture, the index of its counterpart in every other member, or -1. Each counterpart
    /// is claimed at most once, so two members with the same texture twice do not match it to itself.
    /// </summary>
    int[] MatchRow(List<string> members, TextureInfo texture, Dictionary<string, HashSet<int>> matched)
    {
        var counterparts = new int[members.Count];
        for (int m = 1; m < members.Count; m++)
        {
            var other = byId[members[m]];
            counterparts[m] = -1;
            for (int k = 0; k < other.Textures.Count; k++)
            {
                if (matched[members[m]].Contains(k)) continue;
                if (DuplicateFinder.SameGraphic(texture, other.Textures[k])) { counterparts[m] = k; break; }
            }
            if (counterparts[m] >= 0) matched[members[m]].Add(counterparts[m]);
        }
        return counterparts;
    }

    /// <summary>The textures a member has that nobody else in the group does — what a rejection would cost.</summary>
    string OnlyHere(List<string> members, Dictionary<string, HashSet<int>> matched, Resolution resolution)
    {
        var html = new StringBuilder();
        foreach (var id in members)
        {
            var garment = byId[id];
            var unique = Enumerable.Range(0, garment.Textures.Count)
                .Where(k => !matched[id].Contains(k))
                .ToList();
            if (unique.Count == 0) continue;

            bool wouldLose = !resolution.Ignored && resolution.Rejected.Contains(id);
            html.Append($"<h4>{Html("report.onlyIn")} <em>{E(garment.Label)}</em> — "
                        + Html("report.textureCount", ("n", unique.Count))
                        + (wouldLose ? $" <span class=\"warn\">{Html("report.youWillLose")}</span>" : "")
                        + "</h4>");

            html.Append("<div class=\"strip\">");
            foreach (var k in unique.Take(MaxUniqueTextures)) html.Append(Tile(garment.Textures[k]));
            html.Append("</div>");

            if (unique.Count > MaxUniqueTextures)
                html.Append($"<p class=\"hint\">{Html("report.andMore", ("n", unique.Count - MaxUniqueTextures))}</p>");
        }
        return html.ToString();
    }

    /// <summary>One texture tile; null draws the empty slot that means "no counterpart here".</summary>
    string Tile(TextureInfo? texture)
    {
        if (texture == null)
            return $"<div class=\"tile empty\"><div class=\"placeholder\">"
                   + Html("report.noCounterpart").Replace(" ", "<br>")
                   + "</div></div>";

        var uri = thumbnails.DataUri(texture);
        var picture = uri != null
            ? $"<img src=\"{uri}\" alt=\"{E(texture.FileName)}\" loading=\"lazy\" width=\"{ThumbnailSide}\" height=\"{ThumbnailSide}\">"
            : $"<div class=\"placeholder\">{E(texture.Format)}<br>{Html("report.noPreview")}</div>";

        var tags = new List<string> { $"{texture.Width}×{texture.Height}", E(texture.Format) };
        if (texture.MipLevels <= 1) tags.Add($"<span class=\"warn\">{Html("report.noMips")}</span>");
        if (texture.Format == "BC1" && texture.AlphaShare > 0.02f) tags.Add($"<span class=\"warn\">{Html("report.bc1Alpha")}</span>");

        return $"""
            <div class="tile">
              {picture}
              <div class="label" title="{E(texture.FileName)}">{E(texture.FileName)}</div>
              <div class="meta">{string.Join(" · ", tags)}</div>
            </div>
            """;
    }

    // ===================== the document around it all =====================

    string Head(int rejectedCount, long reclaimableBytes)
    {
        int Count(Verdict verdict) => groups.Count(group => group.Verdict == verdict);

        var heading = string.IsNullOrWhiteSpace(title)
            ? Text("report.title")
            : Text("report.titleProject", ("name", title));

        var filters = string.Join("\n    ", Verdicts.All.Select(verdict =>
            $"<button data-filter=\"{verdict.ToKey()}\" aria-pressed=\"false\">"
            + $"{E(Texts.Verdict(verdict, language))} {Count(verdict)}</button>"));

        return $"""
        <!doctype html>
        <html lang="{Texts.Tag(language)}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{E(heading)}</title>
        <style>
        {ReportAssets.Style}
        </style>
        </head>
        <body>
        <header class="page">
          <h1>{E(heading)}</h1>
          <div class="chips">
            <span class="chip">{Html("report.chip.items")} <b>{catalog.Garments.Count}</b></span>
            <span class="chip">{Html("report.chip.textures")} <b>{catalog.Garments.Sum(g => g.Textures.Count)}</b></span>
            <span class="chip">{Html("report.chip.groupsShown")} <b id="counter">0</b></span>
            <span class="chip">{Html("report.chip.toReject")} <b>{rejectedCount}</b></span>
            <span class="chip">{Html("report.chip.reclaimable")} <b>{(reclaimableBytes / 1024.0 / 1024.0).ToString("F1", Invariant)} MB</b></span>
            <span class="chip">{Html("report.chip.compared")} <b>{E(result.Built ?? "")}</b></span>
          </div>
          <div class="chips">
            {filters}
          </div>
          <input id="search" type="search" placeholder="{Html("report.search")}">
          <button id="theme">{Html("report.theme")}</button>
        </header>
        <main>
        """;
    }

    static string VerdictClass(Verdict verdict) => verdict switch
    {
        Verdict.Duplicate => "v-duplicate",
        Verdict.Superset => "v-superset",
        Verdict.NeedsReview => "v-review",
        Verdict.Retexture => "v-retexture",
        _ => "v-other",
    };

    /// <summary>The text under a key, in the report's language.</summary>
    string Text(string key, params (string name, object? value)[] parameters)
    {
        if (parameters.Length == 0) return Texts.T(language, key);
        var values = parameters.ToDictionary(p => p.name, p => Convert.ToString(p.value, Invariant) ?? "");
        return Texts.T(language, key, values);
    }

    /// <summary>The same, ready to be dropped into the page.</summary>
    string Html(string key, params (string name, object? value)[] parameters) => E(Text(key, parameters));

    static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
}
