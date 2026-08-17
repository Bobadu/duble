#nullable enable
using Duble.App;
using Microsoft.Extensions.DependencyInjection;

namespace Duble.Tests;

/// <summary>
/// A session backed by the real Core services. Tests build one through here so that a new dependency on the
/// session does not have to be threaded through every test file.
/// </summary>
public static class TestSession
{
    public static Sesja Create()
    {
        var services = new ServiceCollection().AddDubleCore().BuildServiceProvider();
        return new Sesja(services.GetRequiredService<ICatalogStore>());
    }
}
