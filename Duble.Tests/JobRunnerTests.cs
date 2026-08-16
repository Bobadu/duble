using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Xunit;

namespace Duble.Tests;

public class JobRunnerTests
{
    static string J(object o) => JsonSerializer.Serialize(o, Mostek.Json);

    [Fact]
    public async Task Jedno_zadanie_naraz_postep_i_koniec()
    {
        var zd = new List<string>();
        var jr = new JobRunner((n, d) => zd.Add(n + " " + J(d)));
        var start = new TaskCompletionSource(); var pusc = new TaskCompletionSource();
        var t1 = jr.Uruchom("indeks", "A", async (ct, postep) => { start.SetResult(); await pusc.Task; postep(new Duble.Postep("modele", 5, 10, "A")); });
        await start.Task;
        Assert.True(jr.Zajety);
        Assert.False(await jr.Uruchom("indeks", "B", (ct, p) => Task.CompletedTask));   // zajety
        pusc.SetResult();
        Assert.True(await t1);
        Assert.False(jr.Zajety);
        Assert.Contains(zd, z => z.StartsWith("job ") && z.Contains("\"stan\":\"start\""));
        Assert.Contains(zd, z => z.Contains("\"stan\":\"postep\"") && z.Contains("\"procent\":50"));
        Assert.Contains(zd, z => z.Contains("\"stan\":\"koniec\""));
    }

    [Fact]
    public async Task Anulowanie_daje_stan_anulowano()
    {
        var zd = new List<string>();
        var jr = new JobRunner((n, d) => zd.Add(J(d)));
        var t = jr.Uruchom("indeks", "A", async (ct, p) => { while (true) { ct.ThrowIfCancellationRequested(); await Task.Delay(20, ct); } });
        await Task.Delay(80); jr.Anuluj();
        Assert.True(await t);   // zadanie sie skonczylo (anulowaniem)
        Assert.Contains(zd, z => z.Contains("\"stan\":\"anulowano\""));
    }

    [Fact]
    public async Task Wyjatek_daje_stan_blad()
    {
        var zd = new List<string>();
        var jr = new JobRunner((n, d) => zd.Add(J(d)));
        Assert.True(await jr.Uruchom("indeks", "A", (ct, p) => throw new System.IO.IOException("dysk")));
        Assert.Contains(zd, z => z.Contains("\"stan\":\"blad\"") && z.Contains("dysk"));
        Assert.False(jr.Zajety);
    }
}
