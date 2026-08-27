using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Results of Outfit-first key resolution for a single ARMO.
/// </summary>
public sealed record OutfitKeyResult
{
    /// <summary>
    /// Normalized set key from the Outfit signal, or null when the ARMO belongs to no Outfit
    /// (fall through to the EDID/mesh fallback stage).
    /// </summary>
    public NormalizedSetKey? Key { get; init; }

    /// <summary>
    /// Number of Outfits this ARMO belongs to.
    /// </summary>
    public int OutfitCount { get; init; }
}

/// <summary>
/// Resolves the grouping signal from Outfit (OTFT) membership (Sprint 1.3.4). An armor that
/// belongs to at least one Outfit gets a candidate key from each normalized Outfit EditorID.
/// <see cref="Resolve"/> returns the deterministic alphabetical-first key (single-key consumer);
/// <see cref="ResolveAll"/> returns every candidate for the 1.7.3 agreement rule, where the
/// ArmorSetGrouper picks the key with the most member agreement (verifiable split-membership /
/// cross-sharing merge instead of a purely local tie-break).
/// </summary>
public static class OutfitSetKeyResolver
{
    /// <summary>
    /// Resolves the single Outfit key for the given armor record (alphabetical-first candidate).
    /// Returns <see cref="OutfitKeyResult.Key"/> = null when the armor belongs to no Outfit.
    /// </summary>
    public static OutfitKeyResult Resolve(IArmorGetter armor, RecordIndex index)
    {
        var outfits = index.OutfitsForArmor(armor.FormKey);
        if (outfits.Count == 0)
        {
            return new OutfitKeyResult { Key = null, OutfitCount = 0 };
        }

        var keys = ResolveAll(armor, index);
        return new OutfitKeyResult { Key = keys.FirstOrDefault(), OutfitCount = outfits.Count };
    }

    /// <summary>
    /// Resolves every normalized Outfit candidate key for the armor record, distinct by Id and
    /// ordinally sorted for determinism. Empty when the armor belongs to no Outfit or none of
    /// its Outfit links resolve or produce a meaningful key.
    /// </summary>
    public static IReadOnlyList<NormalizedSetKey> ResolveAll(IArmorGetter armor, RecordIndex index)
    {
        var outfits = index.OutfitsForArmor(armor.FormKey);
        if (outfits.Count == 0)
        {
            return Array.Empty<NormalizedSetKey>();
        }

        var keys = new List<NormalizedSetKey>();
        foreach (var outfitKey in outfits)
        {
            if (!index.TryResolveOutfit(outfitKey, out var outfit))
            {
                continue;
            }

            var key = KeyNormalizer.NormalizeOutfitEditorId(outfit.EditorID);
            if (key is not null)
            {
                keys.Add(key);
            }
        }

        return keys
            .DistinctBy(k => k.Id, StringComparer.Ordinal)
            .OrderBy(k => k.Id, StringComparer.Ordinal)
            .ToList();
    }
}