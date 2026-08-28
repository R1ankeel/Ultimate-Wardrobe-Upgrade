using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Thrown when importing a donor archive that is already owned by this or another
/// <see cref="UltimateWardrobe.Core.Domain.DonorLibrary"/> (cross-project guard, Sprint 2.4.4). The guard is keyed on the
/// archive's SHA-256 hash - the same archive cannot belong to two projects (Phase 0.2 invariant).
/// </summary>
public sealed class DonorAlreadyOwnedException : InvalidOperationException
{
    public Guid OwnerProjectId { get; }

    public DonorAlreadyOwnedException(Guid ownerProjectId, string archiveHash)
        : base($"Donor archive (hash {archiveHash}) is already owned by project {ownerProjectId}.")
    {
        OwnerProjectId = ownerProjectId;
    }
}

/// <summary>
/// End-to-end donor library service (Sprint 2.4): wires the real archive extractor
/// (<see cref="DonorImportService"/>) to graduated classification (<see cref="IDonorClassifier"/>)
/// and maintains the <see cref="UltimateWardrobe.Core.Domain.DonorLibrary.Assets"/> list inside a project.
/// </summary>
public sealed class DonorLibraryService
{
    private readonly DonorImportService _import;
    private readonly IDonorClassifier _classifier;
    private readonly ILogger<DonorLibraryService> _logger;

    public DonorLibraryService(
        DonorImportService? import = null,
        IDonorClassifier? classifier = null,
        ILogger<DonorLibraryService>? logger = null)
    {
        _import = import ?? new DonorImportService();
        _classifier = classifier ?? new DonorClassifier();
        _logger = logger ?? NullLogger<DonorLibraryService>.Instance;
    }

    /// <summary>
    /// Imports a donor archive into the project's library: extracts via the archive extractor,
    /// classifies the extracted <c>Source/&lt;ImportId&gt;/</c> folder, merges the real archive
    /// identity (file name, hash, timestamps) with the classification result, enforces the
    /// cross-project guard, and appends to <see cref="UltimateWardrobe.Core.Domain.DonorLibrary.Assets"/>.
    /// <paramref name="otherLibraries"/> supplies the libraries of other projects so the guard can
    /// reject an archive already owned by them.
    /// </summary>
    public async Task<DonorAsset> ImportAsync(
        string archivePath,
        string projectRoot,
        UltimateWardrobe.Core.Domain.DonorLibrary library,
        Catalog? catalogHint = null,
        CancellationToken cancellationToken = default,
        IEnumerable<UltimateWardrobe.Core.Domain.DonorLibrary>? otherLibraries = null)
    {
        if (library is null) throw new ArgumentNullException(nameof(library));

        DonorAsset imported = null!;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            imported = await _import.ImportAsync(archivePath, projectRoot, cancellationToken);

            EnsureNotOwnedElsewhere(library, imported.ArchiveHash, otherLibraries);

            var classified = await _classifier.ClassifyAsync(imported.ExtractedPath, catalogHint, cancellationToken);

            var merged = MergeIdentity(imported, classified);

            library.Assets.Add(merged);

            _logger.LogInformation(
                "Imported donor {ImportId} '{FileName}' (hash {Hash}) into project {ProjectId}; Kind {Kind}",
                merged.ImportId,
                merged.OriginalFileName,
                merged.ArchiveHash,
                library.ProjectId,
                merged.Kind);

            return merged;
        }
        catch
        {
            if (imported is not null)
            {
                // Cleanup the extracted Source/<ImportId>/ folder on any failure.
                try
                {
                    if (Directory.Exists(imported.ExtractedPath))
                    {
                        Directory.Delete(imported.ExtractedPath, true);
                    }
                }
                catch
                {
                    // Best-effort cleanup; a locked/partial folder must not mask the real error.
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Removes a donor asset from the library and deletes its extracted <c>Source/&lt;ImportId&gt;/</c>
    /// folder. A missing folder is tolerated. Synchronous because it only performs in-memory list
    /// removal plus a local folder delete; kept as <c>RemoveAsync</c> to match the service contract.
    /// </summary>
    public void RemoveAsync(UltimateWardrobe.Core.Domain.DonorLibrary library, Guid importId)
    {
        if (library is null) throw new ArgumentNullException(nameof(library));

        var asset = library.Assets.FirstOrDefault(a => a.ImportId == importId);
        if (asset is not null)
        {
            library.Assets.Remove(asset);
        }

        if (asset is not null && !string.IsNullOrWhiteSpace(asset.ExtractedPath) && Directory.Exists(asset.ExtractedPath))
        {
            Directory.Delete(asset.ExtractedPath, true);
        }
    }

    /// <summary>
    /// Re-runs classification on the existing extracted folder, optionally with a new
    /// <c>catalogHint</c> (e.g. a reference root that appeared later), and updates the asset in
    /// place while preserving its archive identity fields.
    /// </summary>
    public async Task<DonorAsset> ReclassifyAsync(
        UltimateWardrobe.Core.Domain.DonorLibrary library,
        Guid importId,
        Catalog? catalogHint = null,
        CancellationToken cancellationToken = default)
    {
        if (library is null) throw new ArgumentNullException(nameof(library));

        var index = library.Assets.FindIndex(a => a.ImportId == importId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Donor asset {importId} is not in library {library.ProjectId}.");
        }

        var existing = library.Assets[index];

        if (!Directory.Exists(existing.ExtractedPath))
        {
            throw new DirectoryNotFoundException(
                $"Donor folder '{existing.ExtractedPath}' does not exist. Point the classifier at an extracted Source/<ImportId>/ folder.");
        }

        var classified = await _classifier.ClassifyAsync(existing.ExtractedPath, catalogHint, cancellationToken);

        var merged = MergeIdentity(existing, classified);

        library.Assets[index] = merged;

        _logger.LogInformation(
            "Reclassified donor {ImportId} in project {ProjectId}; Kind {Kind}",
            merged.ImportId,
            library.ProjectId,
            merged.Kind);

        return merged;
    }

    /// <summary>
    /// Merges the archive/persisted identity (file name, hash, import time, extracted path) with the
    /// freshly classified result (Kind, ProvidedSets, Detected*, manifest). The classifier fabricates
    /// identity placeholders from the folder; the real archive identity always wins.
    /// </summary>
    private static DonorAsset MergeIdentity(DonorAsset identity, DonorAsset classified)
    {
        return new DonorAsset(
            identity.ImportId,
            identity.OriginalFileName,
            identity.ExtractedPath,
            identity.ImportedAt,
            identity.ArchiveHash,
            classified.Kind,
            classified.ProvidedSets,
            classified.FileManifest,
            classified.DetectedBodySlideFiles,
            classified.DetectedPhysicsFiles);
    }

    private void EnsureNotOwnedElsewhere(UltimateWardrobe.Core.Domain.DonorLibrary library, string archiveHash, IEnumerable<UltimateWardrobe.Core.Domain.DonorLibrary>? otherLibraries = null)
    {
        if (string.IsNullOrWhiteSpace(archiveHash))
        {
            return;
        }

        var target = FindOwner(library, archiveHash);
        if (target is not null)
        {
            throw new DonorAlreadyOwnedException(target.ProjectId, archiveHash);
        }

        if (otherLibraries is null)
        {
            return;
        }

        foreach (var other in otherLibraries)
        {
            if (other is null || ReferenceEquals(other, library))
            {
                continue;
            }

            var owner = FindOwner(other, archiveHash);
            if (owner is not null)
            {
                throw new DonorAlreadyOwnedException(owner.ProjectId, archiveHash);
            }
        }
    }

    private static UltimateWardrobe.Core.Domain.DonorLibrary? FindOwner(UltimateWardrobe.Core.Domain.DonorLibrary library, string archiveHash)
    {
        var first = library.Assets.FirstOrDefault(a => string.Equals(a.ArchiveHash, archiveHash, StringComparison.Ordinal));
        return first is null ? null : library;
    }
}
