#nullable enable
using CodeWalker.GameFiles;
using Duble.Core;
using Duble.Core.Formats;
using Duble.Core.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Duble.Tests;

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
}
