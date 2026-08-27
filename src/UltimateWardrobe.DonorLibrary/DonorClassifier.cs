using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Graduated donor classifier (Sprint 2.0.4 skeleton): probes the extracted folder for plugins,
/// routes to branch 1 (plugin pipeline, Sprint 2.1) or branch 2 (mesh heuristics, Sprint 2.2),
/// and fills the <see cref="DonorAsset.FileManifest"/> from the folder. Branch 3 detectors and
/// <see cref="DonorAssetKind"/> land in Sprint 2.3; until then the skeleton stays honest -
/// zero <see cref="DonorProvidedSet"/>s, <see cref="DonorAssetKind.Unknown"/>, empty flag lists.
/// The asset's archive identity (real hash, file name, timestamps) is merged by
/// <see cref="DonorLibraryService"/> in Sprint 2.4 - the classifier itself only fabricates a
/// documented placeholder for the archive hash.
/// </summary>
public sealed class DonorClassifier : IDonorClassifier
{
    private const string PendingClassificationHash = "classification-pending";

    private readonly DonorPluginProbe _probe;
    private readonly ILogger<DonorClassifier> _logger;

    public DonorClassifier(DonorPluginProbe? probe = null, ILogger<DonorClassifier>? logger = null)
    {
        _probe = probe ?? new DonorPluginProbe();
        _logger = logger ?? NullLogger<DonorClassifier>.Instance;
    }

    public Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog? catalogHint = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(Classify(extractedDir, catalogHint, cancellationToken));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromCanceled<DonorAsset>(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException<DonorAsset>(ex);
        }
    }

    private DonorAsset Classify(string extractedDir, Catalog? catalogHint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(extractedDir))
        {
            throw new ArgumentException("Extracted donor folder must not be empty.", nameof(extractedDir));
        }

        if (!Directory.Exists(extractedDir))
        {
            throw new DirectoryNotFoundException(
                $"Donor folder '{extractedDir}' does not exist. Point the classifier at an extracted Source/<ImportId>/ folder.");
        }

        var warnings = new List<ScanWarning>();
        var probe = _probe.Probe(extractedDir, warnings);

        var branch = probe.Main is not null ? 1 : 2;
        _logger.LogInformation(
            "Classification {Folder}: branch {Branch} selected ({PluginCount} plugins); " +
            "ProvidedSets/kind/detector output lands in Sprints 2.1-2.3",
            Path.GetFileName(Path.TrimEndingDirectorySeparator(extractedDir)),
            branch,
            probe.Candidates.Count);

        cancellationToken.ThrowIfCancellationRequested();

        var manifest = Directory.EnumerateFiles(extractedDir, "*.*", SearchOption.AllDirectories)
            .Select(path => new DonorFileEntry(Path.GetRelativePath(extractedDir, path).Replace('\\', '/'), new FileInfo(path).Length))
            .Where(entry => !string.Equals(entry.RelativePath, "_meta.json", StringComparison.Ordinal))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        cancellationToken.ThrowIfCancellationRequested();

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(extractedDir));
        var importId = Guid.TryParse(folderName, out var parsed) ? parsed : Guid.NewGuid();

        return new DonorAsset(
            importId,
            folderName,
            extractedDir,
            DateTime.UtcNow,
            PendingClassificationHash,
            DonorAssetKind.Unknown,
            Array.Empty<DonorProvidedSet>(),
            manifest);
    }
}