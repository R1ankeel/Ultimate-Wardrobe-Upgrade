using System.Text;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

/// <summary>
/// Post-scan report attached to a <see cref="Catalog"/> (Sprint 1.7). Deterministic warning
/// accumulation (deduplicated and ordinally sorted), <see cref="ScanStats"/> filling, and the
/// Outfit-grouped set count from the grouping stage (Sprint 1.7.3 tuning).
/// </summary>
public sealed class ScanReport
{
    public required ScanStats Stats { get; init; }

    public required IReadOnlyList<ScanWarning> Warnings { get; init; }

    /// <summary>
    /// Number of <see cref="ArmorSet"/>s whose key came from an Outfit (OTFT) membership
    /// signal. Fed from the grouping stage for the 1.7.3 tuning pass.
    /// </summary>
    public int OutfitGroupedSetCount { get; init; }

    /// <summary>
    /// Builds a human-readable, deterministic summary of the scan for the Phase 6 UI. Derived
    /// purely from <see cref="Stats"/>/<see cref="Warnings"/> (no scan timing, no random data),
    /// so repeated calls return the exact same text.
    /// </summary>
    public string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Total armor records: {Stats.TotalArmo}");
        sb.AppendLine($"Total armatures: {Stats.TotalArma}");
        sb.AppendLine($"Grouped sets: {Stats.GroupedSets} ({OutfitGroupedSetCount} outfit-grouped)");

        if (Stats.SkippedByReason.Count == 0)
        {
            sb.AppendLine($"Skipped armor records: {Stats.Skipped}");
        }
        else
        {
            var breakdown = string.Join(", ", Stats.SkippedByReason.Select(kv => $"{kv.Key}: {kv.Value}"));
            sb.AppendLine($"Skipped armor records: {Stats.Skipped} ({breakdown})");
        }

        sb.AppendLine($"Missing loose files: {Stats.MissingFiles}");
        sb.AppendLine($"Warnings: {Warnings.Count}");
        return sb.ToString();
    }
}