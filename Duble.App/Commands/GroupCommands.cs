// Commands/GroupCommands.cs — compare.run and groups.list / get / decide / reset.
//
// The groups come from the last comparison (LiveGroups); who stays is Core's rule with the user's decision on
// top. Verdict reasons travel as codes with parameters, and the interface writes the sentence from i18n —
// Core's dictionary is merged into the interface's, so both halves speak the same language.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Duble.App.Commands;

public sealed class GroupCommands : CommandModule
{
    readonly JobRunner jobs;
    readonly LiveGroups groups;
    readonly GarmentView garments;
    readonly CatalogWorkflow workflow;

    public GroupCommands(Bridge bridge, Session session, JobRunner jobs, LiveGroups groups, GarmentView garments, CatalogWorkflow workflow)
        : base(bridge, session)
    {
        this.jobs = jobs;
        this.groups = groups;
        this.garments = garments;
        this.workflow = workflow;
    }

    public override void Register()
    {
        Bridge.Register("compare.run", _ => StartComparing());
        Bridge.Register("groups.list", List);
        Bridge.Register("groups.get", args => new { grupa = Describe(Required(args), details: true) });
        Bridge.Register("groups.decide", Decide);
        Bridge.Register("groups.reset", Reset);
    }

    object StartComparing()
    {
        bool started = jobs.TryStart(JobKinds.Compare, ProjectName, async (cancellation, progress) =>
        {
            await Task.Yield();
            workflow.CompareAndSave(cancellation, progress);
        });
        if (!started) throw Busy();
        return new { uruchomiono = true };
    }

    object List(JsonElement args)
    {
        RequireProject();
        var verdicts = args.Strings("werdykty");
        var slots = args.Strings("sloty");
        var sources = args.Strings("zrodla");
        var search = (args.Text("szukaj") ?? "").Trim().ToLowerInvariant();
        bool withIgnored = args.Flag("zignorowane");

        var live = groups.All();
        var comparison = Session.Comparison;

        var summary = new
        {
            grup = comparison == null ? (int?)null : live.Count,
            duplikat = live.Count(entry => entry.Group.Verdict == Verdict.Duplicate),
            nadzbior = live.Count(entry => entry.Group.Verdict == Verdict.Superset),
            wglad = live.Count(entry => entry.Group.Verdict == Verdict.NeedsReview),
            przemalowanie = live.Count(entry => entry.Group.Verdict == Verdict.Retexture),
            zignorowane = live.Count(entry => entry.Resolution.Ignored),
            porownano = comparison?.Built,
            doOdrzucenia = PlanView.Describe(Session, Session.Plan(LiveGroups.RejectedIds(live)), withList: false),
        };

        var slotFilter = live.SelectMany(entry => entry.Members.Select(garment => garment.Slot))
            .GroupBy(slot => slot)
            .Select(group => new { typ = group.Key, n = group.Count() })
            .OrderBy(entry => entry.typ)
            .ToList();

        var sourceFilter = live.SelectMany(entry => entry.Members.Select(garment => garment.SourceId ?? ""))
            .GroupBy(id => id)
            .Select(group => new { id = group.Key, nazwa = SourceNameById(group.Key), n = group.Count() })
            .OrderBy(entry => entry.nazwa)
            .ToList();

        var matching = live.Where(entry =>
                (withIgnored || !entry.Resolution.Ignored)
                && (verdicts.Count == 0 || verdicts.Contains(entry.Group.Verdict.ToKey()))
                && (slots.Count == 0 || entry.Members.Any(garment => slots.Contains(garment.Slot ?? "")))
                && (sources.Count == 0 || entry.Members.Any(garment => sources.Contains(garment.SourceId ?? "")))
                && (search.Length == 0 || entry.Members.Any(garment => SearchText(garment).Contains(search))))
            .Select(entry => Describe(entry, details: false))
            .ToList();

        return new { podsumowanie = summary, filtry = new { sloty = slotFilter, zrodla = sourceFilter }, grupy = matching };
    }

    object Decide(JsonElement args)
    {
        var project = Project;
        var live = Required(args);
        var id = live.Group.Id;
        var members = live.Group.Members;

        // the first change to a group writes down what Core would have chosen, so that later edits are
        // relative to a decision that exists rather than to a default that may move
        if (!project.Decisions.TryGetValue(id, out var decision))
        {
            var byDefault = groups.Resolve(live.Group);
            decision = new Decision { Winner = byDefault.Winner, Rejected = byDefault.Rejected.ToList() };
            project.Decisions[id] = decision;
        }

        var winner = args.Text("zwyciezca");
        bool rejectedGiven = args.HasArray("odrzucone");
        if (winner != null && members.Contains(winner))
        {
            decision.Winner = winner;
            // "keep this one" on its own means everything else goes
            if (!rejectedGiven) decision.Rejected = members.Where(member => member != winner).ToList();
        }
        if (rejectedGiven)
            decision.Rejected = args.Strings("odrzucone")
                .Where(member => members.Contains(member) && member != decision.Winner)
                .Distinct()
                .ToList();

        if (args.OptionalFlag("ignoruj") is { } ignored) decision.Ignored = ignored;
        if (args.Text("notatka") is { } note) decision.Note = note.Length == 0 ? null : note;

        Session.SaveProject();
        Changed(id);
        return new { rozstrzygniecie = GarmentView.ResolutionJson(groups.Resolve(live.Group)) };
    }

    object Reset(JsonElement args)
    {
        var live = Required(args);
        Project.Decisions.Remove(live.Group.Id);
        Session.SaveProject();
        Changed(live.Group.Id);
        return new { rozstrzygniecie = GarmentView.ResolutionJson(groups.Resolve(live.Group)) };
    }

    void Changed(string id)
    {
        Bridge.Event("groups.changed", new { id });
        ProjectChanged();
    }

    LiveGroup Required(JsonElement args)
    {
        var id = args.Required("id");
        return groups.Find(id) ?? throw new BridgeException(BridgeErrors.NotFound, id);
    }

    string SourceNameById(string id) => Project.Sources.Find(source => source.Id == id)?.Name ?? id;

    Dictionary<string, object?> Describe(LiveGroup live, bool details)
    {
        var (group, members, resolution) = live;
        var described = new Dictionary<string, object?>
        {
            ["id"] = group.Id,
            ["werdykt"] = group.Verdict.ToKey(),
            ["powod"] = GarmentView.ReasonJson(group.Pairs.FirstOrDefault()?.Reason ?? group.Reason),
            ["zwyciezca"] = group.Winner,
            ["rozstrzygniecie"] = GarmentView.ResolutionJson(resolution),
            ["czlonkowie"] = members.Select(member => garments.Describe(member, group, details, SourceName)).ToList(),
        };

        if (details)
        {
            described["pary"] = group.Pairs.Select(pair => new
            {
                a = pair.A,
                b = pair.B,
                werdykt = pair.Verdict.ToKey(),
                powod = GarmentView.ReasonJson(pair.Reason),
                distGeo = pair.GeometryDistance,
                pokrycieA = pair.CoverageA,
                pokrycieB = pair.CoverageB,
                wspolnychTekstur = pair.SharedTextures,
            }).ToList();
            described["dopasowania"] = MatchedTextures(members);
        }

        return described;
    }

    /// <summary>
    /// Which texture matches which, for every pair of members: the comparison screen draws a line between
    /// them. Each texture on the right is used once, so two textures of A never both point at one of B.
    /// </summary>
    List<object> MatchedTextures(List<Garment> members)
    {
        var thresholds = Session.Thresholds;
        var matches = new List<object>();

        for (int i = 0; i < members.Count; i++)
            for (int j = i + 1; j < members.Count; j++)
            {
                var pairs = new List<string?[]>();
                var used = new HashSet<int>();
                foreach (var left in members[i].Textures)
                    for (int k = 0; k < members[j].Textures.Count; k++)
                    {
                        if (used.Contains(k)) continue;
                        var right = members[j].Textures[k];
                        if (!DuplicateFinder.SameGraphic(left, right, thresholds)) continue;
                        used.Add(k);
                        pairs.Add(new[] { left.Sha256, right.Sha256 });
                        break;
                    }
                matches.Add(new { a = members[i].Id, b = members[j].Id, pary = pairs });
            }

        return matches;
    }
}
