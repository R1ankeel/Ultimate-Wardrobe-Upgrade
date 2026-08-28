using System.IO;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.App.Services;

/// <summary>
/// Single open project carried across the shell (Phase 6 amendments 1 + 7): one project, opened
/// once at the startup gate, cleared only when the application exits. View models read
/// <see cref="Project"/> never and always refetch per operation; the picker sets it via
/// <see cref="Open"/>. <see cref="Store"/> is the one <see cref="IProjectStore"/> bound to the
/// opened <c>project.db</c> and shared by every view model so autosave (amendment 3) never races
/// two stores on the same file.
/// </summary>
public interface IProjectSession
{
    Project? Project { get; }

    string? DatabasePath { get; }

    IProjectStore? Store { get; }

    bool IsOpen { get; }

    void Open(Project project, string databasePath, IProjectStore? store = null);

    void Clear();
}

/// <summary>
/// Default <see cref="IProjectSession"/>: a singleton service (Phase 6 Sprint 6.1 / Sprint 6.2
/// store sharing).
/// </summary>
public sealed class ProjectSession : IProjectSession
{
    private readonly IProjectStoreFactory? _storeFactory;

    public ProjectSession(IProjectStoreFactory? storeFactory = null)
    {
        _storeFactory = storeFactory;
    }

    public Project? Project { get; private set; }

    public string? DatabasePath { get; private set; }

    public IProjectStore? Store { get; private set; }

    public bool IsOpen => Project is not null;

    public void Open(Project project, string databasePath, IProjectStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));
        }

        Project = project;
        DatabasePath = Path.GetFullPath(databasePath);
        Store = store ?? _storeFactory?.Open(DatabasePath);
    }

    public void Clear()
    {
        Project = null;
        DatabasePath = null;
        Store = null;
    }
}