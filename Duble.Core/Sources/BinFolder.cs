#nullable enable
using System;
using System.IO;

namespace Duble.Core.Sources;

/// <summary>
/// The folder rejected files are moved to. Indexing skips it, so that what the user has already rejected does
/// not come back as a pack of its own on the next run.
/// </summary>
public static class BinFolder
{
    public const string Name = "_odrzucone";

    /// <summary>True when the path has a bin folder segment between the root and the file name.</summary>
    public static bool Contains(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals(Name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
