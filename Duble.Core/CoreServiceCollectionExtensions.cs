#nullable enable
using Duble.Core.Formats;
using Duble.Core.Storage;
using Duble.Core.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Duble.Core;

/// <summary>
/// The single entry point into Core. The app and the CLI call this once at start-up and resolve what they need,
/// instead of reaching into a static engine method.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddDubleCore(this IServiceCollection services)
    {
        // CodeWalker has to be in gen9 mode before any file is read. Doing it here means it is done before
        // anything can be resolved, rather than when the assembly happens to load.
        CodeWalkerRuntime.Initialize();

        services.AddSingleton<CodeWalkerRuntime>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICatalogStore, JsonCatalogStore>();
        return services;
    }
}
