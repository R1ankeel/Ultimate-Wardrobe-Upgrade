using System.IO;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.App.Services;

/// <summary>
/// Single open project carried across the shell (Phase 6 amendments 1 + 7): one project, opened
/// once at the startup gate, cleared only when the application exits. View models read
/// <see cref="Project"/> never and always refetch per operation; the picker sets it via
/// <see cref="Open"/>.
/// </summary>
public interface IProjectSession
{
    Project? Project { get; }

    string? DatabasePath { get; }

    bool IsOpen { get; }

    void Open(Project project, string databasePath);

    void Clear();
}

/// <summary>
/// Default <see cref="IProjectSession"/>: a singleton service (Phase 6 Sprint 6.1).
/// </summary>
public sealed class ProjectSession : IProjectSession
{
    public Project? Project { get; private set; }

    public string? DatabasePath { get; private set; }

    public bool IsOpen => Project is not null;

    public void Open(Project project, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));
        }

        Project = project;
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public void Clear()
    {
        Project = null;
        DatabasePath = null;
    }
}