#nullable enable
using System.Collections.Generic;
using System.Linq;
using Duble.Core.Comparison;

namespace Duble.Core.Decisions;

/// <inheritdoc />
public sealed class ResolutionService : IResolutionService
{
    public Resolution Resolve(Grupa group, Decision? decision)
    {
        var members = group.Pozycje ?? new List<string>();
        var defaultWinner = group.Zwyciezca ?? members.FirstOrDefault();
        var defaultRejected = group.Werdykt == Porownanie.Duplikat || group.Werdykt == Porownanie.Nadzbior
            ? members.Where(m => m != defaultWinner).ToList()
            : new List<string>();

        if (decision == null)
            return new Resolution { Winner = defaultWinner, Rejected = defaultRejected, IsDefault = true };

        var resolution = new Resolution { Winner = defaultWinner, Note = decision.Note, Ignored = decision.Ignored };
        if (!string.IsNullOrEmpty(decision.Winner) && members.Contains(decision.Winner))
            resolution.Winner = decision.Winner;
        if (!decision.Ignored && decision.Rejected != null)
            resolution.Rejected = decision.Rejected
                .Where(m => m != resolution.Winner && members.Contains(m))
                .Distinct().ToList();

        resolution.IsDefault = !resolution.Ignored
            && string.IsNullOrEmpty(resolution.Note)
            && resolution.Winner == defaultWinner
            && resolution.Rejected.Count == defaultRejected.Count
            && !resolution.Rejected.Except(defaultRejected).Any();
        return resolution;
    }

    public int CarryOver(IDictionary<string, Decision> decisions,
                         IEnumerable<Grupa> previous, IEnumerable<Grupa> current)
    {
        if (decisions == null || decisions.Count == 0 || previous == null || current == null) return 0;

        static string IdOf(Grupa g) => g.Id ?? Grupa.PoliczId(g.Pozycje ?? new List<string>());

        var decided = previous
            .Where(g => g.Pozycje is { Count: > 0 })
            .Select(g => (id: IdOf(g), members: new HashSet<string>(g.Pozycje)))
            .Where(g => decisions.ContainsKey(g.id))
            .ToList();
        if (decided.Count == 0) return 0;

        int carried = 0;
        foreach (var group in current)
        {
            if (group.Pozycje is not { Count: > 0 }) continue;
            var id = IdOf(group);
            if (decisions.ContainsKey(id)) continue;

            var smallestSuperset = decided
                .Where(old => old.id != id && group.Pozycje.All(old.members.Contains))
                .OrderBy(old => old.members.Count)
                .FirstOrDefault();
            if (smallestSuperset.id == null) continue;

            var source = decisions[smallestSuperset.id];
            decisions[id] = new Decision
            {
                Winner = source.Winner != null && group.Pozycje.Contains(source.Winner) ? source.Winner : null,
                Rejected = (source.Rejected ?? new List<string>()).Where(group.Pozycje.Contains).ToList(),
                Ignored = source.Ignored,
                Note = source.Note,
            };
            carried++;
        }
        return carried;
    }
}
