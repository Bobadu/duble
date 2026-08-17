#nullable enable
using Duble.Core.Comparison;
using Duble.Core.Decisions;
using Duble.Core.Fingerprints;
using Duble.Core.Formats;
using Duble.Core.Indexing;
using Duble.Core.Sources;
using Duble.Core.Storage;
using Duble.Core.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        services.AddSingleton<IProjectStore, JsonProjectStore>();
        services.AddSingleton<IResolutionService, ResolutionService>();
        services.AddSingleton<IComparisonStore, JsonComparisonStore>();

        services.AddSingleton<IQualityScorer, QualityScorer>();
        services.AddSingleton<IReasonFormatter, ReasonFormatter>();
        services.AddSingleton<IDuplicateFinder, DuplicateFinder>();

        services.AddSingleton<ArchiveSourceReader>();
        services.AddSingleton<FolderSourceReader>();
        services.AddSingleton<ISourceReaderFactory, SourceReaderFactory>();
        services.AddSingleton<IArchiveCache, RpfArchiveCache>();

        services.AddSingleton<IGeometryFingerprinter, GeometryFingerprinter>();
        services.AddSingleton<ITextureFingerprinter, TextureFingerprinter>();
        services.AddSingleton<IGarmentIndexer, GarmentIndexer>();

        // An application that wants Core's log lines adds its own logging; without one they go nowhere.
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services;
    }
}
