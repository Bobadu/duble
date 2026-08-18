using Duble.Core.Comparison;

namespace Duble.Core.Projects;

/// <summary>Project settings. Null means "the default", which keeps the project file small and honest.</summary>
public class ProjectSettings
{
    /// <summary>Where rejected files go; null = a _rejected folder next to each source.</summary>
    public string? BinFolder { get; set; }

    /// <summary>Comparison thresholds; null = <see cref="Thresholds.Default"/>.</summary>
    public Thresholds? Thresholds { get; set; }
}
