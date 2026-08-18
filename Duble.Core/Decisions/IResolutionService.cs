using System.Collections.Generic;
using Duble.Core.Comparison;

namespace Duble.Core.Decisions;

/// <summary>Turns a group plus the user's decision into who stays and who goes.</summary>
public interface IResolutionService
{
    /// <summary>
    /// No decision gives the comparison's own proposal: for a duplicate or a superset the winner stays and the
    /// rest are rejected; for "needs review" and "retexture" nobody is rejected. A decision is authoritative —
    /// including an empty rejection list, which means "reject nobody".
    /// </summary>
    Resolution Resolve(DuplicateGroup group, Decision? decision);

    /// <summary>
    /// After a re-comparison groups change membership and therefore change id, which would leave the user's
    /// decision under a dead key and quietly return the new, smaller group to "reject all but the winner". A
    /// new group without a decision of its own inherits the one from the smallest old group it is a subset of:
    /// the winner (if still present), the rejected members they have in common, the ignore flag and the note.
    /// Returns how many decisions were carried over.
    /// </summary>
    int CarryOver(IDictionary<string, Decision> decisions,
                  IEnumerable<DuplicateGroup> previous, IEnumerable<DuplicateGroup> current);
}
