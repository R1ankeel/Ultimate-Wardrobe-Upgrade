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
    Other = 6
}
