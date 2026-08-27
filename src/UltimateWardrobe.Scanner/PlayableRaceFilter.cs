namespace UltimateWardrobe.Scanner;

/// <summary>
/// Playable-race whitelist for the creature-skin pre-filter (Sprint 1.3.3). An ARMO
/// whose primary ARMA <c>Race</c> resolves to a RACE whose EditorID is NOT in the whitelist
/// (10 base races + 10 vampire variants) is skipped into <c>SkipReason.CreatureRace</c>.
/// A null race link never skips.
/// </summary>
public static class PlayableRaceFilter
{
    /// <summary>
    /// EditorIDs of the 10 base playable races.
    /// </summary>
    public static readonly IReadOnlySet<string> BaseRaceIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Argonian",
        "Breton",
        "DarkElf",
        "HighElf",
        "Imperial",
        "Khajiit",
        "Nord",
        "Orc",
        "Redguard",
        "WoodElf",
    };

    /// <summary>
    /// EditorIDs of the 10 vampire variants of the base races.
    /// </summary>
    public static readonly IReadOnlySet<string> VampireRaceIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "ArgonianVampire",
        "BretonVampire",
        "DarkElfVampire",
        "HighElfVampire",
        "ImperialVampire",
        "KhajiitVampire",
        "NordVampire",
        "OrcVampire",
        "RedguardVampire",
        "WoodElfVampire",
    };

    /// <summary>
    /// Returns true when <paramref name="raceEditorId"/> is one of the 10 base playable
    /// races, false otherwise. Vampire variants are checked with
    /// <see cref="IsInPlayableWhitelist"/>.
    /// </summary>
    public static bool IsBaseRaceId(string? raceEditorId)
    {
        return raceEditorId is not null && BaseRaceIds.Contains(raceEditorId);
    }

    /// <summary>
    /// Returns true when <paramref name="raceEditorId"/> is in the full playable whitelist
    /// (10 base races + 10 vampire variants), false otherwise. Null returns false.
    /// </summary>
    public static bool IsInPlayableWhitelist(string? raceEditorId)
    {
        if (raceEditorId is null)
        {
            return false;
        }

        return BaseRaceIds.Contains(raceEditorId) || VampireRaceIds.Contains(raceEditorId);
    }
}
