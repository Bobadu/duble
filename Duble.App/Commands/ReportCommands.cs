// Commands/ReportCommands.cs — report.exportHtml and report.exportCsv.
//
// Both are written in the language of the interface and carry the user's decisions. The HTML report decodes
// every thumbnail it shows, which takes seconds, so it runs as a job; the CSV is immediate.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Duble.App.Commands;

public sealed class ReportCommands : CommandModule
{
    readonly JobRunner jobs;
    readonly LiveGroups groups;
    readonly IHtmlReportBuilder html;
    readonly ICsvExporter csv;

    public ReportCommands(Bridge bridge, Session session, JobRunner jobs, LiveGroups groups,
                          IHtmlReportBuilder html, ICsvExporter csv)
        : base(bridge, session)
    {
        this.jobs = jobs;
        this.groups = groups;
        this.html = html;
        this.csv = csv;
    }

    public override void Register()
    {
        Bridge.Register("report.exportHtml", ExportHtml);
        Bridge.Register("report.exportCsv", ExportCsv);
    }

    object ExportHtml(JsonElement args)
    {
        var project = Project;
        var comparison = RequireComparison();
        var file = Target(args, "html", SafeFileName(project.Name) + "-raport.html");
        if (file == null) return new { anulowano = true };

        var options = new ReportOptions
        {
            Language = Bridge.Settings.EffectiveLanguage,
            Title = project.Name,
            Resolve = groups.Resolve,
        };

        bool started = jobs.TryStart(JobKinds.Report, Path.GetFileName(file), async (_, progress) =>
        {
            await Task.Yield();
            progress(new ProgressReport("report", 0, 0, Path.GetFileName(file)));
            html.Build(Session.Catalog, comparison, file, options);
            Bridge.Event("report.done", new { plik = file, typ = "html" });
        });
        if (!started) throw Busy();

        return new { uruchomiono = true, plik = file };
    }

    object ExportCsv(JsonElement args)
    {
        var project = Project;
        var comparison = RequireComparison();
        var file = Target(args, "csv", SafeFileName(project.Name) + "-grupy.csv");
        if (file == null) return new { anulowano = true };

        try
        {
            var text = csv.Export(Session.Catalog, comparison, groups.Resolve, Bridge.Settings.EffectiveLanguage);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllText(file, text, new UTF8Encoding(false));   // the exporter puts the BOM in the text itself
        }
        catch (Exception e) { throw new BridgeException(BridgeErrors.Io, e.Message); }

        Bridge.Event("report.done", new { plik = file, typ = "csv" });
        return new { plik = file };
    }

    ComparisonResult RequireComparison()
        => Session.Comparison ?? throw new BridgeException(BridgeErrors.NotFound, "nothing has been compared yet");

    /// <summary>Where to write: what the interface asked for, or whatever the Save dialog returns (null = cancelled).</summary>
    string? Target(JsonElement args, string filter, string defaultName)
    {
        var asked = args.Text("sciezka");
        if (!string.IsNullOrWhiteSpace(asked)) return asked;
        return Bridge.Dialogs.SaveFile(null, filter, defaultName, Path.GetDirectoryName(Project.Path));
    }

    static string SafeFileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat((name ?? "project").Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }
}
