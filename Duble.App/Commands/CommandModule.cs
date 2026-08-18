// Commands/CommandModule.cs — what every group of bridge commands has in common.
using System.Linq;

namespace Duble.App.Commands;

/// <summary>
/// A group of bridge commands. Register is the table of contents: one line per command, pointing at the
/// method that answers it.
/// </summary>
public interface ICommandModule
{
    void Register();
}

/// <summary>
/// Base for the command groups that work on the open project. It exists for the three things all of them do:
/// refuse to run without a project, name the source a garment came from, and tell the interface that the
/// project has changed.
/// </summary>
public abstract class CommandModule : ICommandModule
{
    protected CommandModule(Bridge bridge, Session session)
    {
        Bridge = bridge;
        Session = session;
    }

    protected Bridge Bridge { get; }
    protected Session Session { get; }

    public abstract void Register();

    /// <summary>The open project, or a no_project answer — which the interface turns into its own message.</summary>
    protected Project Project => Session.Project ?? throw new BridgeException(BridgeErrors.NoProject, "no project is open");

    /// <summary>The same refusal where the project itself is not needed, only the fact that there is one.</summary>
    protected void RequireProject() => _ = Project;

    /// <summary>The project's name, which several commands use to describe the job they are starting.</summary>
    protected string ProjectName => Project.Name ?? "";

    /// <summary>The name of the source a garment came from; catalogs written before sources had ids only
    /// know the name of the pack.</summary>
    protected string SourceName(Garment garment)
        => Project.Sources.Find(source => source.Id == garment.SourceId)?.Name ?? garment.PackName ?? "";

    /// <summary>What a garment is searched by on both screens that have a search box: its slot and number,
    /// its pack, its container, its source and its id, lower case and in one string.</summary>
    protected string SearchText(Garment garment)
        => $"{garment.Slot}_{garment.Number:d3} {garment.PackName} {garment.Container} {SourceName(garment)} {garment.Id}".ToLowerInvariant();

    /// <summary>The status bar shows counts from the summary, so anything that changes them says so.</summary>
    protected void ProjectChanged() => Bridge.Event("project.changed", new { project = Session.Summary() });

    /// <summary>The one long job at a time is taken; the interface says so rather than queueing.</summary>
    protected static BridgeException Busy() => new(BridgeErrors.Busy, "another job is running");
}
