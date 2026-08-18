#nullable enable
// A comparison viewer as one plain, self-contained HTML file.
//
// Thumbnails are written into the file as data:image/png;base64, and the stylesheet and script are inlined, so
// the report works after being copied anywhere, with no network. The textures are decoded AGAIN from their
// sources — the catalog keeps fingerprints, not images — which is why every texture remembers the path it
// came from.
using System.Globalization;
using System.IO;
using System.Text;
using Duble.Core.Comparison;
using Duble.Core.Decisions;
using Duble.Core.Formats;
using Duble.Core.Model;
using Duble.Core.Sources;

namespace Duble.Core.Reporting;

/// <inheritdoc />
public sealed class HtmlReportBuilder : IHtmlReportBuilder
{
    readonly IArchiveCache archives;
    readonly IResolutionService resolutions;

    public HtmlReportBuilder(IArchiveCache archives, IResolutionService resolutions, CodeWalkerRuntime runtime)
    {
        this.archives = archives;
        this.resolutions = resolutions;
        _ = runtime;   // textures are decoded here, so CodeWalker has to be in gen9 mode before the first read
    }

    public void Build(Catalog catalog, ComparisonResult result, string path, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        var html = new ReportWriter(catalog, result, options, archives, resolutions).Render();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, html, Encoding.UTF8);

        var megabytes = new FileInfo(path).Length / 1024.0 / 1024.0;
        options.Log?.Invoke($"  file size: {megabytes.ToString("F1", CultureInfo.InvariantCulture)} MB");
    }
}
