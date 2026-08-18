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

    /// <summary>
    /// Adds the garments, replacing any with the same id (a source indexed again).
    ///
    /// The order is TOTAL, and has to be. Sorting by pack, slot and number alone leaves two garments that
    /// share a number and differ only in suffix — feet_050_u and feet_050_u_1 — in whatever order the
    /// dictionary happened to enumerate them, and .NET randomises string hashing per process, so that order
    /// changed from one run to the next. The comparison walks this list to build its pairs, so the same
    /// catalog produced pairs with A and B the other way round, and coverageA and coverageB swapped with
    /// them. The id is unique, which is what makes the last key here enough.
    /// </summary>
    public void Upsert(IEnumerable<Garment> garments)
    {
        var byId = Garments.ToDictionary(g => g.Id!);
        foreach (var g in garments) byId[g.Id!] = g;
        Garments = byId.Values
            .OrderBy(g => g.PackName, StringComparer.Ordinal)
            .ThenBy(g => g.Slot, StringComparer.Ordinal)
            .ThenBy(g => g.Number)
            .ThenBy(g => g.Id, StringComparer.Ordinal)
            .ToList();
    }

    public void RemovePack(string pack)
        => Garments = Garments.Where(g => !string.Equals(g.PackName, pack, StringComparison.OrdinalIgnoreCase)).ToList();
}
