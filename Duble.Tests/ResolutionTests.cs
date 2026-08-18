using System.Collections.Generic;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// Turning a group and the user's decision about it into "who stays, who goes" — including what happens to
/// those decisions when a re-comparison splits a group up.
/// </summary>
public class ResolutionTests
{
    static readonly IResolutionService Rules = new ResolutionService();

    static DuplicateGroup Group(Verdict verdict, string winner, params string[] members)
        => new() { Id = "g", Verdict = verdict, Winner = winner, Members = new List<string>(members) };

    /// <summary>The same, with the real group id, which is what a decision is filed under.</summary>
    static DuplicateGroup Identified(Verdict verdict, string winner, params string[] members)
    {
        var group = Group(verdict, winner, members);
        group.Id = DuplicateGroup.ComputeId(group.Members);
        return group;
    }

    [Fact]
    public void Without_a_decision_a_duplicate_rejects_the_losers_and_a_retexture_rejects_nobody()
    {
        var resolution = Rules.Resolve(Group(Verdict.Duplicate, "a", "a", "b", "c"), null);
        Assert.True(resolution.IsDefault);
        Assert.Equal("a", resolution.Winner);
        Assert.Equal(new[] { "b", "c" }, resolution.Rejected);
        Assert.False(resolution.Ignored);

        Assert.Equal(new[] { "a" }, Rules.Resolve(Group(Verdict.Superset, "b", "a", "b"), null).Rejected);
        Assert.Empty(Rules.Resolve(Group(Verdict.Retexture, "a", "a", "b"), null).Rejected);
        Assert.Empty(Rules.Resolve(Group(Verdict.NeedsReview, "a", "a", "b"), null).Rejected);
    }

    [Fact]
    public void A_decision_is_final_even_when_it_rejects_nobody()
    {
        var group = Group(Verdict.Duplicate, "a", "a", "b", "c");

        var resolution = Rules.Resolve(group, new Decision { Winner = "b", Rejected = { "a" } });
        Assert.False(resolution.IsDefault);
        Assert.Equal("b", resolution.Winner);
        Assert.Equal(new[] { "a" }, resolution.Rejected);   // c stays, although it did not win

        resolution = Rules.Resolve(group, new Decision { Rejected = new List<string>() });
        Assert.Equal("a", resolution.Winner);
        Assert.Empty(resolution.Rejected);

        resolution = Rules.Resolve(group, new Decision { Ignored = true, Note = "to inne buty" });
        Assert.True(resolution.Ignored);
        Assert.Empty(resolution.Rejected);
        Assert.Equal("to inne buty", resolution.Note);

        // the winner cannot also be rejected, and nothing outside the group can be
        resolution = Rules.Resolve(group, new Decision { Winner = "a", Rejected = { "a", "x", "b" } });
        Assert.Equal(new[] { "b" }, resolution.Rejected);
    }

    [Fact]
    public void A_decision_that_matches_the_proposal_counts_as_no_decision_again()
    {
        var group = Group(Verdict.Duplicate, "a", "a", "b", "c");

        // "not a duplicate" is a decision; taking it back leaves the entry but restores the proposal
        var decision = new Decision { Winner = "a", Rejected = { "b", "c" }, Ignored = true };
        Assert.False(Rules.Resolve(group, decision).IsDefault);

        decision.Ignored = false;
        Assert.True(Rules.Resolve(group, decision).IsDefault);

        decision.Note = "sprawdzic pozniej";        // a note on its own is still the user speaking
        Assert.False(Rules.Resolve(group, decision).IsDefault);

        decision.Note = null;
        decision.Rejected = new List<string> { "b" };   // c kept by hand
        Assert.False(Rules.Resolve(group, decision).IsDefault);

        // needs-review proposes nobody, so an empty list is what the proposal already said
        var review = Group(Verdict.NeedsReview, "a", "a", "b");
        Assert.True(Rules.Resolve(review, new Decision { Winner = "a", Rejected = new List<string>() }).IsDefault);
        Assert.False(Rules.Resolve(review, new Decision { Winner = "b" }).IsDefault);
    }

    [Fact]
    public void A_decision_carries_over_to_a_subgroup_a_later_comparison_produced()
    {
        var abc = Identified(Verdict.Duplicate, "a", "a", "b", "c");
        var xy = Identified(Verdict.Duplicate, "x", "x", "y");
        var decisions = new Dictionary<string, Decision>
        {
            [abc.Id] = new Decision { Winner = "a", Rejected = { "b" }, Note = "c zostaje" },   // c kept by hand
            [xy.Id] = new Decision { Ignored = true, Note = "inne buty" },
        };

        // after an apply, b is gone and {a,c} is what is left; {x,w} is not a subgroup of {x,y}, so it inherits
        // nothing, while {y,x} hashes to the same id as {x,y} and therefore already has its decision
        var ac = Identified(Verdict.Duplicate, "a", "a", "c");
        var xw = Identified(Verdict.Duplicate, "x", "x", "w");
        var yx = Identified(Verdict.Duplicate, "y", "y", "x");

        Assert.Equal(1, Rules.CarryOver(decisions, new[] { abc, xy }, new[] { ac, xw, yx }));

        var carried = decisions[ac.Id];
        Assert.Equal("a", carried.Winner);
        Assert.Empty(carried.Rejected);
        Assert.Equal("c zostaje", carried.Note);
        Assert.False(Rules.Resolve(ac, carried).IsDefault);
        Assert.Empty(Rules.Resolve(ac, carried).Rejected);   // c does NOT go back to being proposed for rejection

        Assert.False(decisions.ContainsKey(xw.Id));
        Assert.True(decisions.ContainsKey(yx.Id));           // the same id as xy
    }

    [Fact]
    public void An_ignored_group_stays_ignored_and_a_winner_outside_the_subgroup_is_dropped()
    {
        var whole = Identified(Verdict.Duplicate, "d", "a", "b", "c", "d");
        var part = Identified(Verdict.Duplicate, "b", "b", "c");
        var decisions = new Dictionary<string, Decision>
        {
            [whole.Id] = new Decision { Winner = "d", Rejected = { "a", "b" }, Ignored = true },
        };

        Assert.Equal(1, Rules.CarryOver(decisions, new[] { whole }, new[] { part }));
        Assert.True(decisions[part.Id].Ignored);
        Assert.Null(decisions[part.Id].Winner);              // d is not in the subgroup, so the group's own winner stands
        Assert.Equal(new[] { "b" }, decisions[part.Id].Rejected);
    }

    [Fact]
    public void Nothing_to_carry_over_carries_nothing()
    {
        var abc = Identified(Verdict.Duplicate, "a", "a", "b", "c");
        var ac = Identified(Verdict.Duplicate, "a", "a", "c");

        Assert.Equal(0, Rules.CarryOver(new Dictionary<string, Decision>(), new[] { abc }, new[] { ac }));
        Assert.Equal(0, Rules.CarryOver(new Dictionary<string, Decision> { [abc.Id] = new Decision() }, null, new[] { ac }));
    }
}
