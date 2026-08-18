#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using Duble.Core.Formats;

namespace Duble.Core.Sources;

/// <summary>Reads a single .rpf archive: every .ydd and .ytd inside it, nested archives included.</summary>
public sealed class ArchiveSourceReader : ISourceReader
{
    public ArchiveSourceReader(CodeWalkerRuntime runtime) => _ = runtime;

    public IReadOnlyList<SourceEntry> Read(string path)
    {
        var entries = new List<SourceEntry>();
        if (!File.Exists(path)) return entries;

        var archiveInfo = new FileInfo(path);
        var rpf = new RpfFile(path, Path.GetFileName(path));
        rpf.ScanStructure(_ => { }, _ => { });

        void Walk(RpfFile file)
        {
            foreach (var entry in file.AllEntries?.OfType<RpfFileEntry>() ?? Enumerable.Empty<RpfFileEntry>())
            {
                var extension = Path.GetExtension(entry.Name);
                if (!extension.Equals(".ydd", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".ytd", StringComparison.OrdinalIgnoreCase)) continue;

                var owner = entry.File;
                entries.Add(new SourceEntry(
                    Name: entry.Name,
                    Container: Path.GetFileName(file.Path),
                    // remember the archive and the path inside it, so a report can pull the same texture out
                    // a second time for its thumbnail
                    LogicalPath: path + "|" + entry.Path,
                    Length: entry.GetFileSize(),
                    // an entry's stamp includes the archive's own size and date: touch the archive and
                    // everything in it is read again
                    ChangeStamp: entry.GetFileSize() + "|" + archiveInfo.Length + "|" + archiveInfo.LastWriteTimeUtc.Ticks,
                    // ExtractFile hands back the resource without its RSC7 header — Rsc7Header puts it back, so that
                    // LoadResourceFile reads it exactly as it would a file on disk
                    Read: () => Rsc7Header.Wrap(entry, owner.ExtractFile(entry))!));
            }

            foreach (var child in file.Children ?? new List<RpfFile>()) Walk(child);
        }

        Walk(rpf);
        return entries;
    }
}
