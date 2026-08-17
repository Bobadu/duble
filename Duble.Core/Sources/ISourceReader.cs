#nullable enable
using System.Collections.Generic;
using Duble.Core.Results;

namespace Duble.Core.Sources;

/// <summary>Lists the model and texture files a source holds.</summary>
public interface ISourceReader
{
    /// <summary>Every .ydd and .ytd in the source, archives included, the bin folder skipped.</summary>
    IReadOnlyList<SourceEntry> Read(string path);
}

/// <summary>Picks the reader that fits a path: a folder, or a single .rpf archive.</summary>
public interface ISourceReaderFactory
{
    Result<ISourceReader> For(string path);
}
