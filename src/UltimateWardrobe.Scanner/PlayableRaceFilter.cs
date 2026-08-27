namespace UltimateWardrobe.Scanner;

/// <summary>
/// Playable-race whitelist for the creature-skin pre-filter (Sprint 1.3.3). An ARMO
/// whose primary ARMA <c>Race</c> resolves to a RACE whose EditorID is NOT in the whitelist
/// (10 base races + 10 vampire variants + the universal <c>DefaultRace</c> fallback) is
/// skipped into <c>SkipReason.CreatureRace</c>. A null race link never skips.
/// </summary>
public static class PlayableRaceFilter
{
    /// <summary>
    /// EditorIDs of the 10 base playable races (verified against the real Skyrim.esm RACE
    /// records: the playable races all carry the "Race" suffix).
    /// </summary>
    public static readonly IReadOnlySet<string> BaseRaceIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "ArgonianRace",
        "BretonRace",
        "DarkElfRace",
        "HighElfRace",
        "ImperialRace",
        "KhajiitRace",
        "NordRace",
        "OrcRace",
        "RedguardRace",
        "WoodElfRace",
    };

    /// <summary>
    /// EditorIDs of the 10 vampire variants of the base races (real game: "Race" + "Vampire").
    /// </summary>
    public static readonly IReadOnlySet<string> VampireRaceIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "ArgonianRaceVampire",
        "BretonRaceVampire",
        "DarkElfRaceVampire",
        "HighElfRaceVampire",
        "ImperialRaceVampire",
        "KhajiitRaceVampire",
        "NordRaceVampire",
        "OrcRaceVampire",
        "RedguardRaceVampire",
        "WoodElfRaceVampire",
    };

    /// <summary>
    /// EditorIDs of universal fallback races that mark armor as fitting any default humanoid
    /// body, never a creature. The real Skyrim.esm vanilla armor ARMA records reference
    /// "DefaultRace" (FormID 0x000019) rather than a null race link, so this race must never
    /// trigger a <see cref="Core.Enums.SkipReason.CreatureRace"/> skip.
    /// </summary>
    public static readonly IReadOnlySet<string> UniversalRaceIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "DefaultRace",
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

        return BaseRaceIds.Contains(raceEditorId) || VampireRaceIds.Contains(raceEditorId) || UniversalRaceIds.Contains(raceEditorId);
    }
}
