// Commands/ApplyCommands.cs — apply.preview and apply.run.
//
// The plan is worked out fresh every time from what the live groups currently reject, so what the dialog
// shows is what will happen. Running it moves the files, writes an undo log to <cache>\history\<time>.json,
// then indexes the sources it touched again and compares — see CatalogWorkflow.
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Duble.App.Commands;

public sealed class ApplyCommands : CommandModule
{
    readonly JobRunner jobs;
    readonly LiveGroups groups;
    readonly CatalogWorkflow workflow;
    readonly IApplyExecutor executor;
    readonly IUndoStore undoLogs;

    public ApplyCommands(Bridge bridge, Session session, JobRunner jobs, LiveGroups groups, CatalogWorkflow workflow,
                         IApplyExecutor executor, IUndoStore undoLogs)
        : base(bridge, session)
    {
        this.jobs = jobs;
        this.groups = groups;
        this.workflow = workflow;
        this.executor = executor;
        this.undoLogs = undoLogs;
    }

    public override void Register()
    {
        Bridge.Register("apply.preview", Preview);
        Bridge.Register("apply.run", Run);
    }

    object Preview(JsonElement args)
    {
        RequireProject();   // there is nothing to plan without a project
        SetBinFolder(args);
        return PlanView.Describe(Session, CurrentPlan(), withList: true);
    }

    ApplyPlan CurrentPlan() => Session.Plan(LiveGroups.RejectedIds(groups.All()));

    /// <summary>
    /// {kosz?: string|null, ustawKosz?: bool} — the Apply dialog can change where rejected files go and see
    /// the new plan in the same call.
    /// </summary>
    void SetBinFolder(JsonElement args)
    {
        if (!args.Flag("ustawKosz")) return;
        var bin = args.Text("kosz");
        var project = Project;
        project.Settings ??= new ProjectSettings();
        project.Settings.BinFolder = string.IsNullOrWhiteSpace(bin) ? null : bin;
        Session.SaveProject();
    }

    object Run(JsonElement args)
    {
        var name = ProjectName;   // no project, no apply
        SetBinFolder(args);

        var plan = CurrentPlan();
        if (plan.Files == 0) return new { uruchomiono = false, plan = PlanView.Describe(Session, plan, withList: false) };

        bool started = jobs.TryStart(JobKinds.Apply, name, async (cancellation, progress) =>
        {
            await Task.Yield();
            Apply(plan, name, cancellation, progress);
        });
        if (!started) throw Busy();

        return new { uruchomiono = true, plan = PlanView.Describe(Session, plan, withList: false) };
    }

    void Apply(ApplyPlan plan, string description, CancellationToken cancellation, Action<ProgressReport> progress)
    {
        var log = executor.Execute(plan, description, new Progress<ProgressReport>(progress), cancellation);

        // ALWAYS, an aborted apply included: whatever did move has to remain undoable
        var file = Session.NewHistoryFile();
        var saved = undoLogs.Save(log, file);
        Bridge.Event("history.changed", new { plik = file });

        // after an abort the catalog is tidied up anyway — it would otherwise still list files that moved
        var afterwards = log.Aborted ? CancellationToken.None : cancellation;
        var touched = Project.Sources.Where(source => log.Garments.Any(garment => garment.SourceId == source.Id)).ToList();
        if (touched.Count > 0) workflow.Index(touched, false, afterwards, progress);
        workflow.CompareAndSave(afterwards, progress);

        Bridge.Event("apply.done", new
        {
            plik = file,
            przeniesione = log.Moves.Count,
            pozycje = log.Garments.Count,
            bajty = log.Bytes,
            wspoldzielone = log.SharedCount,
            wArchiwum = log.InArchiveCount,
            brakujace = log.MissingCount,
            kosze = log.Garments.Select(garment => garment.BinFolder).Where(bin => bin != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            przerwano = log.Aborted,
            // an undo log that could not be written is the one failure worth interrupting the good news for:
            // the files have moved and, without it, nothing can put them back
            blad = log.Error ?? (saved.IsFailure ? saved.Error.Message : null),
        });
    }
}
