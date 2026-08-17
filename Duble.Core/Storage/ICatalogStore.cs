#nullable enable
using Duble.Core.Model;
using Duble.Core.Results;

namespace Duble.Core.Storage;

/// <summary>Reads and writes the catalog. The model itself knows nothing about files.</summary>
public interface ICatalogStore
{
    /// <summary>
    /// The catalog at that path, or an empty one when the file is missing, unreadable or written by an older
    /// version of Duble. The catalog lives in the project's .cache folder and is always reproducible by
    /// indexing again, so an unreadable one is not an error — it is a re-index.
    /// </summary>
    Catalog Load(string path);

    Result Save(Catalog catalog, string path);
}
