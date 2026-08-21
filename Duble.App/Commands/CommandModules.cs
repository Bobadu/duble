// Commands/CommandModules.cs — every group of commands the interface can call, in one place.
//
// This is the list to add to when a new group appears, and the one the tests build too, so that what they
// drive is wired exactly like the running program.
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Duble.App.Commands;

public static class CommandModules
{
    /// <summary>
    /// <paramref name="updates"/> is where the update check asks and <paramref name="installer"/> is what can
    /// install what it finds; null means GitHub and the Setup. The tests pass their own, so that nothing in
    /// the suite reaches the network.
    /// </summary>
    public static IReadOnlyList<ICommandModule> Create(IServiceProvider services, Bridge bridge, Session session, JobRunner jobs,
        IUpdateSource? updates = null, IUpdateInstaller? installer = null)
    {
        var releases = updates ?? new GitHubUpdateSource();
        var groups = new LiveGroups(session, services.GetRequiredService<IResolutionService>());
        var garments = new GarmentView(services.GetRequiredService<IQualityScorer>());
        var workflow = new CatalogWorkflow(bridge, session,
            services.GetRequiredService<IGarmentIndexer>(), services.GetRequiredService<IClock>());

        var executor = services.GetRequiredService<IApplyExecutor>();
        var undoLogs = services.GetRequiredService<IUndoStore>();

        return new ICommandModule[]
        {
            new AppCommands(bridge, releases, installer ?? new InnoUpdateInstaller(releases)),
            new WindowCommands(bridge),
            new ProjectCommands(bridge, session),
            new SourceCommands(bridge, session, jobs, workflow, services.GetRequiredService<IArchiveExtractor>()),
            new GroupCommands(bridge, session, jobs, groups, garments, workflow),
            new ApplyCommands(bridge, session, jobs, groups, workflow, executor, undoLogs),
            new HistoryCommands(bridge, session, jobs, workflow, executor, undoLogs),
            new ReportCommands(bridge, session, jobs, groups,
                services.GetRequiredService<IHtmlReportBuilder>(), services.GetRequiredService<ICsvExporter>()),
            new CatalogCommands(bridge, session, groups, garments),
            new ProjectSettingsCommands(bridge, session, jobs, workflow, services.GetRequiredService<ICalibrator>()),
        };
    }
}
