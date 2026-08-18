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
    public static IReadOnlyList<ICommandModule> Create(IServiceProvider services, Bridge bridge, Session session, JobRunner jobs)
    {
        var groups = new LiveGroups(session, services.GetRequiredService<IResolutionService>());
        var garments = new GarmentView(services.GetRequiredService<IQualityScorer>());
        var workflow = new CatalogWorkflow(bridge, session,
            services.GetRequiredService<IGarmentIndexer>(), services.GetRequiredService<IClock>());

        var executor = services.GetRequiredService<IApplyExecutor>();
        var undoLogs = services.GetRequiredService<IUndoStore>();

        return new ICommandModule[]
        {
            new AppCommands(bridge),
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
