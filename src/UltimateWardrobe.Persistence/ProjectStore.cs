using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Persistence.Repositories;

namespace UltimateWardrobe.Persistence;

/// <summary>
/// <see cref="IProjectStore"/> bound to a single <c>project.db</c> (Phase 4 Sprint 4.3 - roadmap
/// section 6.5 tasks 4.1/4.3 + section 6.4 unit-of-work). <c>SaveAsync</c>/<c>LoadAsync</c> are the
/// coarse whole-graph facade over the Sprint 4.2 repositories: one <see cref="UnitOfWork"/>
/// transaction saves the Project, its <c>Library.Assets</c>, every <see cref="Overhaul"/> with its
/// <c>Mappings</c> and per-overhaul <see cref="Catalog"/> cache; <c>LoadAsync</c> reopens the file
/// (open + migrate) and rebuilds the full graph from the rows.
///
/// Because the <see cref="IProjectStore.SaveAsync(Project, CancellationToken)"/> contract carries no
/// database path, the store is constructed bound to a path (one store per project DB); the
/// path-taking <see cref="LoadAsync(string, CancellationToken)"/> lets the caller load any file.
/// A missing project (a DB with no <c>Project</c> row or a nonexistent file that only just got
/// migrated into an empty schema) surfaces as a typed <see cref="ProjectStoreException"/>, never a
/// crash.
/// </summary>
public sealed class ProjectStore : IProjectStore
{
    private readonly string _dbPath;

    public ProjectStore(string projectDbPath)
    {
        if (string.IsNullOrWhiteSpace(projectDbPath))
        {
            throw new ArgumentException("Project database path must not be empty.", nameof(projectDbPath));
        }
        _dbPath = Path.GetFullPath(projectDbPath);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var database = await ProjectDatabase.OpenAsync(_dbPath, cancellationToken).ConfigureAwait(false);
        var unitOfWork = new UnitOfWork(database.Connection);

        await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveGraphAsync(unitOfWork, project, cancellationToken).ConfigureAwait(false);
            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Project> LoadAsync(string projectDbPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectDbPath))
        {
            throw new ArgumentException("Project database path must not be empty.", nameof(projectDbPath));
        }

        var fullPath = Path.GetFullPath(projectDbPath);
        await using var database = await ProjectDatabase.OpenAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var unitOfWork = new UnitOfWork(database.Connection);
        return await LoadGraphAsync(unitOfWork, fullPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whole-graph, single-transaction, upsert-only save (issue 3 - no delete-then-reinsert). Every
    /// write is stable by domain id; <c>PieceMapping</c> replaces on its DB <c>UniqueKey</c> and the
    /// per-overhaul catalog replaces in place, so an idempotent save never orphans dependent rows.
    /// </summary>
    private static async Task SaveGraphAsync(UnitOfWork unitOfWork, Project project, CancellationToken cancellationToken)
    {
        var projectRepository = new ProjectRepository(unitOfWork);
        var assetRepository = new DonorAssetRepository(unitOfWork);
        var overhaulRepository = new OverhaulRepository(unitOfWork);
        var mappingRepository = new PieceMappingRepository(unitOfWork);
        var catalogCacheRepository = new CatalogCacheRepository(unitOfWork);

        await projectRepository.UpsertAsync(project, cancellationToken).ConfigureAwait(false);

        foreach (var asset in project.Library.Assets)
        {
            await assetRepository.UpsertAsync(asset, project.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var overhaul in project.Overhauls)
        {
            await overhaulRepository.UpsertAsync(overhaul, cancellationToken).ConfigureAwait(false);

            foreach (var mapping in overhaul.Mappings)
            {
                await mappingRepository.UpsertAsync(mapping, cancellationToken).ConfigureAwait(false);
            }

            if (overhaul.Catalog is { } catalog)
            {
                await catalogCacheRepository.UpsertAsync(overhaul.Id, catalog, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Rebuilds the full <see cref="Project"/> graph: the Project row, its <c>Library.Assets</c>,
    /// and each <see cref="Overhaul"/> with its <c>Mappings</c> and cached <see cref="Catalog"/>.
    /// An <see cref="Overhaul"/> is reconstructed (rather than re-used from its repository read)
    /// because <see cref="Overhaul.Catalog"/> is an <c>init</c>-only property that must be set at
    /// construction, after its catalog cache is read.
    /// </summary>
    private static async Task<Project> LoadGraphAsync(UnitOfWork unitOfWork, string fullPath, CancellationToken cancellationToken)
    {
        var projectRepository = new ProjectRepository(unitOfWork);
        var assetRepository = new DonorAssetRepository(unitOfWork);
        var overhaulRepository = new OverhaulRepository(unitOfWork);
        var mappingRepository = new PieceMappingRepository(unitOfWork);
        var catalogCacheRepository = new CatalogCacheRepository(unitOfWork);

        var projects = await projectRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (projects.Count == 0)
        {
            throw new ProjectStoreException($"No Project found in database '{fullPath}' - it is empty or has not been saved yet.");
        }

        var project = projects[0];
        var assets = await assetRepository.GetByProjectAsync(project.Id, cancellationToken).ConfigureAwait(false);
        project.Library.Assets.AddRange(assets);

        var overhauls = await overhaulRepository.GetByProjectAsync(project.Id, cancellationToken).ConfigureAwait(false);
        foreach (var overhaul in overhauls)
        {
            var cache = await catalogCacheRepository.GetAsync(overhaul.Id, cancellationToken).ConfigureAwait(false);
            var rebuilt = new Overhaul(
                overhaul.Id,
                overhaul.Name,
                overhaul.ProjectId,
                overhaul.Source)
            {
                Policy = overhaul.Policy,
                CreatedAt = overhaul.CreatedAt,
                ModifiedAt = overhaul.ModifiedAt,
                Catalog = cache?.Catalog,
            };

            var mappings = await mappingRepository.GetByOverhaulAsync(overhaul.Id, cancellationToken).ConfigureAwait(false);
            rebuilt.Mappings.AddRange(mappings);
            project.Overhauls.Add(rebuilt);
        }

        return project;
    }
}
