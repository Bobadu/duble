#nullable enable
using System.Linq;
using Duble.Core.Model;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// The identity of a garment, and the group id hashed from it, are a contract with the user's project file:
/// decisions are stored per group id. If these ever change shape, every decision made so far is orphaned.
/// These tests exist to make that break loud.
/// </summary>
public class GarmentIdTests
{
    [Fact]
    public void The_garment_id_is_pack_container_slot_number_suffix()
        => Assert.Equal("pack|civil01_female.rpf|jbib|7|u_1",
                        Garment.MakeId("pack", "civil01_female.rpf", "jbib", 7, "u_1"));

    [Fact]
    public void The_group_id_is_the_same_hash_whatever_the_order_of_its_members()
    {
        var ids = new[] { "a|k.rpf|jbib|1|u", "b|k.rpf|jbib|2|u" };
        Assert.Equal("AB13B2B818E2F50E", Grupa.PoliczId(ids));
        Assert.Equal(Grupa.PoliczId(ids), Grupa.PoliczId(ids.Reverse()));
    }

    [Fact]
    public void A_different_membership_gives_a_different_group_id()
        => Assert.NotEqual(Grupa.PoliczId(new[] { "a|k.rpf|jbib|1|u" }),
                           Grupa.PoliczId(new[] { "a|k.rpf|jbib|1|u", "b|k.rpf|jbib|2|u" }));
}
