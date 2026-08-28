namespace UltimateWardrobe.Core.Enums;

/// <summary>
/// Reason a record was skipped into <see cref="Domain.ScanStats.Skipped"/> during a catalog scan.
/// </summary>
public enum SkipReason
{
    Unknown = 0,
    NoArmature = 1,
    EmptyModel = 2,
    NoSlot = 3,
    NoKeyword = 4,
    CreatureRace = 5,
    Other = 6,

    /// <summary>Ring or amulet jewelry - not a replacable armor kit (manual-testing bug 1).</summary>
    Jewelry = 7,

    /// <summary>Generic vanilla-enchanted item, dropped by its display-name suffix (manual-testing bug 2).</summary>
    Enchanted = 8
}
