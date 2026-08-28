using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Graduated donor classifier: probes the extracted folder for plugins, routes to branch 1
/// (plugin pipeline through <see cref="DonorScanPipeline"/>, Sprint 2.1) or branch 2 (mesh
/// heuristics, Sprint 2.2), and fills the <see cref="DonorAsset.FileManifest"/> from the folder.
/// Branch 1 with zero <see cref="DonorProvidedSet"/>s (missing masters, or a plugin with no
/// groupable armor) falls through to branch 2 with a logged reason (2.1.4). Branch 3 detectors
/// and <see cref="DonorAssetKind"/> land in Sprint 2.3; until then the kind stays honest -
/// <see cref="DonorAssetKind.Unknown"/>, empty flag lists. The asset's archive identity (real
/// hash, file name, timestamps) is merged by <see cref="DonorLibraryService"/> in Sprint 2.4 -
/// the classifier itself only fabricates a documented placeholder for the archive hash.
/// </summary>
public sealed class DonorClassifier : IDonorClassifier
{
    private const string PendingClassificationHash = "classification-pending";

    private readonly DonorPluginProbe _probe;
    private readonly DonorScanPipeline _pipeline;
    private readonly ReferenceMasterMerger _merger;
    private readonly ILogger<DonorClassifier> _logger;

    public DonorClassifier(
        DonorPluginProbe? probe = null,
        DonorScanPipeline? pipeline = null,
        ReferenceMasterMerger? merger = null,
        ILogger<DonorClassifier>? logger = null)
    {
        _probe = probe ?? new DonorPluginProbe();
        _pipeline = pipeline ?? new DonorScanPipeline();
        _merger = merger ?? new ReferenceMasterMerger();
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

        cancellationToken.ThrowIfCancellationRequested();

        var providedSets = ClassifyBranch(extractedDir, probe, catalogHint, warnings, cancellationToken);

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
            providedSets,
            manifest);
    }

    private IReadOnlyList<DonorProvidedSet> ClassifyBranch(
        string extractedDir,
        DonorPluginProbeResult probe,
        Catalog? catalogHint,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (probe.Main is null)
        {
            return ClassifyViaMeshHeuristics(extractedDir, probe, warnings);
        }

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(extractedDir));
        var viaPlugin = ClassifyViaPlugin(folderName, probe, catalogHint, warnings, cancellationToken);

        if (viaPlugin.Count > 0)
        {
            return viaPlugin;
        }

        _logger.LogWarning(
            "Classification {Folder}: branch 1 (plugin pipeline) produced no ProvidedSets; " +
            "falling back to branch 2 (mesh heuristics)",
            folderName);
        warnings.Add(new ScanWarning(
            $"Branch 1 (plugin classification) produced no ProvidedSets - the donor esp carries no groupable armor " +
            "(no ARMO records, missing masters, or every armor was skipped); falling back to branch 2 (mesh heuristics)."));

        return ClassifyViaMeshHeuristics(extractedDir, probe, warnings);
    }

    private IReadOnlyList<DonorProvidedSet> ClassifyViaPlugin(
        string folderName,
        DonorPluginProbeResult probe,
        Catalog? catalogHint,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken)
    {
        var donorKeys = probe.Candidates.Select(c => c.ModKey).ToHashSet();

        var referencePaths = catalogHint is null
            ? Array.Empty<string>()
            : _merger.Merge(catalogHint.Source.RootPath, donorKeys);

        if (referencePaths.Count == 0)
        {
            _logger.LogDebug(
                "Classification {Folder}: no reference esms merged (no catalog hint or no reference data); " +
                "classifying donor plugins standalone",
                folderName);
        }

        var result = _pipeline.Run(probe, referencePaths, warnings, cancellationToken);

        _logger.LogInformation(
            "Classification {Folder}: branch 1 produced {SetCount} ProvidedSets from {ArmorCount} donor ARMO " +
            "({LoadedPlugins} plugins loaded, {ReferencePlugins} reference)",
            folderName,
            result.ProvidedSets.Count,
            result.DonorArmorCount,
            result.LoadedPluginCount,
            result.ReferencePluginCount);

        return result.ProvidedSets;
    }

    private IReadOnlyList<DonorProvidedSet> ClassifyViaMeshHeuristics(
        string extractedDir,
        DonorPluginProbeResult probe,
        List<ScanWarning> warnings)
    {
        _logger.LogDebug(
            "Classification {Folder}: branch 2 (mesh heuristics) lands in Sprint 2.2; returning no ProvidedSets",
            Path.GetFileName(Path.TrimEndingDirectorySeparator(extractedDir)));
        return Array.Empty<DonorProvidedSet>();
    }
}