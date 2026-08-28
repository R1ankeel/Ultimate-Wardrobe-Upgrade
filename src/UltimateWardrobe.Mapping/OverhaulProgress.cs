using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Mapping;

/// <summary>
/// The Overhaul-level progress summary (Phase 3 plan 4.1, roadmap 5.4). Counts the catalog
/// sets per <see cref="ArmorSetStatus"/>, plus the total and the fraction treated as done.
///
/// <see cref="ArmorSetStatus.Done"/> is NOT a derived status - it is a caller-side boolean
/// overlay. <see cref="MappingService.GetOverhaulProgress"/> counts a set toward <see cref="Done"/>
/// exactly when its <see cref="MappingService.GetArmorSetStatus"/> is <see cref="ArmorSetStatus.Mapped"/>
/// AND the caller's done override is true. The invariant
/// <c>Done + InProgress + NeedsPatch + NotStarted == TotalSets</c> always holds.
/// </summary>
public sealed record OverhaulProgress
{
    public int TotalSets { get; init; }
    public int NotStarted { get; init; }
    public int InProgress { get; init; }
    public int Mapped { get; init; }
    public int NeedsPatch { get; init; }
    public int Done { get; init; }

    /// <summary>Fraction of sets treated as done (<see cref="Done"/> / <see cref="TotalSets"/>); 0 when total is 0.</summary>
    public double DoneFraction => TotalSets == 0 ? 0d : (double)Done / TotalSets;

    /// <summary>Sets still needing work: everything not yet counted as done.</summary>
    public int Remaining => TotalSets - Done;
}
