// Commands/CatalogCommands.cs — catalog.list (every indexed garment, with filters) and catalog.item (the card
// of one garment: its textures, its quality, the groups it is in).
//
// The list goes over in one piece, without paging: the interface draws only the visible rows of a virtualised
// grid, and 5 000 garments come to well under a megabyte.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Duble.App.Commands;

public sealed class CatalogCommands : CommandModule
{
    readonly LiveGroups groups;
    readonly GarmentView garments;

    public CatalogCommands(Bridge bridge, Session session, LiveGroups groups, GarmentView garments)
        : base(bridge, session)
    {
        this.groups = groups;
        this.garments = garments;
    }

    public override void Register()
    {
        Bridge.Register("catalog.list", List);
        Bridge.Register("catalog.item", Item);
    }

    object List(JsonElement args)
    {
        RequireProject();
        var sources = args.Strings("sources");
        var slots = args.Strings("slots");
        var formats = args.Strings("formats");
        bool problemsOnly = args.Flag("problems");
        bool inGroupOnly = args.Flag("inGroup");
        var search = (args.Text("search") ?? "").Trim().ToLowerInvariant();

        var groupsByGarment = GroupsByGarment();
        var all = Session.Catalog.Garments;

        var slotFilter = all.GroupBy(garment => garment.Slot)
            .Select(group => new { slot = group.Key, n = group.Count() })
            .OrderBy(entry => entry.slot)
            .ToList();

        var sourceFilter = all.GroupBy(garment => garment.SourceId ?? "")
            .Select(group => new { id = group.Key, name = Project.Sources.Find(source => source.Id == group.Key)?.Name ?? group.Key, n = group.Count() })
            .OrderBy(entry => entry.name)
            .ToList();

        var listed = new List<object>();
        foreach (var garment in all)
        {
            bool withoutMipmaps = garment.Textures.Any(texture => texture.MipLevels <= 1);
            bool bc1WithAlpha = garment.Textures.Any(texture => texture.Format == "BC1" && texture.AlphaShare > 0.02f);
            bool bc7 = garment.Textures.Any(texture => texture.Format == "BC7");
            var verdict = SharpestVerdict(groupsByGarment.GetValueOrDefault(garment.Id!));

            if (sources.Count > 0 && !sources.Contains(garment.SourceId ?? "")) continue;
            if (slots.Count > 0 && !slots.Contains(garment.Slot ?? "")) continue;
            if (formats.Count > 0 && !formats.Contains(garment.GameFormat.ToLabel())) continue;
            if (problemsOnly && !(withoutMipmaps || bc1WithAlpha)) continue;
            if (inGroupOnly && verdict == null) continue;
            if (search.Length > 0 && !SearchText(garment).Contains(search)) continue;

            listed.Add(new
            {
                id = garment.Id,
                sourceId = garment.SourceId,
                source = SourceName(garment),
                container = garment.Container,
                slot = garment.Slot,
                number = garment.Number,
                suffix = garment.Suffix,
                gen9 = garment.GameFormat == GameFormat.Enhanced,
                prop = garment.IsProp,
                thumbnail = GarmentView.Thumbnail(garment),
                textureCount = garment.Textures.Count,
                bytes = garment.ModelSize + garment.Textures.Sum(texture => texture.Size),
                inArchive = GarmentView.IsInArchive(garment),
                noMipmaps = withoutMipmaps,
                bc1WithAlpha = bc1WithAlpha,
                bc7 = bc7,
                verdict,
            });
        }

        return new
        {
            total = all.Count,
            textures = all.Sum(garment => garment.Textures.Count),
            shown = listed.Count,
            filters = new
            {
                slots = slotFilter,
                sources = sourceFilter,
                formats = new
                {
                    legacy = all.Count(garment => garment.GameFormat == GameFormat.Legacy),
                    gen9 = all.Count(garment => garment.GameFormat == GameFormat.Enhanced),
                },
            },
            garments = listed,
        };
    }

    object Item(JsonElement args)
    {
        RequireProject();
        var id = args.Required("id");
        var garment = Session.FindGarment(id) ?? throw new BridgeException(BridgeErrors.NotFound, id);

        var described = garments.Describe(garment, null, details: true, SourceName);
        described["sourcePath"] = Session.SourceOf(garment)?.Path;

        var byId = Session.Catalog.Garments.ToDictionary(other => other.Id!);
        var inGroups = GroupsByGarment().GetValueOrDefault(id) ?? new List<LiveGroup>();

        return new
        {
            garment = described,
            groups = inGroups.Select(live => new
            {
                id = live.Group.Id,
                verdict = live.Group.Verdict.ToKey(),
                ignored = live.Resolution.Ignored,
                reason = GarmentView.ReasonJson(live.Group.Pairs.FirstOrDefault()?.Reason ?? live.Group.Reason),
                others = live.Group.Members
                    .Where(other => other != id && byId.ContainsKey(other))
                    .Select(other => new
                    {
                        id = other,
                        name = $"{byId[other].Slot}_{byId[other].Number:d3}",
                        suffix = byId[other].Suffix,
                        source = SourceName(byId[other]),
                    }).ToList(),
                standing = Standing(live, id),
            }).ToList(),
        };
    }

    /// <summary>Where this garment stands in that group, as the interface's badge reads it.</summary>
    static string Standing(LiveGroup live, string garmentId)
        => live.Resolution.Ignored ? "ignored"
            : live.Resolution.Winner == garmentId && live.Resolution.Rejected.Count > 0 ? "stays"
            : live.Resolution.Rejected.Contains(garmentId) ? "rejected"
            : "neutral";

    Dictionary<string, List<LiveGroup>> GroupsByGarment()
    {
        var byGarment = new Dictionary<string, List<LiveGroup>>();
        foreach (var live in groups.All())
            foreach (var member in live.Members)
            {
                if (!byGarment.TryGetValue(member.Id!, out var list)) byGarment[member.Id!] = list = new List<LiveGroup>();
                list.Add(live);
            }
        return byGarment;
    }

    /// <summary>The interface shows one verdict per garment, so a garment in several groups gets the sharpest
    /// of them. Ignored groups do not count: the user has said they are not duplicates.</summary>
    static string? SharpestVerdict(List<LiveGroup>? inGroups)
        => inGroups?.Where(live => !live.Resolution.Ignored)
            .Select(live => live.Group.Verdict)
            .OrderBy(LiveGroups.Sharpest)
            .Select(verdict => verdict.ToKey())
            .FirstOrDefault();
}
