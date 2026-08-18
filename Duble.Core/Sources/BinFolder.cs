using System;
using System.IO;

namespace Duble.Core.Sources;

/// <summary>
/// The folder rejected files are moved to. Indexing skips it, so that what the user has already rejected does
/// not come back as a pack of its own on the next run.
///
/// The name is part of what a user sees in their wardrobe folder, so it is spelled in a few places outside
/// this one — the settings screen and the apply screen both show it. A test keeps those in step with this.
/// </summary>
public static class BinFolder
{
    public const string Name = "_rejected";

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
