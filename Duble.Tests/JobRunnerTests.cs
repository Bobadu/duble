using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Duble.App;
using Duble.Core;
using Xunit;

namespace Duble.Tests;

public class JobRunnerTests
{
    static string Json(object data) => JsonSerializer.Serialize(data, Bridge.Json);

    [Fact]
    public async Task One_job_at_a_time_with_progress_and_an_end()
    {
        var events = new List<string>();
        var jobs = new JobRunner((name, data) => events.Add(name + " " + Json(data)));
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = jobs.Run(JobKinds.Index, "A", async (_, progress) =>
        {
            started.SetResult();
            await release.Task;
            progress(new ProgressReport("models", 5, 10, "A"));
        });

        await started.Task;
        Assert.True(jobs.Busy);
        Assert.Equal(JobKinds.Index, jobs.Current);
        Assert.False(await jobs.Run(JobKinds.Index, "B", (_, _) => Task.CompletedTask));   // busy

        release.SetResult();
        Assert.True(await first);
        Assert.False(jobs.Busy);
        Assert.Null(jobs.Current);

        Assert.Contains(events, e => e.StartsWith("job ") && e.Contains("\"state\":\"start\"") && e.Contains("\"kind\":\"index\"") && e.Contains("\"description\":\"A\""));
        Assert.Contains(events, e => e.Contains("\"state\":\"progress\"") && e.Contains("\"percent\":50"));
        Assert.Contains(events, e => e.Contains("\"state\":\"done\""));
    }

    [Fact]
    public async Task TryStart_refuses_while_a_job_is_running_and_frees_the_slot_afterwards()
    {
        var jobs = new JobRunner((_, _) => { });
        var release = new TaskCompletionSource();

        Assert.True(jobs.TryStart(JobKinds.Index, "A", async (_, _) => await release.Task));
        Assert.False(jobs.TryStart(JobKinds.Compare, "B", (_, _) => Task.CompletedTask));

        release.SetResult();
        for (int waited = 0; waited < 500 && jobs.Busy; waited++) await Task.Delay(10);
        Assert.False(jobs.Busy);
        Assert.True(jobs.TryStart(JobKinds.Compare, "B", (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task Progress_is_throttled_but_a_new_stage_and_the_end_of_one_always_get_through()
    {
        var events = new List<string>();
        var jobs = new JobRunner((_, data) => events.Add(Json(data)));

        await jobs.Run(JobKinds.Apply, "A", (_, progress) =>
        {
            for (int i = 0; i < 500; i++) progress(new ProgressReport("apply", i, 500, "x"));   // 500 in a moment
            progress(new ProgressReport("apply", 500, 500, null));                              // end of the stage
            progress(new ProgressReport("compare", 0, 0, null));                                  // a new stage
            return Task.CompletedTask;
        });

        int reported = events.FindAll(e => e.Contains("\"state\":\"progress\"")).Count;
        Assert.InRange(reported, 3, 30);   // the first, the end of the stage, the new stage, plus any 100 ms ticks
        Assert.Contains(events, e => e.Contains("\"done\":500") && e.Contains("\"percent\":100"));
        Assert.Contains(events, e => e.Contains("\"stage\":\"compare\""));
    }

    [Fact]
    public async Task Cancelling_ends_the_job_as_cancelled()
    {
        var events = new List<string>();
        var jobs = new JobRunner((_, data) => events.Add(Json(data)));

        var job = jobs.Run(JobKinds.Index, "A", async (cancellation, _) =>
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                await Task.Delay(20, cancellation);
            }
        });

        await Task.Delay(80);
        jobs.Cancel();

        Assert.True(await job);
        Assert.Contains(events, e => e.Contains("\"state\":\"cancelled\""));
        Assert.False(jobs.Busy);
    }

    [Fact]
    public async Task A_failure_ends_the_job_as_failed_and_frees_the_runner()
    {
        var events = new List<string>();
        var jobs = new JobRunner((_, data) => events.Add(Json(data)));

        Assert.True(await jobs.Run(JobKinds.Index, "A", (_, _) => throw new System.IO.IOException("disk")));

        Assert.Contains(events, e => e.Contains("\"state\":\"failed\"") && e.Contains("disk"));
        Assert.False(jobs.Busy);
    }
}
