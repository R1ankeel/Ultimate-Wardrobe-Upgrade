using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Patcher;

/// <summary>
/// Sprint 5.3.2 (plan section 4.6, roadmap 7.5 task 5.1) - the Phase 5 orchestrator implementing
/// <see cref="IPatcher"/>. Full export loop: resolve the mappings to live target records (fails
/// before any output is touched when the overhaul has no scanned catalog), resolve + clear the mod
/// folder (delete-then-rebuild, never the output directory), build the esp plugin, slice the
/// donor files, write <c>meta.ini</c> (mapped-set count is the number of distinct target armor sets
/// among the resolved targets), and compose a self-contained <see cref="PatchReport"/>. Build-
/// blocking failures surface as a typed <see cref="PatchException"/>; per-mapping issues surface as
/// <see cref="PatchWarning"/>s. Coarse <see cref="PatchProgress"/> is reported per pipeline stage.
/// </summary>
public sealed class WardrobePatcher : IPatcher
{
    private const int StageCount = 5;

    private readonly ILogger<WardrobePatcher> _logger;
    private readonly TargetResolver _resolver = new();
    private readonly PluginBuilder _pluginBuilder = new();
    private readonly FileSlicer _fileSlicer = new();

    public WardrobePatcher(ILogger<WardrobePatcher>? logger = null)
    {
        _logger = logger ?? NullLogger<WardrobePatcher>.Instance;
    }

    public Task<PatchResult> BuildAsync(
        Overhaul overhaul,
        DonorLibrary donorLibrary,
        string outputDir,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (overhaul is null) throw new ArgumentNullException(nameof(overhaul));
        if (donorLibrary is null) throw new ArgumentNullException(nameof(donorLibrary));
        if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("OutputDir must not be empty.", nameof(outputDir));

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Export build started for Overhaul '{OverhaulName}' with {MappingCount} mappings",
            overhaul.Name,
            overhaul.Mappings.Count);

        Report(progress, "Resolve targets", 1);
        var resolution = _resolver.Resolve(overhaul, cancellationToken);

        Report(progress, "Prepare export folder", 2);
        var modDir = OutputFolder.ResolveModDir(outputDir, overhaul.Name);
        OutputFolder.ClearModDir(modDir);

        Report(progress, "Build esp plugin", 3);
        var plugin = _pluginBuilder.Build(overhaul, resolution.Targets, donorLibrary, modDir, cancellationToken);

        Report(progress, "Copy donor files", 4);
        var slice = _fileSlicer.Slice(
            resolution.Targets.Select(t => t.Mapping).ToList(),
            donorLibrary,
            modDir,
            cancellationToken);

        Report(progress, "Write meta.ini", 5);
        var mappedSets = resolution.Targets
            .Select(t => t.Mapping.TargetArmorSetId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        OutputFolder.WriteMetaIni(modDir, overhaul.Name, mappedSets);

        var warnings = new List<PatchWarning>();
        warnings.AddRange(resolution.Warnings);
        if (plugin.Report is { } pluginReport)
        {
            warnings.AddRange(pluginReport.Warnings);
        }

        warnings.AddRange(slice.Warnings);

        var report = new PatchReport
        {
            TotalMappings = overhaul.Mappings.Count,
            ResolvedMappings = resolution.Targets.Count,
            SkippedMappings = (overhaul.Mappings.Count - resolution.Targets.Count) + slice.SkippedMappings,
            OverriddenRecords = plugin.Report?.OverriddenRecords ?? 0,
            CopiedFiles = slice.CopiedFiles,
            CopiedBytes = slice.CopiedBytes,
            Warnings = warnings,
        };

        _logger.LogInformation(
            "Export build finished for Overhaul '{OverhaulName}': {Total} mappings, {Resolved} resolved, {Skipped} skipped, " +
            "{Overridden} overridden records, {Copied} files / {Bytes} bytes, {WarningCount} warnings",
            overhaul.Name,
            report.TotalMappings,
            report.ResolvedMappings,
            report.SkippedMappings,
            report.OverriddenRecords,
            report.CopiedFiles.Count,
            report.CopiedBytes,
            report.Warnings.Count);

        return Task.FromResult(new PatchResult(plugin.PluginPath, slice.CopiedFiles) { Report = report });
    }

    private static void Report(IProgress<PatchProgress>? progress, string stage, int completed)
    {
        progress?.Report(new PatchProgress(stage, completed, StageCount));
    }
}