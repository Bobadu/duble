#nullable enable
using System.Collections.Generic;

namespace Duble.Core.Decisions;

/// <summary>What the user decided about one group. Absent means the comparison's own verdict stands.</summary>
public class Decision
{
    public string? Winner { get; set; }
    public List<string> Rejected { get; set; } = new();

    /// <summary>"Not a duplicate" — the group stays visible but nothing in it is ever moved.</summary>
    public bool Ignored { get; set; }

    public string? Note { get; set; }
}
