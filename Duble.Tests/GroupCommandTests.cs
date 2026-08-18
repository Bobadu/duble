using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

/// <summary>
/// groups.* and apply.preview over a made-up catalog, with no game files: three groups — DUPLICATE a=b,
/// RETEXTURE c=d and DUPLICATE e=f=g.
/// </summary>
public class GroupCommandTests
{
    static TestApp Compared(string name)
    {
        var app = new TestApp(name);
        app.NewProject("G");
        SampleData.SevenGarmentsInThreeSources(app.Session, app.Temp);
        app.Session.Compare(default, null);
        app.Session.Save();
        return app;
    }

    [Fact]
    public async Task The_list_its_filters_the_decisions_and_the_apply_preview()
    {
        using var app = Compared("groups");

        var list = await app.Call("groups.list", "{}");
        Assert.Equal(3, list.GetProperty("groups").GetArrayLength());

        var summary = list.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("duplicate").GetInt32());
        Assert.Equal(1, summary.GetProperty("retexture").GetInt32());
        Assert.Equal(3, summary.GetProperty("toReject").GetProperty("garments").GetInt32());   // b, f and g
        Assert.Equal(Verdict.Duplicate.ToKey(), list.GetProperty("groups")[0].GetProperty("verdict").GetString());
        Assert.Equal(3, list.GetProperty("groups")[0].GetProperty("members").GetArrayLength());   // the larger group first
        Assert.True(list.GetProperty("filters").GetProperty("slots").GetArrayLength() >= 3);

        Assert.Equal(1, (await app.Call("groups.list", "{\"verdicts\":[\"retexture\"]}")).GetProperty("groups").GetArrayLength());
        Assert.Equal(1, (await app.Call("groups.list", "{\"search\":\"jbib_007\"}")).GetProperty("groups").GetArrayLength());
        Assert.Equal(1, (await app.Call("groups.list", "{\"slots\":[\"feet\"]}")).GetProperty("groups").GetArrayLength());

        // the a=b group: make b the one to keep
        var jbib = (await app.Call("groups.list", "{\"slots\":[\"jbib\"]}")).GetProperty("groups")[0];
        var groupId = jbib.GetProperty("id").GetString();
        var idOfB = MemberId(jbib, number: 7);
        var idOfA = MemberId(jbib, number: 1);

        var decided = (await app.Call("groups.decide", $"{{\"id\":\"{groupId}\",\"winner\":\"{idOfB}\"}}"))
            .GetProperty("resolution");
        Assert.Equal(idOfB, decided.GetProperty("winner").GetString());
        Assert.Equal(idOfA, decided.GetProperty("rejected")[0].GetString());
        Assert.False(decided.GetProperty("isDefault").GetBoolean());
        Assert.True(app.Saw("groups.changed"));
        Assert.Contains(groupId, File.ReadAllText(app.Session.Project.Path));   // written into the .duble file

        // the e=f=g group: not a duplicate at all
        var feet = (await app.Call("groups.list", "{\"slots\":[\"feet\"]}")).GetProperty("groups")[0];
        var bootsId = feet.GetProperty("id").GetString();
        await app.Call("groups.decide", $"{{\"id\":\"{bootsId}\",\"ignored\":true,\"note\":\"other boots\"}}");

        Assert.Equal(2, (await app.Call("groups.list", "{}")).GetProperty("groups").GetArrayLength());
        Assert.Equal(3, (await app.Call("groups.list", "{\"ignored\":true}")).GetProperty("groups").GetArrayLength());

        var preview = await app.Call("apply.preview");
        Assert.Equal(1, preview.GetProperty("garments").GetInt32());   // only a
        Assert.Equal(3, preview.GetProperty("files").GetInt32());     // its .ydd and two .ytd
        Assert.Equal(200, preview.GetProperty("bytes").GetInt64());   // 100 + 50 + 50

        // the details of a group: which textures match, and the quality breakdown
        var details = (await app.Call("groups.get", $"{{\"id\":\"{groupId}\"}}")).GetProperty("group");
        Assert.Equal(1, details.GetProperty("matches").GetArrayLength());
        Assert.Equal(2, details.GetProperty("matches")[0].GetProperty("pairs").GetArrayLength());
        Assert.True(details.GetProperty("members")[0].GetProperty("quality").GetProperty("total").GetDouble() > 0);
        Assert.Equal(2, details.GetProperty("members")[0].GetProperty("textures").GetArrayLength());

        var note = (await app.Call("groups.get", $"{{\"id\":\"{bootsId}\"}}"))
            .GetProperty("group").GetProperty("resolution").GetProperty("note").GetString();
        Assert.Equal("other boots", note);

        // resetting goes back to what Core would have chosen
        var reset = (await app.Call("groups.reset", $"{{\"id\":\"{bootsId}\"}}")).GetProperty("resolution");
        Assert.True(reset.GetProperty("isDefault").GetBoolean());
        Assert.Equal(3, (await app.Call("groups.list", "{}")).GetProperty("groups").GetArrayLength());

        var error = await app.Failing("groups.get", "{\"id\":\"no-such-group\"}");
        Assert.Equal(BridgeErrors.NotFound, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rejecting_by_hand_keeps_the_winner_out_of_the_list()
    {
        using var app = Compared("groups-reject");
        var jbib = (await app.Call("groups.list", "{\"slots\":[\"jbib\"]}")).GetProperty("groups")[0];
        var groupId = jbib.GetProperty("id").GetString();
        var idOfA = MemberId(jbib, number: 1);
        var idOfB = MemberId(jbib, number: 7);

        // the interface may send the winner among the rejected; it cannot be both
        var decided = (await app.Call("groups.decide",
                $"{{\"id\":\"{groupId}\",\"winner\":\"{idOfA}\",\"rejected\":[\"{idOfA}\",\"{idOfB}\"]}}"))
            .GetProperty("resolution");

        Assert.Equal(idOfA, decided.GetProperty("winner").GetString());
        Assert.Equal(idOfB, Assert.Single(decided.GetProperty("rejected").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Compare_run_starts_a_job_and_ends_with_compare_done()
    {
        using var app = Compared("groups-compare");

        var started = await app.Call("compare.run");

        Assert.True(started.GetProperty("started").GetBoolean());
        await app.WaitFor("compare.done");
    }

    static string MemberId(JsonElement group, int number)
        => group.GetProperty("members").EnumerateArray()
            .First(member => member.GetProperty("number").GetInt32() == number)
            .GetProperty("id").GetString();
}
