// Commands/CatalogWorkflow.cs — reading sources into the catalog, and the comparison that has to follow.
//
// Three commands end in the same two steps: adding or re-indexing a source, applying decisions, and undoing
// them all move files or change what is on disk, after which the Duplicates screen is only correct once the
// affected sources have been read again and everything compared. Keeping both here is what stops one of the
// three from forgetting the second half.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Duble.App.Commands;

public sealed class CatalogWorkflow
{
    readonly Bridge bridge;
    readonly Session session;
    readonly IGarmentIndexer indexer;
    readonly IClock clock;

    public CatalogWorkflow(Bridge bridge, Session session, IGarmentIndexer indexer, IClock clock)
    {
        this.bridge = bridge;
        this.session = session;
        this.indexer = indexer;
        this.clock = clock;
    }

    /// <summary>
    /// Indexes the given sources and replaces their garments in the session's catalog. Incremental unless
    /// <paramref name="force"/>: what has not changed on disk keeps the fingerprints it already had. A source
    /// that is no longer there is skipped rather than emptied — an unplugged drive is not a reason to lose it.
    /// </summary>
    public void Index(IList<ProjectSource> sources, bool force, CancellationToken cancellation, Action<ProgressReport> progress)
    {
        var project = session.Project ?? throw new InvalidOperationException("no project is open");

        foreach (var source in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Directory.Exists(source.Path) && !File.Exists(source.Path)) continue;

            Catalog? previous = null;
            session.EditCatalog(catalog => previous = new Catalog { Garments = catalog.Garments.ToList() });

            var options = new IndexOptions
            {
                PreviousCatalog = previous,
                Force = force,
                ThumbnailFolder = project.ThumbnailFolder,
            };

            var name = source.Name ?? "";
            progress(new ProgressReport("start", 0, 0, name));
            var report = indexer.Index(source.Path, name, options,
                new Progress<ProgressReport>(step => progress(new ProgressReport(step.Stage, step.Done, step.Total, name))),
                cancellation);
            if (report.IsFailure) throw new BridgeException(BridgeErrors.Io, report.Error.Message);

            var garments = report.Value.Garments;
            foreach (var garment in garments) garment.SourceId = source.Id;
            session.EditCatalog(catalog =>
            {
                catalog.RemovePack(name);
                catalog.Upsert(garments);
                catalog.Sources[name] = source.Path;
            });

            source.IndexedAt = clock.Stamp();
            source.Format = SourceFormats.Of(garments);

            bridge.Event("sources.changed", new { id = source.Id });
            bridge.Event("project.changed", new { projekt = session.Summary() });
        }
    }

    /// <summary>Compares, saves everything, and tells the interface — the Duplicates screen is never stale.</summary>
    public void CompareAndSave(CancellationToken cancellation, Action<ProgressReport> progress)
    {
        progress(new ProgressReport("compare", 0, 0, null));
        session.Compare(cancellation, progress);
        session.Save();

        bridge.Event("sources.changed", new { id = (string?)null });
        bridge.Event("project.changed", new { projekt = session.Summary() });
        bridge.Event("compare.done", new { podsumowanie = session.Summary() });
    }
}
