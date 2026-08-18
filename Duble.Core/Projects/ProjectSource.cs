namespace Duble.Core.Projects;

/// <summary>One place a project reads garments from.</summary>
public class ProjectSource
{
    public string? Id { get; set; }

    public string? Path { get; set; }

    /// <summary>Folder or file name, made unique within the project — the catalog groups garments by pack name.</summary>
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    public SourceKind Kind { get; set; }

    /// <summary>Filled in by indexing; Unknown until then.</summary>
    public SourceFormat Format { get; set; }

    public string? IndexedAt { get; set; }
}
