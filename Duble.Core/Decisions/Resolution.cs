#nullable enable
using System.Collections.Generic;

namespace Duble.Core.Decisions;

/// <summary>Who stays and who goes in one group, once the user's decision has been taken into account.</summary>
public sealed class Resolution
{
    public string? Winner { get; set; }
    public List<string> Rejected { get; set; } = new();

    /// <summary>"Not a duplicate": nothing here is ever moved.</summary>
    public bool Ignored { get; set; }

    /// <summary>
    /// True when this is exactly what the comparison proposed — either the user never touched the group, or
    /// they undid what they had done ("not a duplicate" and back again). A note or an ignore makes it false.
    /// </summary>
    public bool IsDefault { get; set; }

    public string? Note { get; set; }
}
