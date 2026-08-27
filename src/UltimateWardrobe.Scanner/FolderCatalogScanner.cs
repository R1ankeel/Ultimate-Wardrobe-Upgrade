using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// End-to-end <see cref="ICatalogScanner"/> implementation (Sprint 1.5.1): orchestrates the
/// 1.1-1.4 pipeline - plugin discovery, masters-first ordering, overlay loading, record index,
/// ARMO-&gt;ARMA-&gt;files correlation, ArmorSet grouping and gender/weight variant assembly -
/// into a deterministic <see cref="Catalog"/>. Cancellation is checked between plugins and
/// between record groups. The whole scan is synchronous under the <see cref="Task"/> contract
/// (no thread affinity, no <see cref="SynchronizationContext"/> capture).
/// </summary>
public sealed class FolderCatalogScanner : ICatalogScanner
{
    private readonly ILogger<FolderCatalogScanner> _logger;

    public FolderCatalogScanner(ILogger<FolderCatalogScanner>? logger = null)
    {
        _logger = logger ?? NullLogger<FolderCatalogScanner>.Instance;
    }

    /// <summary>
    /// Report of the most recent successful scan (used by the 1.7.3 tuning pass; always derived
    /// from the same data that produced <see cref="Catalog"/>).
    /// </summary>
    public ScanReport? LastReport { get; private set; }

    public Task<Catalog> ScanAsync(CatalogSource source, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(RunScan(source, cancellationToken));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromCanceled<Catalog>(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException<Catalog>(ex);
        }
    }

    private Catalog RunScan(CatalogSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastReport = null;

        var warnings = new List<ScanWarning>();

        var discovery = ScanReport.Guard("discovering plugins", null, () => new PluginDiscovery().Discover(source, warnings));

        cancellationToken.ThrowIfCancellationRequested();

        var loader = new ModLoader();
        var loadOrder = ScanReport.Guard(
            "building the artificial load order",
            null,
            () => new LoadOrderBuilder(loader).Build(discovery, warnings, cancellationToken));

        var loaded = new List<LoadedMod>();
        try
        {
            foreach (var plugin in loadOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mod = loader.TryLoad(plugin.AbsolutePath, warnings);
                if (mod is not null)
                {
                    loaded.Add(mod);
                }
            }

            var index = ScanReport.Guard(
                "building the record index",
                null,
                () => RecordIndex.Build(loaded, warnings, cancellationToken));

            var fileResolver = new FileResolver(source.RootPath, _logger);
            var correlated = ScanReport.Guard(
                "correlating armors",
                null,
                () => new ArmorCorrelator(fileResolver).Correlate(index, warnings, cancellationToken));

            var grouping = ScanReport.Guard(
                "grouping armors into sets",
                null,
                () => new ArmorSetGrouper().Group(correlated, index, warnings, cancellationToken));

            var sets = ScanReport.Guard(
                "assembling gender/weight variants",
                null,
                () => VariantAssembler.Assemble(grouping, index, warnings, cancellationToken));

            CountMissingFiles(fileResolver, sets);

            var report = ScanReport.Build(
                totalArmo: index.ArmorCount,
                totalArma: index.ArmorAddonCount,
                groupedSetCount: sets.Count,
                skippedByReason: grouping.SkippedByReason,
                outfitGroupedSetCount: grouping.OutfitGroupedSetCount,
                warnings: warnings,
                missingFiles: fileResolver.MissingFiles);

            LastReport = report;
            return new Catalog(source, sets, report.Stats, report.Warnings);
        }
        finally
        {
            foreach (var mod in loaded)
            {
                mod.Dispose();
            }
        }
    }

    /// <summary>
    /// Accounts missing loose files for every mesh/texture referenced by the assembled catalog.
    /// Missing files only increment <see cref="ScanStats.MissingFiles"/> (Debug-level logging) -
    /// never ScanWarnings, to avoid warning floods for BSA-packed vanilla.
    /// </summary>
    private static void CountMissingFiles(FileResolver resolver, IReadOnlyList<ArmorSet> sets)
    {
        foreach (var set in sets)
        {
            foreach (var variant in set.Variants)
            {
                foreach (var piece in variant.Pieces)
                {
                    if (piece.MeshPath is not null)
                    {
                        resolver.ResolveMesh(piece.MeshPath);
                    }

                    foreach (var texturePath in piece.TexturePaths)
                    {
                        resolver.ResolveTexture(texturePath);
                    }
                }
            }
        }
    }
}