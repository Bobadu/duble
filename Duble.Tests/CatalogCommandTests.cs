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
        Assert.Equal(7, list.GetProperty("razem").GetInt32());
        Assert.Equal(7, list.GetProperty("pokazane").GetInt32());
        Assert.Equal(7, list.GetProperty("pozycje").GetArrayLength());
        Assert.Equal(3, list.GetProperty("filtry").GetProperty("zrodla").GetArrayLength());
        Assert.Equal(3, list.GetProperty("filtry").GetProperty("sloty").GetArrayLength());
        Assert.Equal(7, list.GetProperty("filtry").GetProperty("formaty").GetProperty("legacy").GetInt32());

        var b = list.GetProperty("pozycje").EnumerateArray()
            .First(item => item.GetProperty("typ").GetString() == "jbib" && item.GetProperty("numer").GetInt32() == 7);
        Assert.True(b.GetProperty("bezMipow").GetBoolean());
        // the interface reads gen9 as a boolean; the format being an enum in Core must not leak as a number
        Assert.Equal(JsonValueKind.False, b.GetProperty("gen9").ValueKind);
        Assert.Equal(Verdict.Duplicate.ToKey(), b.GetProperty("grupa").GetString());

        var c = list.GetProperty("pozycje").EnumerateArray()
            .First(item => item.GetProperty("typ").GetString() == "lowr" && item.GetProperty("numer").GetInt32() == 3);
        Assert.Equal(Verdict.Retexture.ToKey(), c.GetProperty("grupa").GetString());

        Assert.Equal(3, (await app.Call("catalog.list", "{\"sloty\":[\"feet\"]}")).GetProperty("pozycje").GetArrayLength());
        Assert.Equal(3, (await app.Call("catalog.list", "{\"zrodla\":[\"z-p2\"]}")).GetProperty("pozycje").GetArrayLength());
        Assert.Equal(3, (await app.Call("catalog.list", "{\"problemy\":true}")).GetProperty("pozycje").GetArrayLength());   // b, f and g have no mipmaps
        Assert.Equal(7, (await app.Call("catalog.list", "{\"wGrupie\":true}")).GetProperty("pozycje").GetArrayLength());
        Assert.Equal(0, (await app.Call("catalog.list", "{\"formaty\":[\"gen9\"]}")).GetProperty("pozycje").GetArrayLength());
        Assert.Equal(1, (await app.Call("catalog.list", "{\"szukaj\":\"jbib_007\"}")).GetProperty("pozycje").GetArrayLength());

        // an ignored group does not count as being in a group
        var boots = app.Groups.All().First(live => live.Group.Members.Count == 3).Group;
        app.Session.Project.Decisions[boots.Id] = new Decision { Ignored = true };
        Assert.Equal(4, (await app.Call("catalog.list", "{\"wGrupie\":true}")).GetProperty("pozycje").GetArrayLength());

        var item = await app.Call("catalog.item", $"{{\"id\":{JsonSerializer.Serialize(b.GetProperty("id").GetString())}}}");
        var garment = item.GetProperty("pozycja");
        Assert.Equal(2, garment.GetProperty("tekstury").GetArrayLength());
        Assert.True(garment.GetProperty("rozpiska").GetProperty("razem").GetDouble() >= 0);
        Assert.Equal("p2", garment.GetProperty("zrodlo").GetString());
        Assert.EndsWith("p2", garment.GetProperty("zrodloSciezka").GetString());

        Assert.Equal(1, item.GetProperty("grupy").GetArrayLength());
        var group = item.GetProperty("grupy")[0];
        Assert.Equal(Verdict.Duplicate.ToKey(), group.GetProperty("werdykt").GetString());
        Assert.Equal("odrzucona", group.GetProperty("stan").GetString());
        Assert.Equal(1, group.GetProperty("inni").GetArrayLength());
        Assert.Equal("jbib_001", group.GetProperty("inni")[0].GetProperty("nazwa").GetString());

        var error = await app.Failing("catalog.item", "{\"id\":\"no-such-garment\"}");
        Assert.Equal(BridgeErrors.NotFound, error.GetProperty("code").GetString());
    }
}
