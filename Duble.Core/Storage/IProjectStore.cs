using Duble.Core.Projects;
using Duble.Core.Results;

namespace Duble.Core.Storage;

/// <summary>Reads and writes the .duble project file.</summary>
public interface IProjectStore
{
    /// <summary>
    /// The project at that path, or a failure carrying a code: missing or unparseable is project.unreadable,
    /// and a file written by another version of Duble is project.unsupported_version. Duble reads exactly the
    /// version it writes.
    /// </summary>
    Result<Project> Load(string path);

    Result Save(Project project);
}
