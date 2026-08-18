// JobRunner.cs — one long job at a time (indexing, comparing, applying…), reported to the interface as "job"
// events and cancellable from there.
//
// event "job": { kind, description, state: start|progress|done|cancelled|failed, stage, done, total, percent,
//                text, error } — the interface's vocabulary, see Bridge.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Duble.App;

/// <summary>The kinds of job, as the interface knows them: it shows progress only for the one it expects.</summary>
public static class JobKinds
{
    public const string Index = "index";
    public const string Compare = "compare";
    public const string Apply = "apply";
    public const string Undo = "undo";
    public const string Unpack = "unpack";
    public const string Report = "report";
    public const string Calibration = "calibration";
}

public sealed class JobRunner
{
    /// <summary>Progress is reported per file — hundreds of times a second while applying — and every event
    /// redraws the interface, so it is passed on at most this often. A new stage and the last step of a stage
    /// always get through, otherwise a progress bar could sit at 90 % after the work had finished.</summary>
    const int MinimumMillisecondsBetweenReports = 100;

    readonly Action<string, object> raise;
    readonly object gate = new();
    CancellationTokenSource? cancellation;

    public JobRunner(Action<string, object> raise) => this.raise = raise;

    /// <summary>Whether a job is running. The interface asks before offering to start another.</summary>
    public bool Busy { get; private set; }

    /// <summary>The kind of the running job, or null.</summary>
    public string? Current { get; private set; }

    /// <summary>Runs the work and waits for it. false = another job was already running, nothing was started.</summary>
    public async Task<bool> Run(string kind, string description, Func<CancellationToken, Action<ProgressReport>, Task> work)
    {
        var reserved = Reserve(kind);
        if (reserved == null) return false;
        await Execute(kind, description, work, reserved).ConfigureAwait(false);
        return true;
    }

    /// <summary>Starts the work in the background. true = it is running, false = another job was already running.</summary>
    public bool TryStart(string kind, string description, Func<CancellationToken, Action<ProgressReport>, Task> work)
    {
        var reserved = Reserve(kind);
        if (reserved == null) return false;
        _ = Execute(kind, description, work, reserved);
        return true;
    }

    public void Cancel()
    {
        lock (gate) cancellation?.Cancel();
    }

    /// <summary>Takes the single slot, or returns null if it is taken. Reserving and starting have to be one
    /// step: two commands arriving together would otherwise both believe they had started their job.</summary>
    CancellationTokenSource? Reserve(string kind)
    {
        lock (gate)
        {
            if (Busy) return null;
            Busy = true;
            Current = kind;
            return cancellation = new CancellationTokenSource();
        }
    }

    async Task Execute(string kind, string description, Func<CancellationToken, Action<ProgressReport>, Task> work, CancellationTokenSource reserved)
    {
        var token = reserved.Token;
        raise("job", new { kind = kind, description = description, state = "start" });
        try
        {
            await Task.Run(() => work(token, Throttled(kind, description)), token).ConfigureAwait(false);
            raise("job", new { kind = kind, description = description, state = "done" });
        }
        catch (OperationCanceledException) { raise("job", new { kind = kind, description = description, state = "cancelled" }); }
        catch (Exception e) { raise("job", new { kind = kind, description = description, state = "failed", error = e.Message }); }
        finally
        {
            lock (gate)
            {
                Busy = false;
                Current = null;
                if (ReferenceEquals(cancellation, reserved)) cancellation = null;
            }
            reserved.Dispose();
        }
    }

    Action<ProgressReport> Throttled(string kind, string description)
    {
        var throttle = new object();
        long lastTick = 0;
        string? lastStage = null;

        return report =>
        {
            lock (throttle)
            {
                var now = Environment.TickCount64;
                bool endOfStage = report.Total > 0 && report.Done >= report.Total;
                bool newStage = report.Stage != lastStage;
                if (!endOfStage && !newStage && now - lastTick < MinimumMillisecondsBetweenReports) return;
                lastTick = now;
                lastStage = report.Stage;
            }
            raise("job", new
            {
                kind = kind,
                description = description,
                state = "progress",
                stage = report.Stage,
                done = report.Done,
                total = report.Total,
                percent = report.Total > 0 ? (int)(100L * report.Done / report.Total) : 0,
                text = report.Container,
            });
        };
    }
}
