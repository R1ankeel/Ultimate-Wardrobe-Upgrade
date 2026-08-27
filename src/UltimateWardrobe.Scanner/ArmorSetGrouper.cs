using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// A named set produced by the Outfit-first / EDID-mesh-fallback grouping pipeline, before
/// gender/weight variant assembly (Sprint 1.4).
/// </summary>
public sealed record GroupedSet
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<CorrelatedArmor> Members { get; init; }
}

/// <summary>
/// Result of the <see cref="ArmorSetGrouper"/> pipeline.
/// </summary>
public sealed record GroupingResult
{
    public required IReadOnlyList<GroupedSet> Sets { get; init; }

    public required IReadOnlyDictionary<SkipReason, int> SkippedByReason { get; init; }
}

/// <summary>
/// Groups raw correlated ARMO records into <see cref="GroupedSet"/>s (Sprint 1.3.5). The
/// pipeline: creature-skin pre-filter (<see cref="PlayableRaceFilter"/>) -> Outfit-first stage
/// (<see cref="OutfitSetKeyResolver"/>) -> EDID/mesh fallback stage (<see cref="KeyNormalizer"/>)
/// -> group merge, with garbage filtering into <see cref="SkipReason"/> counts and deterministic
/// ordering (sets by Id, members by BOD2 slot order then EditorId).
/// </summary>
public sealed class ArmorSetGrouper
{
    private static readonly IReadOnlyList<BipedObjectFlag> SlotOrder =
    [
        BipedObjectFlag.Head,
        BipedObjectFlag.Hair,
        BipedObjectFlag.Body,
        BipedObjectFlag.Hands,
        BipedObjectFlag.Forearms,
        BipedObjectFlag.Amulet,
        BipedObjectFlag.Ring,
        BipedObjectFlag.Feet,
        BipedObjectFlag.Calves,
        BipedObjectFlag.Shield,
        BipedObjectFlag.Tail,
        BipedObjectFlag.LongHair,
        BipedObjectFlag.Circlet,
        BipedObjectFlag.Ears,
    ];

    public GroupingResult Group(
        IEnumerable<CorrelatedArmor> correlated,
        RecordIndex index,
        List<ScanWarning> warnings)
    {
        var skipped = new Dictionary<SkipReason, int>();
        var byKey = new SortedDictionary<string, GroupedSet>(StringComparer.Ordinal);

        foreach (var armor in correlated)
        {
            if (TrySkipCreature(armor, index, warnings))
            {
                Track(skipped, SkipReason.CreatureRace);
                continue;
            }

            var garbage = ClassifyGarbage(armor, index);
            if (garbage is not null)
            {
                Track(skipped, garbage.Value);
                continue;
            }

            var key = ResolveKey(armor, index);
            if (key is null)
            {
                Track(skipped, SkipReason.Other);
                continue;
            }

            if (!byKey.TryGetValue(key.Id, out var set))
            {
                set = new GroupedSet { Id = key.Id, DisplayName = key.DisplayName, Members = new List<CorrelatedArmor>() };
                byKey[key.Id] = set;
            }

            ((List<CorrelatedArmor>)set.Members).Add(armor);
        }

        var sets = byKey.Values
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => s with { Members = OrderMembers(s.Members) })
            .ToList();

        return new GroupingResult { Sets = sets, SkippedByReason = skipped };
    }

    private static bool TrySkipCreature(CorrelatedArmor armor, RecordIndex index, List<ScanWarning> warnings)
    {
        if (armor.RaceLink is null || armor.RaceLink.FormKey.IsNull)
        {
            return false;
        }

        if (!index.TryResolveRace(armor.RaceLink.FormKey, out var race))
        {
            warnings.Add(new ScanWarning(
                $"Armor '{armor.EditorId}' (FormId {armor.FormId:X}) references a race '{armor.RaceLink.FormKey}' that could not " +
                "be resolved in the loaded file set; the record is kept and grouped by its fallback key.",
                armor.EditorId));
            return false;
        }

        return !PlayableRaceFilter.IsInPlayableWhitelist(race.EditorID);
    }

    private static SkipReason? ClassifyGarbage(CorrelatedArmor armor, RecordIndex index)
    {
        if (armor.FirstAddon is null)
        {
            return SkipReason.NoArmature;
        }

        if (string.IsNullOrWhiteSpace(armor.MeshPath))
        {
            return SkipReason.EmptyModel;
        }

        if (armor.BipedFlags == (BipedObjectFlag)0)
        {
            return SkipReason.NoSlot;
        }

        if (armor.BipedFlags.HasFlag(BipedObjectFlag.Body) && !HasArmorKeyword(armor, index))
        {
            return SkipReason.NoKeyword;
        }

        return null;
    }

    private static bool HasArmorKeyword(CorrelatedArmor armor, RecordIndex index)
    {
        if (armor.Armor.Keywords is null)
        {
            return false;
        }

        foreach (var k in armor.Armor.Keywords)
        {
            if (k is null || k.FormKey.IsNull || !index.TryResolveKeyword(k.FormKey, out var keyword))
            {
                continue;
            }

            if (keyword.EditorID is "ArmorHeavy" or "ArmorLight" or "ArmorClothing")
            {
                return true;
            }
        }

        return false;
    }

    private static NormalizedSetKey? ResolveKey(CorrelatedArmor armor, RecordIndex index)
    {
        var outfit = OutfitSetKeyResolver.Resolve(armor.Armor, index);
        if (outfit.Key is not null)
        {
            return outfit.Key;
        }

        return KeyNormalizer.NormalizeEditorId(armor.EditorId)
            ?? KeyNormalizer.NormalizeMeshFolder(armor.MeshPath);
    }

    private static IReadOnlyList<CorrelatedArmor> OrderMembers(IReadOnlyList<CorrelatedArmor> members)
    {
        return members
            .OrderBy(m => SlotIndex(m.BipedFlags))
            .ThenBy(m => m.EditorId, StringComparer.Ordinal)
            .ToList();
    }

    private static int SlotIndex(BipedObjectFlag flags)
    {
        for (var i = 0; i < SlotOrder.Count; i++)
        {
            if (flags.HasFlag(SlotOrder[i]))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static void Track(Dictionary<SkipReason, int> skipped, SkipReason reason)
    {
        skipped[reason] = skipped.TryGetValue(reason, out var count) ? count + 1 : 1;
    }
}
