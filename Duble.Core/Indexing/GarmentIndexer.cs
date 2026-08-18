using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Duble.Core.Fingerprints;
using Duble.Core.Formats;
using Duble.Core.Model;
using Duble.Core.Naming;
using Duble.Core.Results;
using Duble.Core.Sources;
using Microsoft.Extensions.Logging;

namespace Duble.Core.Indexing;

/// <summary>Walks a source and builds garments out of it.</summary>
public interface IGarmentIndexer
{
    /// <summary>
    /// Every garment in one source. Files are grouped into garments by name, following the R* convention:
    /// clothing as &lt;slot&gt;_&lt;NNN&gt;_&lt;u|r&gt;.ydd with &lt;slot&gt;_diff_&lt;NNN&gt;_&lt;letter&gt;_&lt;race&gt;.ytd,
    /// props as p_&lt;anchor&gt;_&lt;NNN&gt;.ydd with p_&lt;anchor&gt;_diff_&lt;NNN&gt;_&lt;letter&gt;.ytd.
    /// </summary>
    Result<IndexReport> Index(string sourcePath, string? packName, IndexOptions options,
                              IProgress<ProgressReport>? progress = null, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class GarmentIndexer : IGarmentIndexer
{
    readonly ISourceReaderFactory readers;
    readonly IGeometryFingerprinter geometry;
    readonly ITextureFingerprinter textures;
    readonly ILogger<GarmentIndexer> log;

    public GarmentIndexer(ISourceReaderFactory readers, IGeometryFingerprinter geometry,
                          ITextureFingerprinter textures, ILogger<GarmentIndexer> log)
    {
        this.readers = readers;
        this.geometry = geometry;
        this.textures = textures;
        this.log = log;
    }

    public Result<IndexReport> Index(string sourcePath, string? packName, IndexOptions options,
                                     IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        options ??= new IndexOptions();
        sourcePath = Path.GetFullPath(sourcePath);
        ct.ThrowIfCancellationRequested();

        var reader = readers.For(sourcePath);
        if (reader.IsFailure) return Result<IndexReport>.Fail(reader.Error);

        var entries = reader.Value.Read(sourcePath);
        var pack = packName ?? Path.GetFileNameWithoutExtension(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        if (entries.Count == 0)
        {
            log.LogWarning("{Source}: no .ydd or .ytd files found", sourcePath);
            return Result<IndexReport>.Ok(new IndexReport(Array.Empty<Garment>(), Array.Empty<string>(),
                                                         Array.Empty<string>(), 0, 0));
        }

        // Incremental: a file whose logical path and change stamp are both unchanged is taken from the
        // previous catalog rather than read and fingerprinted again.
        var knownModels = new Dictionary<string, Garment>(StringComparer.OrdinalIgnoreCase);
        var knownTextures = new Dictionary<string, TextureInfo>(StringComparer.OrdinalIgnoreCase);
        if (options.PreviousCatalog != null && !options.Force)
            foreach (var garment in options.PreviousCatalog.Garments)
            {
                if (garment.ModelPath != null && garment.ChangeStamp != null) knownModels[garment.ModelPath] = garment;
                foreach (var texture in garment.Textures)
                    if (texture.Path != null && texture.ChangeStamp != null) knownTextures[texture.Path] = texture;
            }

        int reusedModels = 0, reusedTextures = 0;
        int batchSize = Math.Max(1, options.BatchSize);

        // Files whose names are outside the convention must not vanish quietly — they are usually the
        // leftovers of an export, and a leftover is exactly what a duplicate finder is looking for.
        var skipped = new ConcurrentBag<string>();

        // Files that ARE clothing and would not read. Kept apart from the ones above because they mean the
        // catalog is incomplete, not that the folder had something else in it.
        var unreadable = new ConcurrentBag<string>();

        // --- models ---
        var garments = new ConcurrentBag<Garment>();
        var modelFiles = entries.Where(e => e.Name.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase)).ToList();
        int done = 0;
        foreach (var batch in modelFiles.Chunk(batchSize))
        {
            ct.ThrowIfCancellationRequested();
            Parallel.ForEach(batch, entry =>
            {
                Garment? garment;
                if (knownModels.TryGetValue(entry.LogicalPath, out var known) && known.ChangeStamp == entry.ChangeStamp)
                {
                    garment = ModelFromCatalog(entry, pack, known);
                    Interlocked.Increment(ref reusedModels);
                }
                else garment = ReadModel(entry, pack, unreadable);

                if (garment != null) garments.Add(garment); else skipped.Add(entry.Name);
                Interlocked.Increment(ref done);
            });
            progress?.Report(new ProgressReport("models", done, modelFiles.Count, pack));
        }

        // --- textures ---
        var texturesByGarment = new ConcurrentDictionary<string, ConcurrentBag<(TextureInfo texture, string race)>>();
        var textureFiles = entries.Where(e => e.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)).ToList();
        done = 0;
        foreach (var batch in textureFiles.Chunk(batchSize))
        {
            ct.ThrowIfCancellationRequested();
            Parallel.ForEach(batch, entry =>
            {
                var name = ClothingFileName.ParseTexture(entry.Name);
                if (name == null) { skipped.Add(entry.Name); Interlocked.Increment(ref done); return; }

                var container = !string.IsNullOrEmpty(entry.Container) ? entry.Container : name.Container ?? "";
                var key = $"{container}|{name.Slot}|{name.Number}|{name.IsProp}";

                TextureInfo? texture;
                if (knownTextures.TryGetValue(entry.LogicalPath, out var known)
                    && known.ChangeStamp == entry.ChangeStamp
                    && ThumbnailIsThere(options.ThumbnailFolder, known))
                {
                    texture = known;
                    Interlocked.Increment(ref reusedTextures);
                }
                else texture = ReadTexture(entry, options, unreadable);

                if (texture != null)
                    texturesByGarment.GetOrAdd(key, _ => new ConcurrentBag<(TextureInfo, string)>()).Add((texture, name.Race));

                Interlocked.Increment(ref done);
            });
            progress?.Report(new ProgressReport("textures", done, textureFiles.Count, pack));
        }

        if (reusedModels + reusedTextures > 0)
            log.LogInformation("{Pack}: reused {Models} models and {Textures} textures from the previous catalog",
                pack, reusedModels, reusedTextures);

        var found = garments.ToList();
        AttachTextures(found, texturesByGarment);

        var skippedFiles = skipped.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (skippedFiles.Count > 0)
            log.LogInformation("{Pack}: {Count} files skipped, outside the naming convention: {Examples}",
                pack, skippedFiles.Count, string.Join(", ", skippedFiles.Take(6)));

        var unreadableFiles = unreadable.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (unreadableFiles.Count > 0)
            log.LogWarning("{Pack}: {Count} clothing files COULD NOT BE READ, so this catalog is incomplete: {Examples}",
                pack, unreadableFiles.Count, string.Join(", ", unreadableFiles.Take(6)));

        var ordered = found.OrderBy(g => g.Slot).ThenBy(g => g.Number).ToList();
        return Result<IndexReport>.Ok(new IndexReport(ordered, skippedFiles, unreadableFiles, reusedModels, reusedTextures));
    }

    /// <summary>
    /// A texture's key carries no u/r suffix, because textures do not have one. So when a _u and an _r model
    /// share a number, they are split by race instead: _u takes "uni", _r takes the rest.
    /// </summary>
    static void AttachTextures(List<Garment> garments,
                               ConcurrentDictionary<string, ConcurrentBag<(TextureInfo texture, string race)>> byGarment)
    {
        foreach (var group in garments.GroupBy(g => $"{g.Container}|{g.Slot}|{g.Number}|{g.IsProp}"))
        {
            if (!byGarment.TryGetValue(group.Key, out var all)) continue;

            var members = group.ToList();
            bool hasUniversal = members.Any(m => m.Suffix.StartsWith("u"));
            bool hasRacial = members.Any(m => m.Suffix.StartsWith("r"));

            foreach (var member in members)
            {
                IEnumerable<(TextureInfo texture, string race)> mine = all;
                if (hasUniversal && hasRacial)
                    mine = member.Suffix.StartsWith("u")
                        ? all.Where(x => x.race.Equals("uni", StringComparison.OrdinalIgnoreCase))
                        : all.Where(x => !x.race.Equals("uni", StringComparison.OrdinalIgnoreCase));

                member.Textures = mine.Select(x => x.texture)
                    .OrderBy(t => t.FileName, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    Garment? ReadModel(SourceEntry entry, string pack, ConcurrentBag<string> unreadable)
    {
        var name = ClothingFileName.ParseModel(entry.Name);
        if (name == null) return null;   // not a clothing model: skipped, not a failure

        var container = !string.IsNullOrEmpty(entry.Container) ? entry.Container : name.Container ?? "";

        byte[] bytes;
        try { bytes = entry.Read(); }
        catch (Exception e)
        {
            log.LogWarning("{File}: {Message}", entry.LogicalPath, e.Message);
            unreadable.Add(entry.Name);
            return null;
        }

        var garment = new Garment
        {
            Id = Garment.MakeId(pack, container, name.Slot, name.Number, name.Suffix),
            PackName = pack,
            Container = container,
            Slot = name.Slot,
            Number = name.Number,
            Suffix = name.Suffix,
            IsProp = name.IsProp,
            GameFormat = GameFormats.FromHeader(Rsc7Header.IsEnhanced(bytes, ".ydd")),
            ModelPath = entry.LogicalPath,
            ChangeStamp = entry.ChangeStamp,
            ModelSize = bytes.Length,
            ModelSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
        };

        var fingerprint = geometry.Compute(bytes);
        if (fingerprint.IsFailure) log.LogWarning("{File}: {Message}", entry.LogicalPath, fingerprint.Error.Message);
        garment.Geometry = fingerprint.IsSuccess ? fingerprint.Value : new GeometryFingerprint();
        return garment;
    }

    /// <summary>A garment from the previous catalog: name and container recomputed (cheap), fingerprint copied without reading the file.</summary>
    static Garment? ModelFromCatalog(SourceEntry entry, string pack, Garment known)
    {
        var name = ClothingFileName.ParseModel(entry.Name);
        if (name == null) return null;

        var container = !string.IsNullOrEmpty(entry.Container) ? entry.Container : name.Container ?? "";
        return new Garment
        {
            Id = Garment.MakeId(pack, container, name.Slot, name.Number, name.Suffix),
            PackName = pack,
            Container = container,
            Slot = name.Slot,
            Number = name.Number,
            Suffix = name.Suffix,
            IsProp = name.IsProp,
            GameFormat = known.GameFormat,
            ModelPath = entry.LogicalPath,
            ChangeStamp = entry.ChangeStamp,
            ModelSize = known.ModelSize,
            ModelSha256 = known.ModelSha256,
            Geometry = known.Geometry,
        };
    }

    TextureInfo? ReadTexture(SourceEntry entry, IndexOptions options, ConcurrentBag<string> unreadable)
    {
        byte[] bytes;
        try { bytes = entry.Read(); }
        catch (Exception e)
        {
            // The name said this is a clothing texture, so losing it makes the garment score lower than it
            // should — that is a wrong answer, not a missing file, and it has to be reported as one.
            log.LogWarning("{File}: {Message}", entry.LogicalPath, e.Message);
            unreadable.Add(entry.Name);
            return null;
        }

        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        Action<byte[], int, int>? onPixels = options.ThumbnailFolder == null
            ? null
            : (pixels, width, height) => WriteThumbnail(options.ThumbnailFolder, sha, pixels, width, height);

        var fingerprint = textures.Compute(bytes, onPixels);
        var texture = fingerprint.IsSuccess ? fingerprint.Value : new TextureInfo();

        texture.FileName = entry.Name;
        texture.Path = entry.LogicalPath;
        texture.Size = bytes.Length;
        texture.Sha256 = sha;
        texture.ChangeStamp = entry.ChangeStamp;
        return texture;
    }

    /// <summary>A reused texture still needs its thumbnail on disk, or the report and the catalog show a gap.</summary>
    static bool ThumbnailIsThere(string? folder, TextureInfo texture)
        => folder == null || texture.Sha256 == null || !texture.IsDecoded
           || File.Exists(Path.Combine(folder, texture.Sha256 + ".png"));

    /// <summary>Side of the thumbnails the catalog grid shows, in pixels.</summary>
    const int ThumbnailSide = 128;

    void WriteThumbnail(string folder, string sha, byte[] pixels, int width, int height)
    {
        var file = Path.Combine(folder, sha + ".png");
        if (File.Exists(file)) return;
        try
        {
            Directory.CreateDirectory(folder);
            var thumbnail = Thumbnail.FromPixels(pixels, width, height, ThumbnailSide);
            File.WriteAllBytes(file, PngWriter.Rgb(thumbnail, ThumbnailSide, ThumbnailSide));
        }
        catch (IOException)
        {
            // two threads racing for the same sha: the loser does nothing, the file is there either way
        }
    }
}
