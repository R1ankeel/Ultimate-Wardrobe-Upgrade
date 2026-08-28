using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Persistence;

namespace UltimateWardrobe.App.Services;

/// <summary>
/// Factory for <see cref="IProjectStore"/> instances bound to a concrete <c>project.db</c>
/// (Phase 6 Sprint 6.1). One store per database - <see cref="ProjectStore"/> carries the path in
/// its constructor while <see cref="IProjectStore.LoadAsync(string, CancellationToken)"/> loads any
/// file, so the picker can open a store before the session is set.
/// </summary>
public interface IProjectStoreFactory
{
    IProjectStore Open(string projectDbPath);
}

/// <summary>
/// Default <see cref="IProjectStoreFactory"/> wrapping <see cref="ProjectStore"/>.
/// </summary>
public sealed class ProjectStoreFactory : IProjectStoreFactory
{
    public IProjectStore Open(string projectDbPath)
    {
        return new ProjectStore(projectDbPath);
    }
}