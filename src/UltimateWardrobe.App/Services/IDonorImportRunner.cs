using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.App.Services;

/// <summary>
/// Coarse per-file progress for a donor import batch (Phase 6 Sprint 6.3): how many of the total
/// archives have been extracted + classified + appended so far.
/// </summary>
public sealed record DonorImportProgress(int FilesDone, int TotalFiles);

/// <summary>
/// App-layer seam that imports a batch of donor archives one at a time through the Phase 2 pipeline
/// (<see cref="DonorLibraryService.ImportAsync"/> - extract -> cross-project guard -> classify ->
/// append), raising <see cref="IProgress{T}"/> between files and passing the cancellation token
/// through. This abstraction keeps the ViewModel headless-testable: tests stub it to raise progress
/// and to force per-file success/failure without touching real archives. (Phase 6 Sprint 6.3.)
/// </summary>
public interface IDonorImportRunner
{
    /// <summary>
    /// Imports each archive into <paramref name="library"/> via the Phase 2 pipeline and the
    /// <paramref name="catalogHint"/> (the project's vanilla hint). Archives are processed in order;
    /// progress is reported after each file. A failing archive aborts the batch - its extractor
    /// already cleaned up and it appended nothing, so the library stays unchanged for that file.
    /// </summary>
    Task<IReadOnlyList<DonorAsset>> ImportAsync(
        IReadOnlyList<string> archivePaths,
        string projectRoot,
        UltimateWardrobe.Core.Domain.DonorLibrary library,
        Catalog? catalogHint,
        IProgress<DonorImportProgress>? progress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IDonorImportRunner"/> over the real <see cref="DonorLibraryService"/>
/// (Phase 6 Sprint 6.3). Supported archive extensions are <c>.7z / .zip / .rar</c>, compared
/// case-insensitively; anything else is skipped with a warning rather than failing the batch.
/// </summary>
public sealed class DonorImportRunner : IDonorImportRunner
{
    private readonly DonorLibraryService _service;
    private readonly ILogger<DonorImportRunner> _logger;

    public DonorImportRunner(DonorLibraryService service, ILogger<DonorImportRunner>? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? NullLogger<DonorImportRunner>.Instance;
    }

    public async Task<IReadOnlyList<DonorAsset>> ImportAsync(
        IReadOnlyList<string> archivePaths,
        string projectRoot,
        UltimateWardrobe.Core.Domain.DonorLibrary library,
        Catalog? catalogHint,
        IProgress<DonorImportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (archivePaths is null) throw new ArgumentNullException(nameof(archivePaths));
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root must not be empty.", nameof(projectRoot));

        var supported = archivePaths
            .Where(p => !string.IsNullOrWhiteSpace(p) && IsSupportedArchive(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (supported.Count == 0)
        {
            return Array.Empty<DonorAsset>();
        }

        var skipped = archivePaths.Count - supported.Count;
        if (skipped > 0)
        {
            _logger.LogWarning("Skipped {Skipped} unsupported donor archives.", skipped);
        }

        var imported = new List<DonorAsset>();
        var done = 0;
        foreach (var archivePath in supported)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var asset = await _service.ImportAsync(
                archivePath, projectRoot, library, catalogHint, cancellationToken);
            imported.Add(asset);
            done++;
            progress?.Report(new DonorImportProgress(done, supported.Count));
            _logger.LogInformation("Imported donor file {Done}/{Total}: '{File}'.", done, supported.Count, Path.GetFileName(archivePath));
        }

        return imported;
    }

    /// <summary>
    /// True when the file has a supported donor archive extension (<c>.7z / .zip / .rar</c>,
    /// case-insensitive) - the drop-zone filter (Phase 6 Sprint 6.3).
    /// </summary>
    public static bool IsSupportedArchive(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return Path.GetExtension(path).Equals(".7z", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".rar", StringComparison.OrdinalIgnoreCase);
    }
}
