using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Graduated donor classifier: probes the extracted folder for plugins, routes to branch 1
/// (plugin pipeline through <see cref="DonorScanPipeline"/>, Sprint 2.1) or branch 2 (mesh
/// heuristics via <see cref="MeshPathIndexer"/> + <see cref="MeshSetAssembler"/>, Sprint 2.2),
/// and fills the <see cref="DonorAsset.FileManifest"/> from the folder. Branch 1 with zero
/// <see cref="DonorProvidedSet"/>s (missing masters, or a plugin with no groupable armor) falls
/// through to branch 2 with a logged reason (2.1.4). Branch 3 (Sprint 2.3) runs for EVERY
/// classification regardless of branch 1/2: <see cref="BodySlideDetector"/> +
/// <see cref="PhysicsDetector"/> fill <see cref="DonorAsset.DetectedBodySlideFiles"/> /
/// <see cref="DonorAsset.DetectedPhysicsFiles"/> and <see cref="DonorKindDetector"/> derives
/// <see cref="DonorAssetKind"/> from the 4.3 table (flags are independent of Kind). The asset's
/// archive identity (real hash, file name, timestamps) is merged by
/// <see cref="DonorLibraryService"/> in Sprint 2.4 - the classifier itself only fabricates a
/// documented placeholder for the archive hash.
/// </summary>
public sealed class DonorClassifier : IDonorClassifier
{
    private const string PendingClassificationHash = "classification-pending";

    private readonly DonorPluginProbe _probe;
    private readonly DonorScanPipeline _pipeline;
    private readonly ReferenceMasterMerger _merger;
    private readonly MeshPathIndexer _indexer;
    private readonly BodySlideDetector _bodySlideDetector;
    private readonly PhysicsDetector _physicsDetector;
    private readonly ILogger<DonorClassifier> _logger;

    public DonorClassifier(
        DonorPluginProbe? probe = null,
        DonorScanPipeline? pipeline = null,
        ReferenceMasterMerger? merger = null,
        MeshPathIndexer? indexer = null,
        BodySlideDetector? bodySlideDetector = null,
        PhysicsDetector? physicsDetector = null,
        ILogger<DonorClassifier>? logger = null)
    {
        _probe = probe ?? new DonorPluginProbe();
        _pipeline = pipeline ?? new DonorScanPipeline();
        _merger = merger ?? new ReferenceMasterMerger();
        _indexer = indexer ?? new MeshPathIndexer();
        _bodySlideDetector = bodySlideDetector ?? new BodySlideDetector();
        _physicsDetector = physicsDetector ?? new PhysicsDetector();
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

        var branch = ClassifyBranch(extractedDir, probe, catalogHint, warnings, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var bodySlideFiles = _bodySlideDetector.Detect(extractedDir);
        var setMeshPaths = branch.Sets
            .SelectMany(s => s.Variants)
            .SelectMany(v => v.Pieces)
            .Select(p => p.MeshPath)
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();
        var physicsFiles = _physicsDetector.Detect(extractedDir, setMeshPaths);
        var kind = DonorKindDetector.Derive(branch.Sets, branch.ViaPlugin, bodySlideFiles, physicsFiles);

        var manifest = Directory.EnumerateFiles(extractedDir, "*.*", SearchOption.AllDirectories)
            .Select(path => new DonorFileEntry(Path.GetRelativePath(extractedDir, path).Replace('\\', '/'), new FileInfo(path).Length))
            .Where(entry => !string.Equals(entry.RelativePath, "_meta.json", StringComparison.Ordinal))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        cancellationToken.ThrowIfCancellationRequested();

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(extractedDir));
        var importId = Guid.TryParse(folderName, out var parsed) ? parsed : Guid.NewGuid();

        _logger.LogInformation(
            "Classification {Folder}: branch {Branch} -> Kind {Kind}; {BodySlide} BodySlide files, {Physics} physics files, {SetCount} ProvidedSets",
            folderName,
            branch.ViaPlugin ? "1" : "2",
            kind,
            bodySlideFiles.Count,
            physicsFiles.Count,
            branch.Sets.Count);

        return new DonorAsset(
            importId,
            folderName,
            extractedDir,
            DateTime.UtcNow,
            PendingClassificationHash,
            kind,
            branch.Sets,
            manifest,
            bodySlideFiles,
            physicsFiles);
    }

    private BranchResult ClassifyBranch(
        string extractedDir,
        DonorPluginProbeResult probe,
        Catalog? catalogHint,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (probe.Main is null)
        {
            return new BranchResult(ClassifyViaMeshHeuristics(extractedDir, probe, warnings), ViaPlugin: false);
        }

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(extractedDir));
        var viaPlugin = ClassifyViaPlugin(folderName, probe, catalogHint, warnings, cancellationToken);

        if (viaPlugin.Count > 0)
        {
            return new BranchResult(viaPlugin, ViaPlugin: true);
        }

        _logger.LogWarning(
            "Classification {Folder}: branch 1 (plugin pipeline) produced no ProvidedSets; " +
            "falling back to branch 2 (mesh heuristics)",
            folderName);
        warnings.Add(new ScanWarning(
            $"Branch 1 (plugin classification) produced no ProvidedSets - the donor esp carries no groupable armor " +
            "(no ARMO records, missing masters, or every armor was skipped); falling back to branch 2 (mesh heuristics)."));

        return new BranchResult(ClassifyViaMeshHeuristics(extractedDir, probe, warnings), ViaPlugin: false);
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
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(extractedDir));

        var meshPaths = _indexer.IndexMeshes(extractedDir);
        if (meshPaths.Count == 0)
        {
            _logger.LogDebug(
                "Classification {Folder}: branch 2 (mesh heuristics) found no meshes/**/*.nif; returning no ProvidedSets",
                folderName);
            return Array.Empty<DonorProvidedSet>();
        }

        var texturePaths = _indexer.IndexTextures(extractedDir);
        var sets = MeshSetAssembler.Assemble(meshPaths, texturePaths, warnings);

        _logger.LogInformation(
            "Classification {Folder}: branch 2 (mesh heuristics) produced {SetCount} ProvidedSets from {MeshCount} meshes",
            folderName,
            sets.Count,
            meshPaths.Count);

        return sets;
    }

    private sealed record BranchResult(IReadOnlyList<DonorProvidedSet> Sets, bool ViaPlugin);
}