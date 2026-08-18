#nullable enable
using System;
using System.IO;

namespace Duble.Core.Reporting;

/// <summary>
/// The report's stylesheet and script, embedded in the assembly and inlined into every report. They are real
/// .css and .js files rather than C# strings so they stay editable as what they are; nothing outside this
/// class needs to know where they come from.
/// </summary>
static class ReportAssets
{
    static readonly Lazy<string> style = new(() => Read("report.css"));
    static readonly Lazy<string> script = new(() => Read("report.js"));

    public static string Style => style.Value;

    public static string Script => script.Value;

    static string Read(string fileName)
    {
        var resource = "Duble.Core.Reporting." + fileName;
        using var stream = typeof(ReportAssets).Assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException("missing resource " + resource);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimEnd();
    }
}
