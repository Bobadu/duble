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
        _ = Project;
        var sources = args.Strings("zrodla");
        var slots = args.Strings("sloty");
        var formats = args.Strings("formaty");
        bool problemsOnly = args.Flag("problemy");
        bool inGroupOnly = args.Flag("wGrupie");
        var search = (args.Text("szukaj") ?? "").Trim().ToLowerInvariant();

        var groupsByGarment = GroupsByGarment();
        var all = Session.Catalog.Garments;

        var slotFilter = all.GroupBy(garment => garment.Slot)
            .Select(group => new { typ = group.Key, n = group.Count() })
            .OrderBy(entry => entry.typ)
            .ToList();

        var sourceFilter = all.GroupBy(garment => garment.SourceId ?? "")
            .Select(group => new { id = group.Key, nazwa = Project.Sources.Find(source => source.Id == group.Key)?.Name ?? group.Key, n = group.Count() })
            .OrderBy(entry => entry.nazwa)
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
                zrodloId = garment.SourceId,
                zrodlo = SourceName(garment),
                kontener = garment.Container,
                typ = garment.Slot,
                numer = garment.Number,
                sufiks = garment.Suffix,
                gen9 = garment.GameFormat == GameFormat.Enhanced,
                props = garment.IsProp,
                thumb = GarmentView.Thumbnail(garment),
                tekstur = garment.Textures.Count,
                bajty = garment.ModelSize + garment.Textures.Sum(texture => texture.Size),
                wArchiwum = GarmentView.IsInArchive(garment),
                bezMipow = withoutMipmaps,
                bc1Alfa = bc1WithAlpha,
                bc7 = bc7,
                grupa = verdict,
            });
        }

        return new
        {
            razem = all.Count,
            tekstury = all.Sum(garment => garment.Textures.Count),
            pokazane = listed.Count,
            filtry = new
            {
                sloty = slotFilter,
                zrodla = sourceFilter,
                formaty = new
                {
                    legacy = all.Count(garment => garment.GameFormat == GameFormat.Legacy),
                    gen9 = all.Count(garment => garment.GameFormat == GameFormat.Enhanced),
                },
            },
            pozycje = listed,
        };
    }

    object Item(JsonElement args)
    {
        _ = Project;
        var id = args.Required("id");
        var garment = Session.FindGarment(id) ?? throw new BridgeException(BridgeErrors.NotFound, id);

        var described = garments.Describe(garment, null, details: true, SourceName);
        described["zrodloSciezka"] = Session.SourceOf(garment)?.Path;

        var byId = Session.Catalog.Garments.ToDictionary(other => other.Id!);
        var inGroups = GroupsByGarment().GetValueOrDefault(id) ?? new List<LiveGroup>();

        return new
        {
            pozycja = described,
            grupy = inGroups.Select(live => new
            {
                id = live.Group.Id,
                werdykt = live.Group.Verdict.ToKey(),
                ignoruj = live.Resolution.Ignored,
                powod = GarmentView.ReasonJson(live.Group.Pairs.FirstOrDefault()?.Reason ?? live.Group.Reason),
                inni = live.Group.Members
                    .Where(other => other != id && byId.ContainsKey(other))
                    .Select(other => new
                    {
                        id = other,
                        nazwa = $"{byId[other].Slot}_{byId[other].Number:d3}",
                        sufiks = byId[other].Suffix,
                        zrodlo = SourceName(byId[other]),
                    }).ToList(),
                stan = State(live, id),
            }).ToList(),
        };
    }

    /// <summary>Where this garment stands in that group, as the interface's badge reads it.</summary>
    static string State(LiveGroup live, string garmentId)
        => live.Resolution.Ignored ? "ignoruj"
            : live.Resolution.Winner == garmentId && live.Resolution.Rejected.Count > 0 ? "zostaje"
            : live.Resolution.Rejected.Contains(garmentId) ? "odrzucona"
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
