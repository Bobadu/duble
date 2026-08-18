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
            ["pozycje"] = plan.Garments.Count,
            ["pliki"] = plan.Files,
            ["bajty"] = plan.Bytes,
            ["wArchiwum"] = plan.InArchiveCount,
            ["wspoldzielone"] = plan.SharedCount,
            ["brakujace"] = plan.MissingCount,
            ["brakujaceZrodla"] = plan.MissingSources,
            ["kosz"] = session.Project?.Settings?.BinFolder,
            ["kosze"] = plan.BinTotals().Select(bin => new { kosz = bin.BinFolder, pliki = bin.Files, bajty = bin.Bytes }).ToList(),
        };

        if (withList)
        {
            var byId = session.Catalog.Garments.ToDictionary(garment => garment.Id!);
            described["lista"] = plan.Garments.Select(garment => new
            {
                id = garment.Id,
                nazwa = garment.Name,
                sufiks = garment.Suffix,
                zrodlo = garment.SourceName,
                zrodloId = garment.SourceId,
                kontener = garment.Container,
                kosz = garment.BinFolder,
                thumb = byId.TryGetValue(garment.Id, out var indexed) ? GarmentView.Thumbnail(indexed) : null,
                pliki = garment.MoveCount,
                bajty = garment.Bytes,
                wspoldzielone = garment.SharedCount,
                wArchiwum = garment.InArchiveCount,
                brakujace = garment.MissingCount,
            }).ToList();
        }

        return described;
    }
}
