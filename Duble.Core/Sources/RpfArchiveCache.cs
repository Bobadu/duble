#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Core.Formats;
using Duble.Core.Results;

namespace Duble.Core.Sources;

/// <summary>
/// Reads a file that has already been indexed, a second time.
///
/// The catalog stores a logical path for every file: a plain path for a loose file, and
/// "path\to\archive.rpf|path\inside" for an entry in an archive. A report or a preview has to get at the same
/// bytes again — including when the source was an .rpf straight off the internet rather than an unpacked
/// folder — and scanning an archive's structure is expensive, so open archives are kept.
/// </summary>
public interface IArchiveCache : IDisposable
{
    /// <summary>The bytes behind a logical path, or a failure when the file or the entry is gone.</summary>
    Result<byte[]> Read(string logicalPath);

    /// <summary>
    /// Forgets every open archive. Call this when a project closes, and after an apply — moving files leaves
    /// the scanned structure describing an archive that is no longer where it was.
    /// </summary>
    void Clear();
}

/// <inheritdoc />
public sealed class RpfArchiveCache : IArchiveCache
{
    readonly ConcurrentDictionary<string, RpfFile> open = new(StringComparer.OrdinalIgnoreCase);

    public RpfArchiveCache(CodeWalkerRuntime runtime) => _ = runtime;

    public Result<byte[]> Read(string logicalPath)
    {
        if (string.IsNullOrEmpty(logicalPath))
            return Result<byte[]>.Fail(ErrorCodes.SourceMissing, "no path given");

        int separator = logicalPath.IndexOf('|');
        if (separator < 0)
            return File.Exists(logicalPath)
                ? Read(() => File.ReadAllBytes(logicalPath), logicalPath)
                : Result<byte[]>.Fail(ErrorCodes.SourceMissing, logicalPath);

        var archivePath = logicalPath[..separator];
        var insidePath = logicalPath[(separator + 1)..];

        if (!open.TryGetValue(archivePath, out var archive))
        {
            if (!File.Exists(archivePath))
                return Result<byte[]>.Fail(ErrorCodes.SourceMissing, archivePath);
            try
            {
                archive = new RpfFile(archivePath, Path.GetFileName(archivePath));
                archive.ScanStructure(_ => { }, _ => { });
            }
            catch (Exception e)
            {
                return Result<byte[]>.Fail(ErrorCodes.ArchiveUnreadable, $"{archivePath}: {e.Message}");
            }
            archive = open.GetOrAdd(archivePath, archive);
        }

        var entry = Entries(archive).FirstOrDefault(e => string.Equals(e.Path, insidePath, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return Result<byte[]>.Fail(ErrorCodes.SourceMissing, logicalPath);

        // ExtractFile hands back the resource without its RSC7 header; Rsc7Header puts it back so that the bytes
        // read exactly like a file on disk
        return Read(() => Rsc7Header.Wrap(entry, entry.File.ExtractFile(entry))!, logicalPath);
    }

    public void Clear() => open.Clear();

    public void Dispose() => Clear();

    static Result<byte[]> Read(Func<byte[]> read, string what)
    {
        try
        {
            return Result<byte[]>.Ok(read());
        }
        catch (Exception e)
        {
            return Result<byte[]>.Fail(ErrorCodes.ArchiveUnreadable, $"{what}: {e.Message}");
        }
    }

    static IEnumerable<RpfFileEntry> Entries(RpfFile file)
    {
        foreach (var entry in file.AllEntries?.OfType<RpfFileEntry>() ?? Enumerable.Empty<RpfFileEntry>())
            yield return entry;
        foreach (var child in file.Children ?? new List<RpfFile>())
            foreach (var entry in Entries(child))
                yield return entry;
    }
}
