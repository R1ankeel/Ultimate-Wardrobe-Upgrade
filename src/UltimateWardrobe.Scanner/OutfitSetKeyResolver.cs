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
/// Resolves the priority grouping signal from Outfit (OTFT) membership (Sprint 1.3.4).
/// An armor that belongs to at least one Outfit gets its set key from the normalized Outfit
/// EditorID. Multi-outfit armor picks the deterministic alphabetical-first key.
/// </summary>
public static class OutfitSetKeyResolver
{
    /// <summary>
    /// Resolves the Outfit key for the given armor record. Returns
    /// <see cref="OutfitKeyResult.Key"/> = null when the armor belongs to no Outfit.
    /// </summary>
    public static OutfitKeyResult Resolve(IArmorGetter armor, RecordIndex index)
    {
        var outfits = index.OutfitsForArmor(armor.FormKey);
        if (outfits.Count == 0)
        {
            return new OutfitKeyResult { Key = null, OutfitCount = 0 };
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

        if (keys.Count == 0)
        {
            return new OutfitKeyResult { Key = null, OutfitCount = outfits.Count };
        }

        var selected = keys.OrderBy(k => k.Id, StringComparer.Ordinal).First();
        return new OutfitKeyResult { Key = selected, OutfitCount = outfits.Count };
    }
}
