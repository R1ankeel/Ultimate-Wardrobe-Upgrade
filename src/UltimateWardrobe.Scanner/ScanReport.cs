using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Post-scan report (Sprint 1.5.3): deterministic warning accumulation (deduplicated and
/// ordinally sorted) plus <see cref="ScanStats"/> filling, and the route for unexpected
/// per-record exceptions into <see cref="CatalogScanException"/> with an <see cref="CatalogScanException.EditorId"/>.
/// Set-counting for the Outfit signal is fed from the grouping stage (1.7.3 tuning).
/// </summary>
public sealed class ScanReport
{
    public required ScanStats Stats { get; init; }

    public required IReadOnlyList<ScanWarning> Warnings { get; init; }

    /// <summary>
    /// Number of <see cref="ArmorSet"/>s whose key came from an Outfit (OTFT) membership
    /// signal. Used by the 1.7.3 tuning pass to gauge how much of the catalog rides on the
    /// Outfit-first path.
    /// </summary>
    public int OutfitGroupedSetCount { get; init; }

    /// <summary>
    /// Builds the report from raw scan outputs: deduplicates and sorts warnings for
    /// determinism, fills <see cref="ScanStats"/> (total skipped = the sum of the
    /// per-reason breakdown), and records the Outfit-grouped set count.
    /// </summary>
    public static ScanReport Build(
        int totalArmo,
        int totalArma,
        int groupedSetCount,
        IReadOnlyDictionary<SkipReason, int> skippedByReason,
        int outfitGroupedSetCount,
        IEnumerable<ScanWarning> warnings,
        int missingFiles = 0)
    {
        var sortedByReason = new SortedDictionary<SkipReason, int>();
        foreach (var pair in skippedByReason)
        {
            sortedByReason[pair.Key] = pair.Value;
        }

        var skippedTotal = sortedByReason.Values.Sum();

        var orderedWarnings = warnings
            .DistinctBy(w => (w.Message, w.EditorId))
            .OrderBy(w => w.Message, StringComparer.Ordinal)
            .ThenBy(w => w.EditorId, StringComparer.Ordinal)
            .ToList();

        return new ScanReport
        {
            Stats = new ScanStats
            {
                TotalArmo = totalArmo,
                TotalArma = totalArma,
                GroupedSets = groupedSetCount,
                Skipped = skippedTotal,
                SkippedByReason = sortedByReason,
                MissingFiles = missingFiles,
            },
            Warnings = orderedWarnings,
            OutfitGroupedSetCount = outfitGroupedSetCount,
        };
    }

    /// <summary>
    /// Runs <paramref name="action"/> and routes any unexpected exception into a
    /// <see cref="CatalogScanException"/> carrying the affected record's
    /// <see cref="CatalogScanException.EditorId"/>. <see cref="CatalogScanException"/> and
    /// <see cref="OperationCanceledException"/> pass through unchanged.
    /// </summary>
    public static T Guard<T>(string operation, string? editorId, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (CatalogScanException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(operation, editorId, ex);
        }
    }

    /// <summary>
    /// Action overload of <see cref="Guard{T}"/>.
    /// </summary>
    public static void Guard(string operation, string? editorId, Action action)
    {
        Guard(operation, editorId, () =>
        {
            action();
            return true;
        });
    }

    private static CatalogScanException Wrap(string operation, string? editorId, Exception ex)
    {
        var context = editorId is null ? "(no record context)" : $"record '{editorId}'";
        return new CatalogScanException(
            $"Unexpected error while {operation} for {context}: {ex.Message}",
            ex)
        {
            EditorId = editorId,
        };
    }
}