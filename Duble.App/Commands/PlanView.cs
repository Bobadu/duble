// Commands/PlanView.cs — an apply plan as the interface reads it.
//
// Two screens ask for it: the Duplicates summary wants the totals only ("this many files would move"), the
// Apply dialog wants the list as well, because it shows every garment and where it would go.
using System.Collections.Generic;
using System.Linq;

namespace Duble.App.Commands;

public static class PlanView
{
    public static object Describe(Session session, ApplyPlan plan, bool withList)
    {
        var described = new Dictionary<string, object?>
        {
            ["garments"] = plan.Garments.Count,
            ["files"] = plan.Files,
            ["bytes"] = plan.Bytes,
            ["inArchive"] = plan.InArchiveCount,
            ["shared"] = plan.SharedCount,
            ["missing"] = plan.MissingCount,
            ["missingSources"] = plan.MissingSources,
            ["bin"] = session.Project?.Settings?.BinFolder,
            ["bins"] = plan.BinTotals().Select(bin => new { bin = bin.BinFolder, files = bin.Files, bytes = bin.Bytes }).ToList(),
        };

        if (withList)
        {
            var byId = session.Catalog.Garments.ToDictionary(garment => garment.Id!);
            described["list"] = plan.Garments.Select(garment => new
            {
                id = garment.Id,
                name = garment.Name,
                suffix = garment.Suffix,
                source = garment.SourceName,
                sourceId = garment.SourceId,
                container = garment.Container,
                bin = garment.BinFolder,
                thumbnail = byId.TryGetValue(garment.Id, out var indexed) ? GarmentView.Thumbnail(indexed) : null,
                files = garment.MoveCount,
                bytes = garment.Bytes,
                shared = garment.SharedCount,
                inArchive = garment.InArchiveCount,
                missing = garment.MissingCount,
            }).ToList();
        }

        return described;
    }
}
