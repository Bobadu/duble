using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>catalog.list (filters, search, problems, membership of a group) and catalog.item (textures,
/// quality, groups) over a made-up catalog.</summary>
public class CatalogCommandTests
{
    [Fact]
    public async Task The_list_its_filters_and_the_card_of_one_garment()
    {
        using var app = new TestApp("catalog");
        app.NewProject("K");
        SampleData.SevenGarmentsInThreeSources(app.Session, app.Temp);
        app.Session.Compare(default, null);
        app.Session.Save();

        var list = await app.Call("catalog.list", "{}");
        Assert.Equal(7, list.GetProperty("total").GetInt32());
        Assert.Equal(7, list.GetProperty("shown").GetInt32());
        Assert.Equal(7, list.GetProperty("garments").GetArrayLength());
        Assert.Equal(3, list.GetProperty("filters").GetProperty("sources").GetArrayLength());
        Assert.Equal(3, list.GetProperty("filters").GetProperty("slots").GetArrayLength());
        Assert.Equal(7, list.GetProperty("filters").GetProperty("formats").GetProperty("legacy").GetInt32());

        var b = list.GetProperty("garments").EnumerateArray()
            .First(item => item.GetProperty("slot").GetString() == "jbib" && item.GetProperty("number").GetInt32() == 7);
        Assert.True(b.GetProperty("noMipmaps").GetBoolean());
        // the interface reads gen9 as a boolean; the format being an enum in Core must not leak as a number
        Assert.Equal(JsonValueKind.False, b.GetProperty("gen9").ValueKind);
        Assert.Equal(Verdict.Duplicate.ToKey(), b.GetProperty("verdict").GetString());

        var c = list.GetProperty("garments").EnumerateArray()
            .First(item => item.GetProperty("slot").GetString() == "lowr" && item.GetProperty("number").GetInt32() == 3);
        Assert.Equal(Verdict.Retexture.ToKey(), c.GetProperty("verdict").GetString());

        Assert.Equal(3, (await app.Call("catalog.list", "{\"slots\":[\"feet\"]}")).GetProperty("garments").GetArrayLength());
        Assert.Equal(3, (await app.Call("catalog.list", "{\"sources\":[\"z-p2\"]}")).GetProperty("garments").GetArrayLength());
        Assert.Equal(3, (await app.Call("catalog.list", "{\"problems\":true}")).GetProperty("garments").GetArrayLength());   // b, f and g have no mipmaps
        Assert.Equal(7, (await app.Call("catalog.list", "{\"inGroup\":true}")).GetProperty("garments").GetArrayLength());
        Assert.Equal(0, (await app.Call("catalog.list", "{\"formats\":[\"gen9\"]}")).GetProperty("garments").GetArrayLength());
        Assert.Equal(1, (await app.Call("catalog.list", "{\"search\":\"jbib_007\"}")).GetProperty("garments").GetArrayLength());

        // an ignored group does not count as being in a group
        var boots = app.Groups.All().First(live => live.Group.Members.Count == 3).Group;
        app.Session.Project.Decisions[boots.Id] = new Decision { Ignored = true };
        Assert.Equal(4, (await app.Call("catalog.list", "{\"inGroup\":true}")).GetProperty("garments").GetArrayLength());

        var item = await app.Call("catalog.item", $"{{\"id\":{JsonSerializer.Serialize(b.GetProperty("id").GetString())}}}");
        var garment = item.GetProperty("garment");
        Assert.Equal(2, garment.GetProperty("textures").GetArrayLength());
        Assert.True(garment.GetProperty("quality").GetProperty("total").GetDouble() >= 0);
        Assert.Equal("p2", garment.GetProperty("source").GetString());
        Assert.EndsWith("p2", garment.GetProperty("sourcePath").GetString());

        Assert.Equal(1, item.GetProperty("groups").GetArrayLength());
        var group = item.GetProperty("groups")[0];
        Assert.Equal(Verdict.Duplicate.ToKey(), group.GetProperty("verdict").GetString());
        Assert.Equal("rejected", group.GetProperty("standing").GetString());
        Assert.Equal(1, group.GetProperty("others").GetArrayLength());
        Assert.Equal("jbib_001", group.GetProperty("others")[0].GetProperty("name").GetString());

        var error = await app.Failing("catalog.item", "{\"id\":\"no-such-garment\"}");
        Assert.Equal(BridgeErrors.NotFound, error.GetProperty("code").GetString());
    }
}
