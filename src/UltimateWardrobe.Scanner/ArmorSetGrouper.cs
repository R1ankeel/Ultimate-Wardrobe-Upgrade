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

    /// <summary>
    /// True when at least one member resolved its key from an Outfit (OTFT) membership signal
    /// (Sprint 1.3.4). Fed to <see cref="ScanReport.OutfitGroupedSetCount"/> for the 1.7.3
    /// tuning pass.
    /// </summary>
    public bool GroupedViaOutfit { get; init; }
}

/// <summary>
/// Result of the <see cref="ArmorSetGrouper"/> pipeline.
/// </summary>
public sealed record GroupingResult
{
    public required IReadOnlyList<GroupedSet> Sets { get; init; }

    public required IReadOnlyDictionary<SkipReason, int> SkippedByReason { get; init; }

    /// <summary>
    /// Number of sets whose key came from the Outfit signal (Sprint 1.5.3, tuning in 1.7.3).
    /// </summary>
    public int OutfitGroupedSetCount { get; init; }
}

/// <summary>
/// Groups raw correlated ARMO records into <see cref="GroupedSet"/>s (Sprint 1.3.5). The
/// pipeline: creature-skin pre-filter (<see cref="PlayableRaceFilter"/>) -> candidate-key
/// collection (EDID/mesh fallback key plus every normalized Outfit key, Sprint 1.7.3) ->
/// wardrobe-outfit filtering (multi-family NPC wardrobes are dropped, Sprint 1.7.3) ->
/// community merge -> agreement rule (each community picks the candidate key with the most
/// member agreement; ties prefer Outfit-originating keys, then alphabetical) -> group merge,
/// with garbage filtering into <see cref="SkipReason"/> counts and deterministic ordering (sets
/// by Id, members by BOD2 slot order then EditorId).
/// </summary>
public sealed class ArmorSetGrouper
{
    public GroupingResult Group(
        IEnumerable<CorrelatedArmor> correlated,
        RecordIndex index,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        var skipped = new Dictionary<SkipReason, int>();
        var accepted = new List<CorrelatedArmor>();
        var candidates = new List<CandidateKeySet>();

        foreach (var armor in correlated)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            var candidate = CollectCandidates(armor, index);
            if (candidate is null)
            {
                Track(skipped, SkipReason.Other);
                continue;
            }

            accepted.Add(armor);
            candidates.Add(candidate);
        }

        var sets = MergeByAgreement(accepted, candidates);

        return new GroupingResult
        {
            Sets = sets,
            SkippedByReason = skipped,
            OutfitGroupedSetCount = sets.Count(s => s.GroupedViaOutfit),
        };
    }

    private static CandidateKeySet CollectCandidates(CorrelatedArmor armor, RecordIndex index)
    {
        var edidKey = KeyNormalizer.NormalizeEditorId(armor.EditorId)
                      ?? KeyNormalizer.NormalizeMeshFolder(armor.MeshPath);
        if (edidKey is null)
        {
            return null!;
        }

        var outfitKeys = OutfitSetKeyResolver.ResolveAll(armor.Armor, index);

        var keys = new List<NormalizedSetKey> { edidKey };
        keys.AddRange(outfitKeys);
        var distinct = keys.DistinctBy(k => k.Id, StringComparer.Ordinal).ToList();

        return new CandidateKeySet
        {
            Keys = distinct,
            OutfitIds = outfitKeys.Select(k => k.Id).ToHashSet(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Merges accepted armors into sets: armors sharing any candidate key form a community
    /// (union-find), then each community is decided by the agreement rule - the candidate key
    /// with the most member votes; ties prefer Outfit-originating keys, then the ordinal-first
    /// key. A shared EDID base (e.g. all plain Iron pieces) or a shared Outfit (e.g. the
    /// split-membership half) keeps a full kit in ONE set. Multi-family "wardrobe" outfit keys
    /// are filtered out first so vanilla NPC outfits cannot tie unrelated families together.
    /// </summary>
    private static List<GroupedSet> MergeByAgreement(IReadOnlyList<CorrelatedArmor> accepted, IReadOnlyList<CandidateKeySet> candidates)
    {
        candidates = FilterWardrobeOutfits(candidates);

        var parent = new int[accepted.Count];
        var keyToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < accepted.Count; i++)
        {
            parent[i] = i;
            foreach (var key in candidates[i].Keys)
            {
                if (keyToIndex.TryGetValue(key.Id, out var other))
                {
                    Union(parent, i, other);
                }
                else
                {
                    keyToIndex[key.Id] = i;
                }
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < accepted.Count; i++)
        {
            var root = Find(parent, i);
            if (!groups.TryGetValue(root, out var members))
            {
                members = new List<int>();
                groups[root] = members;
            }

            members.Add(i);
        }

        var byKey = new SortedDictionary<string, GroupedSet>(StringComparer.Ordinal);
        var viaOutfit = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.Values)
        {
            var winner = Winner(group, candidates);
            var usedOutfit = group.Any(i => candidates[i].OutfitIds.Contains(winner.Id));
            if (usedOutfit)
            {
                viaOutfit.Add(winner.Id);
            }

            if (!byKey.TryGetValue(winner.Id, out var set))
            {
                set = new GroupedSet { Id = winner.Id, DisplayName = winner.DisplayName, Members = new List<CorrelatedArmor>() };
                byKey[winner.Id] = set;
            }

            ((List<CorrelatedArmor>)set.Members).AddRange(group.Select(i => accepted[i]));
        }

        return byKey.Values
            .Select(s => s with
            {
                GroupedViaOutfit = viaOutfit.Contains(s.Id),
                Members = OrderMembers(s.Members),
            })
            .ToList();
    }

    /// <summary>
    /// Removes "wardrobe" outfit keys - Outfit EditorIDs that carry armor from several distinct
    /// EDID families and where not every carrier has that Outfit as its only Outfit signal
    /// (vanilla NPC wardrobes like cwmission04outfitimperial mix Iron/Steel/Leather, so they
    /// must not tie unrelated families into one mega-set). An Outfit key is kept verbatim when
    /// all its carriers share one family, or when all its carriers are exclusively in it (the
    /// OUTFIT-driven iron set from Sprints 1.3.4/1.3.6 has anonymous piece EDIDs and survives).
    /// </summary>
    private static IReadOnlyList<CandidateKeySet> FilterWardrobeOutfits(IReadOnlyList<CandidateKeySet> candidates)
    {
        var carriersByOutfit = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            foreach (var outfitId in candidates[i].OutfitIds)
            {
                if (!carriersByOutfit.TryGetValue(outfitId, out var carriers))
                {
                    carriers = new List<int>();
                    carriersByOutfit[outfitId] = carriers;
                }

                carriers.Add(i);
            }
        }

        var drop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in carriersByOutfit)
        {
            var carriers = pair.Value;

            var families = new HashSet<string>(StringComparer.Ordinal);
            var allExclusive = true;
            foreach (var i in carriers)
            {
                families.Add(candidates[i].Keys[0].Id);
                if (candidates[i].OutfitIds.Count != 1)
                {
                    allExclusive = false;
                }
            }

            if (families.Count > 1 && !allExclusive)
            {
                drop.Add(pair.Key);
            }
        }

        if (drop.Count == 0)
        {
            return candidates;
        }

        var filtered = new List<CandidateKeySet>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var keys = candidate.Keys
                .Where(k => !candidate.OutfitIds.Contains(k.Id) || !drop.Contains(k.Id))
                .ToList();
            var outfits = candidate.OutfitIds.Where(id => !drop.Contains(id)).ToHashSet(StringComparer.Ordinal);
            filtered.Add(candidate with { Keys = keys, OutfitIds = outfits });
        }

        return filtered;
    }

    private static NormalizedSetKey Winner(List<int> group, IReadOnlyList<CandidateKeySet> candidates)
    {
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var outfitPresent = new HashSet<string>(StringComparer.Ordinal);

        foreach (var i in group)
        {
            outfitPresent.UnionWith(candidates[i].OutfitIds);
            foreach (var key in candidates[i].Keys)
            {
                votes[key.Id] = votes.TryGetValue(key.Id, out var count) ? count + 1 : 1;
                displayNames.TryAdd(key.Id, key.DisplayName);
            }
        }

        var max = votes.Values.Count == 0 ? 0 : votes.Values.Max();
        var contenders = votes.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();

        var outfitContenders = contenders.Where(k => outfitPresent.Contains(k)).ToList();
        if (outfitContenders.Count > 0)
        {
            contenders = outfitContenders;
        }

        var winnerId = contenders.OrderBy(k => k, StringComparer.Ordinal).First();
        return new NormalizedSetKey { Id = winnerId, DisplayName = displayNames[winnerId] };
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }

        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        var ra = Find(parent, a);
        var rb = Find(parent, b);
        if (ra != rb)
        {
            parent[ra] = rb;
        }
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

    private static IReadOnlyList<CorrelatedArmor> OrderMembers(IReadOnlyList<CorrelatedArmor> members)
    {
        return members
            .OrderBy(m => SlotIndex(m.BipedFlags))
            .ThenBy(m => m.EditorId, StringComparer.Ordinal)
            .ToList();
    }

    private static int SlotIndex(BipedObjectFlag flags)
    {
        return BipedSlotMapper.SlotIndex(flags);
    }

    private static void Track(Dictionary<SkipReason, int> skipped, SkipReason reason)
    {
        skipped[reason] = skipped.TryGetValue(reason, out var count) ? count + 1 : 1;
    }

    private sealed record CandidateKeySet
    {
        public required IReadOnlyList<NormalizedSetKey> Keys { get; init; }

        public required HashSet<string> OutfitIds { get; init; }
    }
}