using System;

namespace Duble.Core.Sources;

/// <summary>
/// One file a source offers, whether it sits loose in a folder or inside an .rpf archive.
/// </summary>
/// <param name="Name">The file name on its own.</param>
/// <param name="Container">Name of the .rpf folder or archive it sits in, or "" for a loose file.</param>
/// <param name="LogicalPath">
/// Where to find it again: a plain path, or "path\to\archive.rpf|path\inside" for an archive entry.
/// </param>
/// <param name="Length">Size in bytes.</param>
/// <param name="ChangeStamp">Size and timestamp; indexing skips a file whose stamp has not moved.</param>
/// <param name="Read">Reads the bytes. For an archive entry this extracts and re-attaches the RSC7 header.</param>
public sealed record SourceEntry(
    string Name,
    string Container,
    string LogicalPath,
    long Length,
    string ChangeStamp,
    Func<byte[]> Read);
