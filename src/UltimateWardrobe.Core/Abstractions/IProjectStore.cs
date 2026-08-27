using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Core.Abstractions;

public interface IProjectStore
{
    Task SaveAsync(Project project, CancellationToken cancellationToken = default);
    Task<Project> LoadAsync(string projectDbPath, CancellationToken cancellationToken = default);
}
