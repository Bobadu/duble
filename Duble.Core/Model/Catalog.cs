using System;
using System.Collections.Generic;
using System.Linq;

namespace Duble.Core.Model;

/// <summary>Everything Duble has indexed: the garments, and where each pack came from.</summary>
public class Catalog
{
    /// <summary>3 = English keys, camelCase JSON, GameFormat as a string. Anything older loads as empty.</summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;
    public string? Built { get; set; }

    /// <summary>Pack name to the folder or archive it came from, so re-indexing needs no paths.</summary>
    public Dictionary<string, string> Sources { get; set; } = new();

    public List<Garment> Garments { get; set; } = new();

    /// <summary>Adds the garments, replacing any with the same id (a source indexed again).</summary>
    public void Upsert(IEnumerable<Garment> garments)
    {
        var byId = Garments.ToDictionary(g => g.Id!);
        foreach (var g in garments) byId[g.Id!] = g;
        Garments = byId.Values
            .OrderBy(g => g.PackName).ThenBy(g => g.Slot).ThenBy(g => g.Number)
            .ToList();
    }

    public void RemovePack(string pack)
        => Garments = Garments.Where(g => !string.Equals(g.PackName, pack, StringComparison.OrdinalIgnoreCase)).ToList();
}
