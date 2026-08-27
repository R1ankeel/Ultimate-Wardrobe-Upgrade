using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// End-to-end <see cref="ICatalogScanner"/> implementation: orchestrates the
/// 1.1-1.4 pipeline - plugin discovery, masters-first ordering, overlay loading, record index,
/// ARMO-&gt;ARMA-&gt;files correlation, ArmorSet grouping and gender/weight variant assembly -
/// into a deterministic <see cref="Catalog"/> (Sprint 1.5.1, catalog report 1.7.2). Cancellation
/// is checked between plugins and between record groups. The whole scan is synchronous under the
/// <see cref="Task"/> contract (no thread affinity, no <see cref="SynchronizationContext"/> capture).
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

        var scanId = Guid.NewGuid().ToString("N");
        _logger.LogInformation(
            "Scan {ScanId} started; source kind {SourceKind}, root path {RootPath}",
            scanId,
            source.Kind,
            source.RootPath);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Scan {ScanId}: source details - main plugin {MainPlugin}, masters [{Masters}]",
                scanId,
                source is StoryModCatalogSource story ? story.MainPlugin : "(n/a)",
                source is StoryModCatalogSource story2 ? string.Join(", ", story2.Masters) : "(n/a)");
        }

        var watch = Stopwatch.StartNew();
        var warnings = new List<ScanWarning>();

        var discovery = ScanReportBuilder.Guard("discovering plugins", null, () => new PluginDiscovery().Discover(source, warnings));

        foreach (var master in discovery.MissingExplicitMasters)
        {
            _logger.LogWarning(
                "Scan {ScanId}: missing master '{MasterFileName}' requested by main plugin '{MainPlugin}'",
                scanId,
                master.FileName,
                source is StoryModCatalogSource story3 ? story3.MainPlugin : "(n/a)");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var loader = new ModLoader();
        var loadOrder = ScanReportBuilder.Guard(
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
                if (mod is null)
                {
                    _logger.LogWarning(
                        "Scan {ScanId}: plugin '{PluginPath}' failed to load and was skipped",
                        scanId,
                        plugin.AbsolutePath);
                    continue;
                }

                _logger.LogDebug(
                    "Scan {ScanId}: plugin '{PluginFileName}' loaded ({LoadedIndex}/{LoadedCount})",
                    scanId,
                    plugin.ModKey.FileName,
                    loaded.Count + 1,
                    loadOrder.Count);
                loaded.Add(mod);
            }

            var index = ScanReportBuilder.Guard(
                "building the record index",
                null,
                () => RecordIndex.Build(loaded, warnings, cancellationToken));

            _logger.LogInformation(
                "Scan {ScanId}: record index built for {PluginCount} plugins - {ArmoCount} ARMO, {ArmaCount} ARMA",
                scanId,
                loaded.Count,
                index.ArmorCount,
                index.ArmorAddonCount);

            var fileResolver = new FileResolver(source.RootPath, _logger);
            var correlated = ScanReportBuilder.Guard(
                "correlating armors",
                null,
                () => new ArmorCorrelator(fileResolver).Correlate(index, warnings, cancellationToken));

            var grouping = ScanReportBuilder.Guard(
                "grouping armors into sets",
                null,
                () => new ArmorSetGrouper().Group(correlated, index, warnings, cancellationToken));

            var sets = ScanReportBuilder.Guard(
                "assembling gender/weight variants",
                null,
                () => VariantAssembler.Assemble(grouping, index, warnings, cancellationToken));

            CountMissingFiles(fileResolver, sets);

            var report = ScanReportBuilder.Build(
                totalArmo: index.ArmorCount,
                totalArma: index.ArmorAddonCount,
                groupedSetCount: sets.Count,
                skippedByReason: grouping.SkippedByReason,
                outfitGroupedSetCount: grouping.OutfitGroupedSetCount,
                warnings: warnings,
                missingFiles: fileResolver.MissingFiles);

            _logger.LogInformation(
                "Scan {ScanId}: grouped {ArmoCount} armors into {SetCount} sets ({OutfitGrouped} outfit-grouped); skipped {SkippedCount}",
                scanId,
                index.ArmorCount,
                sets.Count,
                report.OutfitGroupedSetCount,
                report.Stats.Skipped);

            LastReport = report;
            return new Catalog(source, sets, report.Stats, report.Warnings, report);
        }
        finally
        {
            foreach (var mod in loaded)
            {
                mod.Dispose();
            }

            watch.Stop();
            _logger.LogInformation(
                "Scan {ScanId} finished in {ElapsedMilliseconds} ms with {WarningCount} warnings",
                scanId,
                watch.ElapsedMilliseconds,
                warnings.Count);
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