#nullable enable
using CodeWalker.GameFiles;
using Duble.Core;
using Duble.Core.Formats;
using Duble.Core.Reporting;
using Duble.Core.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Duble.Tests;

/// <summary>AddDubleCore is the only way in: whatever an application asks it for has to come back wired up.</summary>
public class ServiceRegistrationTests
{
    [Fact]
    public void AddDubleCore_resolves_the_clock()
    {
        using var provider = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        Assert.IsType<SystemClock>(provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddDubleCore_owns_the_CodeWalker_runtime_and_prepares_it()
    {
        using var provider = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<CodeWalkerRuntime>());
        Assert.True(RpfManager.IsGen9);
    }

    [Fact]
    public void Services_are_singletons()
    {
        using var provider = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<IClock>(), provider.GetRequiredService<IClock>());
    }

    /// <summary>
    /// The report and the export used to be one class registered under two interfaces. They are separate now,
    /// and the application resolves each by its own interface.
    /// </summary>
    [Fact]
    public void The_report_and_the_export_are_two_separate_services()
    {
        using var provider = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        Assert.IsType<HtmlReportBuilder>(provider.GetRequiredService<IHtmlReportBuilder>());
        Assert.IsType<CsvExporter>(provider.GetRequiredService<ICsvExporter>());
    }
}
