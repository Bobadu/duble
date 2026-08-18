using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duble.Cli.CommandLine;

namespace Duble.Cli.Commands;

/// <summary>
/// `duble index` and `duble refresh`: read packs and put their garments in the catalog.
///
/// Indexing is INCREMENTAL. A file whose size and timestamp have not changed keeps the fingerprint the
/// catalog already holds, so re-indexing a wardrobe costs seconds rather than minutes; --force ignores that.
/// </summary>
public static class IndexCommand
{
    static readonly CliOption Pack = CliOption.Value("--pack", "name", "Name to index the source under (default: the folder or archive name)");
    static readonly CliOption Thumbnails = CliOption.Value("--thumbnails", "folder", "Also write a <sha>.png thumbnail per texture here");
    static readonly CliOption Force = CliOption.Flag("--force", "Fingerprint everything again, ignoring what the catalog holds");

    public static CliCommand Index { get; } = new(
        "index",
        "Read packs and put their garments in the catalog",
        "<source>...",
        new[] { CatalogOptions.Catalog, Pack, Thumbnails, Force, CatalogOptions.Game, CliPaths.HomeOption },
        context => Run(context, context.Arguments.Positional));

    public static CliCommand Refresh { get; } = new(
        "refresh",
        "Index every source the catalog already knows, again",
        "",
        new[] { CatalogOptions.Catalog, Thumbnails, Force, CatalogOptions.Game, CliPaths.HomeOption },
        RunRefresh);

    static int RunRefresh(CommandContext context)
    {
        var store = context.Service<ICatalogStore>();
        var sources = store.Load(context.Arguments.Value(CatalogOptions.Catalog.Name, context.Paths.Catalog)!)
            .Sources.Values.ToList();

        if (sources.Count == 0)
        {
            context.Output.Error("the catalog knows no sources yet — run `duble index <source>` first");
            return ExitCode.Failed;
        }
        return Run(context, sources);
    }

    static int Run(CommandContext context, IReadOnlyList<string> sources)
    {
        if (sources.Count == 0) return context.Misuse("give at least one source");

        var catalogFile = context.Arguments.Value(CatalogOptions.Catalog.Name, context.Paths.Catalog)!;
        var store = context.Service<ICatalogStore>();
        var indexer = context.Service<IGarmentIndexer>();
        var clock = context.Service<IClock>();

        var catalog = store.Load(catalogFile);
        var options = new IndexOptions
        {
            PreviousCatalog = catalog,
            Force = context.Arguments.Flag(Force.Name),
            ThumbnailFolder = context.Arguments.Value(Thumbnails.Name),
        };

        foreach (var source in sources)
        {
            context.Output.Line("== " + source);

            var name = context.Arguments.Value(Pack.Name) ?? PackNameOf(source);
            var started = clock.Now;
            var report = indexer.Index(source, name, options);
            if (report.IsFailure)
            {
                context.Output.Error(report.Error.ToString());
                return ExitCode.Failed;
            }

            var garments = report.Value.Garments;
            catalog.RemovePack(name);
            catalog.Upsert(garments);
            catalog.Sources[name] = Path.GetFullPath(source);

            int textures = garments.Sum(garment => garment.Textures.Count);
            int undecodable = garments.Sum(garment => garment.Textures.Count(texture => !texture.IsDecoded));
            context.Output.Detail($"{garments.Count} garments, {textures} textures"
                + (undecodable > 0 ? $" ({undecodable} that will not decode)" : "")
                + $", {(clock.Now - started).TotalSeconds:F0} s");
        }

        var saved = store.Save(catalog, catalogFile);
        if (saved.IsFailure)
        {
            context.Output.Error(saved.Error.ToString());
            return ExitCode.Failed;
        }

        context.Output.Line($"catalog: {catalogFile} ({catalog.Garments.Count} garments)");
        return ExitCode.Ok;
    }

    /// <summary>The pack name a source is indexed under: its folder name, or an archive's name without .rpf.</summary>
    static string PackNameOf(string source)
    {
        var name = Path.GetFileName(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar));
        return name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(name) : name;
    }
}
