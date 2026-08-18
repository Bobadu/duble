// Commands/LiveGroups.cs — the groups of the last comparison that are still real, with the user's decision
// applied. Three screens are built on this: Duplicates, the catalog card of one garment, and Apply.
//
// "Still real" means every member is in the current catalog: removing a source makes its groups disappear
// rather than leaving them pointing at garments that no longer exist.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.App.Commands;

/// <summary>A group, its members as they are in the catalog now, and who stays after the decision.</summary>
public sealed record LiveGroup(DuplicateGroup Group, List<Garment> Members, Resolution Resolution);

public sealed class LiveGroups
{
    /// <summary>Sharpest verdict first — the order the interface lists them in and the one used to pick the
    /// single verdict shown against a garment that is in several groups.</summary>
    static readonly Dictionary<Verdict, int> Sharpness = new()
    {
        [Verdict.Duplicate] = 0,
        [Verdict.Superset] = 1,
        [Verdict.NeedsReview] = 2,
        [Verdict.Retexture] = 3,
    };

    readonly Session session;
    readonly IResolutionService resolutions;

    public LiveGroups(Session session, IResolutionService resolutions)
    {
        this.session = session;
        this.resolutions = resolutions;
    }

    public static int Sharpest(Verdict verdict) => Sharpness.TryGetValue(verdict, out var order) ? order : 9;

    /// <summary>Every live group, sharpest verdict first, then the larger group, then by member id so that
    /// two runs over the same data list them in the same order.</summary>
    public List<LiveGroup> All()
    {
        var comparison = session.Comparison;
        if (comparison == null || session.Project == null) return new List<LiveGroup>();

        var byId = session.Catalog.Garments.ToDictionary(garment => garment.Id!);
        var live = new List<LiveGroup>();
        foreach (var group in comparison.Groups)
        {
            if (group.Members == null || group.Members.Count == 0 || !group.Members.All(byId.ContainsKey)) continue;
            // a result written before groups had ids still has to be addressable from the interface
            if (string.IsNullOrEmpty(group.Id)) group.Id = DuplicateGroup.ComputeId(group.Members);
            live.Add(new LiveGroup(group, group.Members.Select(id => byId[id]).ToList(), Resolve(group)));
        }

        return live
            .OrderBy(entry => Sharpest(entry.Group.Verdict))
            .ThenByDescending(entry => entry.Group.Members.Count)
            .ThenBy(entry => entry.Group.Members[0], StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>One group by id, or null — which the commands turn into not_found.</summary>
    public LiveGroup? Find(string id) => All().FirstOrDefault(entry => entry.Group.Id == id);

    /// <summary>Who stays and who goes: the rule from Core, with the user's decision on top of it.</summary>
    public Resolution Resolve(DuplicateGroup group)
        => resolutions.Resolve(group, session.Project?.Decisions.GetValueOrDefault(group.Id ?? ""));

    /// <summary>Everything that would be moved out: the rejected garments of every group but the ignored ones.</summary>
    public static HashSet<string> RejectedIds(IEnumerable<LiveGroup> groups)
        => new(groups.Where(entry => !entry.Resolution.Ignored).SelectMany(entry => entry.Resolution.Rejected));
}
