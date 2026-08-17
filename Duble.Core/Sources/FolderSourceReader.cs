#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Duble.Core.Sources;

/// <summary>
/// Reads a folder: the loose .ydd and .ytd files in it, plus everything inside any .rpf archive it contains.
///
/// A source can be an unpacked pack, where containers are FOLDERS whose name ends in .rpf, or a folder holding
/// real archives (stream\civil01_female.rpf, dlcpacks\x\dlc.rpf). Both shapes appear in the wild, so both are
/// handled here.
/// </summary>
public sealed class FolderSourceReader : ISourceReader
{
    readonly ArchiveSourceReader archives;

    public FolderSourceReader(ArchiveSourceReader archives) => this.archives = archives;

    public IReadOnlyList<SourceEntry> Read(string root)
    {
        var entries = new List<SourceEntry>();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (BinFolder.Contains(root, file)) continue;

            var extension = Path.GetExtension(file);
            if (extension.Equals(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                entries.AddRange(archives.Read(file));
                continue;
            }
            if (!extension.Equals(".ydd", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".ytd", StringComparison.OrdinalIgnoreCase)) continue;

            var info = new FileInfo(file);
            entries.Add(new SourceEntry(
                Name: Path.GetFileName(file),
                Container: ContainerOf(root, file),
                LogicalPath: file,
                Length: info.Length,
                ChangeStamp: info.Length + "|" + info.LastWriteTimeUtc.Ticks,
                Read: () => File.ReadAllBytes(file)));
        }

        return entries;
    }

    /// <summary>The nearest ancestor folder whose name ends in .rpf — in an unpacked pack that is the container.</summary>
    static string ContainerOf(string root, string file)
    {
        var folder = Path.GetDirectoryName(file);
        while (!string.IsNullOrEmpty(folder) && folder.Length >= root.Length)
        {
            var name = Path.GetFileName(folder);
            if (name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) return name;
            folder = Path.GetDirectoryName(folder);
        }
        return "";
    }
}

/// <summary>Picks a reader by what the path is: an .rpf file is an archive, anything else is a folder.</summary>
public sealed class SourceReaderFactory : ISourceReaderFactory
{
    readonly FolderSourceReader folders;
    readonly ArchiveSourceReader archives;

    public SourceReaderFactory(FolderSourceReader folders, ArchiveSourceReader archives)
    {
        this.folders = folders;
        this.archives = archives;
    }

    public Results.Result<ISourceReader> For(string path)
    {
        if (Directory.Exists(path)) return Results.Result<ISourceReader>.Ok(folders);
        if (File.Exists(path)) return Results.Result<ISourceReader>.Ok(archives);
        return Results.Result<ISourceReader>.Fail(Results.ErrorCodes.SourceMissing, "no such source: " + path);
    }
}
