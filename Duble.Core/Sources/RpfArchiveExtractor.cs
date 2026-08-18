#nullable enable
// Unpacking a source into a plain folder: an .rpf becomes a folder named "<name>.rpf\" with the files inside.
//
// Why: Duble only reads archives, never writes to them, so an apply cannot move a file that lives inside one.
// An unpacked copy is an ordinary folder — it indexes the same way (a container is a folder whose name ends in
// .rpf), it can be tidied with apply and undo, and it can be packed again with whatever tool the pack came
// from. The original is left untouched.
//
// Resources (.ydd, .ytd and the rest) are written as RSC7 files: a 16-byte header (version, page flags)
// followed by a deflate payload — exactly what CodeWalker and OpenIV export, so our indexer, CodeWalker and
// FiveM's stream folder all read them. Binary files (.meta, .ymt, .xml…) are written as they came out. Nested
// archives (dlc.rpf\x64\...\pack.rpf) become subfolders.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeWalker.GameFiles;

namespace Duble.Core.Sources;

/// <summary>What one unpacking produced.</summary>
public sealed class ExtractionResult
{
    /// <summary>Where the copy was written.</summary>
    public string Folder { get; set; } = "";

    public int Files { get; set; }

    /// <summary>How many archives were opened, nested ones included.</summary>
    public int Archives { get; set; }

    public long Bytes { get; set; }

    /// <summary>What went wrong along the way; unpacking carries on past a file it cannot read.</summary>
    public List<string> Errors { get; } = new();
}

/// <summary>Writes a readable, writable copy of a source that lives inside .rpf archives.</summary>
public interface IArchiveExtractor
{
    /// <summary>Unpacks one archive, nested archives included, into a folder holding its root contents.</summary>
    ExtractionResult ExtractArchive(string archivePath, string targetFolder,
                                    IProgress<ProgressReport>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Copies a whole source: ordinary files are copied, .rpf archives are unpacked into subfolders of the
    /// same name, and the bin folder is skipped. A source that is itself an .rpf goes through
    /// <see cref="ExtractArchive"/>.
    /// </summary>
    ExtractionResult ExtractSource(string sourcePath, string targetFolder,
                                   IProgress<ProgressReport>? progress = null, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class RpfArchiveExtractor : IArchiveExtractor
{
    public RpfArchiveExtractor(Formats.CodeWalkerRuntime runtime) => _ = runtime;

    /// <summary>An archive entry as bytes for disk: a resource gets its RSC7 header back, a binary file is unchanged.</summary>
    public static byte[]? ToRsc7File(RpfFileEntry entry, byte[]? data)
    {
        if (data == null) return null;
        return entry is RpfResourceFileEntry resource
            ? ResourceBuilder.AddResourceHeader(resource, ResourceBuilder.Compress(data))
            : data;
    }

    public ExtractionResult ExtractArchive(string archivePath, string targetFolder,
                                           IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        var result = new ExtractionResult { Folder = targetFolder };
        if (!File.Exists(archivePath))
        {
            result.Errors.Add("brak archiwum: " + archivePath);
            return result;
        }

        var archive = new RpfFile(archivePath, Path.GetFileName(archivePath));
        archive.ScanStructure(_ => { }, message => result.Errors.Add("[scan] " + message));
        Unpack(archive, targetFolder, result, progress, ct);
        return result;
    }

    public ExtractionResult ExtractSource(string sourcePath, string targetFolder,
                                          IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
    {
        if (File.Exists(sourcePath)) return ExtractArchive(sourcePath, targetFolder, progress, ct);

        var result = new ExtractionResult { Folder = targetFolder };
        if (!Directory.Exists(sourcePath))
        {
            result.Errors.Add("brak folderu: " + sourcePath);
            return result;
        }

        var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Where(f => !BinFolder.Contains(sourcePath, f))
            .ToList();

        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourcePath, file);
            progress?.Report(new ProgressReport("unpack", done++, files.Count, relative));

            var target = Path.Combine(targetFolder, relative);
            try
            {
                if (file.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    var nested = ExtractArchive(file, target, null, ct);
                    result.Files += nested.Files;
                    result.Archives += nested.Archives;
                    result.Bytes += nested.Bytes;
                    result.Errors.AddRange(nested.Errors);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? targetFolder);
                    File.Copy(file, target, true);
                    result.Files++;
                    result.Bytes += new FileInfo(file).Length;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { result.Errors.Add($"{relative}: {e.Message}"); }
        }

        progress?.Report(new ProgressReport("unpack", files.Count, files.Count, null));
        return result;
    }

    static void Unpack(RpfFile archive, string targetFolder, ExtractionResult result,
                       IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        result.Archives++;

        // Every file entry of this archive and of the ones nested in it. A path relative to the target is the
        // entry's own path with the archive's root prefix removed.
        var root = (archive.Path ?? "").ToLowerInvariant();
        var entries = new List<(RpfFileEntry Entry, RpfFile Owner)>();

        void Collect(RpfFile file)
        {
            foreach (var entry in file.AllEntries?.OfType<RpfFileEntry>() ?? Enumerable.Empty<RpfFileEntry>())
            {
                // a nested archive is reached through Children, not as a binary file
                if (entry is RpfBinaryFileEntry && entry.NameLower.EndsWith(".rpf")) continue;
                entries.Add((entry, file));
            }

            foreach (var child in file.Children ?? new List<RpfFile>())
            {
                result.Archives++;
                Collect(child);
            }
        }

        Collect(archive);

        int done = 0;
        foreach (var (entry, owner) in entries)
        {
            ct.ThrowIfCancellationRequested();

            var path = entry.Path ?? entry.Name;
            if (path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) path = path[(root.Length + 1)..];
            else if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) path = entry.Name;

            progress?.Report(new ProgressReport("unpack", done++, entries.Count, path));

            try
            {
                var extracted = owner.ExtractFile(entry);
                if (extracted == null)
                {
                    result.Errors.Add($"{path}: nie udalo sie wyciagnac ({owner.LastError})");
                    continue;
                }

                var bytes = ToRsc7File(entry, extracted)!;
                var target = Path.Combine(targetFolder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? targetFolder);

                // write beside it and move into place, so a half-written file never looks finished
                var temporary = target + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, target, true);

                result.Files++;
                result.Bytes += bytes.Length;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { result.Errors.Add($"{path}: {e.Message}"); }
        }

        progress?.Report(new ProgressReport("unpack", entries.Count, entries.Count, null));
    }
}
