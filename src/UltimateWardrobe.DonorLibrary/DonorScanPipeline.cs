using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Result of a branch-1 (plugin) classification run.
/// </summary>
public sealed record DonorPipelineResult
{
    public required IReadOnlyList<DonorProvidedSet> ProvidedSets { get; init; }

    /// <summary>Donor-originated ARMO records that reached the correlate stage.</summary>
    public int DonorArmorCount { get; init; }

    /// <summary>Plugins successfully loaded (reference + donor, after corrupt skip).</summary>
    public int LoadedPluginCount { get; init; }

    /// <summary>Reference plugin files merged into this run.</summary>
    public int ReferencePluginCount { get; init; }
}

/// <summary>
/// Branch-1 pipeline (Sprint 2.1.2): loads the combined reference + donor plugin set, builds a
/// <see cref="RecordIndex"/>, correlates ONLY donor-originated ARMO (filtered by
/// <c>FormKey.ModKey</c> against the donor plugin keys), groups them into <see cref="ArmorSet"/>s
/// and assembles gender/weight variants, then maps each set into a <see cref="DonorProvidedSet"/>
/// (Id/DisplayName from the <see cref="ArmorSet"/>, <see cref="ArmorSet.Variants"/> reused).
/// Reference records are used for resolution only and never appear in the output (2.1.3).
/// Loaded overlays are disposed in a <see langword="finally"/>; corrupt reference/donor plugins
/// warn and are skipped via <see cref="ModLoader.TryLoad"/> - never abort (2.1.3).
/// </summary>
public sealed class DonorScanPipeline
{
    private readonly ModLoader _loader;
    private readonly ILogger<DonorScanPipeline> _logger;

    public DonorScanPipeline(ModLoader? loader = null, ILogger<DonorScanPipeline>? logger = null)
    {
        _loader = loader ?? new ModLoader();
        _logger = logger ?? NullLogger<DonorScanPipeline>.Instance;
    }

    public DonorPipelineResult Run(
        DonorPluginProbeResult probe,
        IReadOnlyList<string> referencePaths,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var donorKeys = probe.Candidates.Select(c => c.ModKey).ToHashSet();

        // Reference first, donor later-wins. A reference path whose name the donor set already
        // owns is dropped here too, so a caller-provided unfiltered list cannot resurrect a
        // donor-bundled duplicate from the reference side.
        var loadPaths = referencePaths
            .Where(p => !donorKeys.Contains(ModKey.FromFileName(Path.GetFileName(p))))
            .Select(p => (Path: p, IsDonor: false))
            .Concat(probe.Candidates.Select(c => (Path: c.AbsolutePath, IsDonor: true)))
            .ToList();

        var loaded = new List<LoadedMod>();
        try
        {
            LoadPlugins(loadPaths, loaded, warnings, cancellationToken);

            var index = ScanReportBuilder.Guard(
                "building the record index",
                null,
                () => RecordIndex.Build(loaded, warnings, cancellationToken));

            var correlator = new ArmorCorrelator(new FileResolver(probe.DataPath, _logger));

            var correlated = index.EnumerateArmor()
                .Where(a => donorKeys.Contains(a.FormKey.ModKey))
                .OrderBy(a => a.FormKey.ModKey.Name, StringComparer.Ordinal)
                .ThenBy(a => a.FormKey.ID)
                .Select(a => ScanReportBuilder.Guard(
                    "correlating armor",
                    a.EditorID,
                    () => correlator.CorrelateOne(a, index, warnings)))
                .ToList();

            var grouping = ScanReportBuilder.Guard(
                "grouping donor armors into sets",
                null,
                () => new ArmorSetGrouper().Group(correlated, index, warnings, cancellationToken));

            var sets = ScanReportBuilder.Guard(
                "assembling gender/weight variants",
                null,
                () => VariantAssembler.Assemble(grouping, index, warnings, cancellationToken));

            var provided = sets
                .Select(s => new DonorProvidedSet(s.Id, s.DisplayName, s.Variants))
                .ToList();

            return new DonorPipelineResult
            {
                ProvidedSets = provided,
                DonorArmorCount = correlated.Count,
                LoadedPluginCount = loaded.Count,
                ReferencePluginCount = referencePaths.Count,
            };
        }
        finally
        {
            foreach (var mod in loaded)
            {
                mod.Dispose();
            }
        }
    }

    private void LoadPlugins(
        IReadOnlyList<(string Path, bool IsDonor)> loadPaths,
        List<LoadedMod> loaded,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken)
    {
        foreach (var (path, isDonor) in loadPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mod = _loader.TryLoad(path, warnings);
            if (mod is null)
            {
                _logger.LogWarning(
                    "Donor scan: {Origin} plugin '{Path}' failed to load and was skipped",
                    isDonor ? "donor" : "reference",
                    path);
                continue;
            }

            _logger.LogDebug(
                "Donor scan: {Origin} plugin '{Plugin}' loaded ({LoadedIndex}/{LoadedCount})",
                isDonor ? "donor" : "reference",
                mod.ModKey.Name,
                loaded.Count + 1,
                loadPaths.Count);
            loaded.Add(mod);
        }
    }
}