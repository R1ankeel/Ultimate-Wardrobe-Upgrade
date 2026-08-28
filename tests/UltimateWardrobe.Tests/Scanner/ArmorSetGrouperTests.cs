using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class ArmorSetGrouperTests
{
    private static GroupingResult BuildGrouping(TestTempDir dir, out List<ScanWarning> warnings)
    {
        return GroupingTestHarness.Group(dir, out warnings);
    }

    private static int SkippedFor(GroupingResult result, SkipReason reason)
    {
        return result.SkippedByReason.TryGetValue(reason, out var count) ? count : 0;
    }

    [Fact]
    public void OutfitDrivenIronSet_GroupsWithoutEditorIdHints()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        var set = Assert.Single(result.Sets, s => s.Id == "ironarmor");
        Assert.Equal("Iron Armor", set.DisplayName);
        Assert.Equal(3, set.Members.Count);
        Assert.Contains(set.Members, m => m.EditorId == "0A2C8841");
        Assert.Contains(set.Members, m => m.EditorId == "0A2C8842");
        Assert.Contains(set.Members, m => m.EditorId == "0A2C8843");

        foreach (var pieceEditorId in new[] { "0A2C8841", "0A2C8842", "0A2C8843" })
        {
            Assert.DoesNotContain(result.Sets, s => s.Id == pieceEditorId.ToLowerInvariant());
        }
    }

    [Fact]
    public void MultiFamilyWardrobeOutfit_DoesNotBridgeFamilies()
    {
        using var dir = new TestTempDir();
        SyntheticWardrobeUniverse.Write(dir.Root);

        var warnings = new List<ScanWarning>();
        ModLoader loader = new();
        var loaded = loader.TryLoad(dir.File(SyntheticWardrobeUniverse.FileName), warnings);
        try
        {
            Assert.NotNull(loaded);
            var index = RecordIndex.Build(new[] { loaded! }, warnings);
            var correlated = new ArmorCorrelator().Correlate(index, warnings);
            var result = new ArmorSetGrouper().Group(correlated, index, warnings);

            Assert.DoesNotContain(result.Sets, s => s.Id == "mercenarymixer");

            var steel = Assert.Single(result.Sets, s => s.Members.Any(m => m.EditorId == "ArmorSteelCuirassA"));
            Assert.Equal(
                new[] { "ArmorSteelBootsA", "ArmorSteelCuirassA" },
                steel.Members.Select(m => m.EditorId).OrderBy(e => e, StringComparer.Ordinal));

            var iron = Assert.Single(result.Sets, s => s.Members.Any(m => m.EditorId == "ArmorIronCuirass"));
            Assert.Equal(new[] { "ArmorIronCuirass" }, iron.Members.Select(m => m.EditorId));
        }
        finally
        {
            loaded?.Dispose();
        }
    }

    [Fact]
    public void SplitMembershipSet_LandsInOneArmorSet_NeverFragments()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        var containingSets = result.Sets
            .Where(s => s.Members.Any(m => m.EditorId.Contains("NordicCarved")))
            .ToList();

        Assert.Single(containingSets);

        var set = containingSets[0];
        Assert.Equal("nordiccarved", set.Id);
        Assert.Equal("Nordic Carved", set.DisplayName);

        var pieceEditorIds = set.Members.Select(m => m.EditorId).OrderBy(e => e, StringComparer.Ordinal).ToList();
        Assert.Equal(
            new[]
            {
                "DLC2NordicCarvedBoots",
                "DLC2NordicCarvedCuirass",
                "DLC2NordicCarvedGauntlets",
                "DLC2NordicCarvedHelmet",
            },
            pieceEditorIds);
    }

    [Fact]
    public void SplitMembershipSet_SharedGauntletsAndBoots_DoNotLeakIntoOtherSets()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        foreach (var setId in new[] { "aasharedset", "leather", "ironarmor" })
        {
            var otherSet = Assert.Single(result.Sets, s => s.Id == setId);
            Assert.DoesNotContain(otherSet.Members, m => m.EditorId == "DLC2NordicCarvedGauntlets");
            Assert.DoesNotContain(otherSet.Members, m => m.EditorId == "DLC2NordicCarvedBoots");
        }
    }

    [Fact]
    public void FallbackEdidSets_GroupByNormalizedEditorId()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        var leather = Assert.Single(result.Sets, s => s.Id == "leather");
        Assert.Equal("Leather", leather.DisplayName);
        Assert.Equal(2, leather.Members.Count);

        Assert.Equal(
            new[] { "ArmorLeatherCuirass", "ArmorLeatherGauntlets" },
            leather.Members.Select(m => m.EditorId).OrderBy(e => e, StringComparer.Ordinal));
    }

    [Fact]
    public void CreatureSkin_SkippedAsCreatureRace_AndNeverAppearsAsSet()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        Assert.DoesNotContain(result.Sets, s => s.Members.Any(m => m.EditorId == "Boar"));
        Assert.Equal(1, SkippedFor(result, SkipReason.CreatureRace));
    }

    [Fact]
    public void VampireRaceArmor_NeverSkipped_AndGroupsNormally()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        var set = Assert.Single(result.Sets, s => s.Id == "vampirerobes");
        Assert.Single(set.Members);
        Assert.Equal("ClothesVampireRobes", set.Members[0].EditorId);

        Assert.Equal(1, SkippedFor(result, SkipReason.CreatureRace));
    }

    [Fact]
    public void MultiOutfitArmor_LandsInDeterministicSingleSet()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        var set = Assert.Single(result.Sets, s => s.Id == "aasharedset");
        Assert.Equal("Aa Shared Set", set.DisplayName);
        var member = Assert.Single(set.Members);
        Assert.Equal("SharedBoots", member.EditorId);

        Assert.DoesNotContain(result.Sets, s => s.Id == "zzsharedset");
    }

    [Fact]
    public void GarbageFiltering_PerReasonSkipCounts()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        Assert.Equal(1, SkippedFor(result, SkipReason.CreatureRace));
        Assert.Equal(1, SkippedFor(result, SkipReason.NoArmature));
        Assert.Equal(1, SkippedFor(result, SkipReason.EmptyModel));
        Assert.Equal(1, SkippedFor(result, SkipReason.NoSlot));
        Assert.Equal(1, SkippedFor(result, SkipReason.NoKeyword));
        Assert.Equal(0, SkippedFor(result, SkipReason.Other));

        Assert.Equal(5, result.SkippedByReason.Values.Sum());

        foreach (var skippedEditorId in new[] { "DanglingOnly", "EmptyModelBoots", "NoSlotRing", "NakedBody", "Boar" })
        {
            Assert.DoesNotContain(result.Sets, s => s.Members.Any(m => m.EditorId == skippedEditorId));
        }
    }

    [Fact]
    public void UnresolvableArmaRace_Warns_AndRecordKept()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out var warnings);

        Assert.Single(result.Sets, s => s.Id == "mystery" && s.Members.Count == 1);
        Assert.Equal(1, SkippedFor(result, SkipReason.CreatureRace));

        Assert.Contains(warnings, w => w.Message.Contains("could not be resolved")
                                      && w.Message.Contains("race") && w.EditorId == "MysteryGauntlets");
    }

    [Fact]
    public void MembersOrderedBySlotThenEditorId()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        var iron = Assert.Single(result.Sets, s => s.Id == "ironarmor");
        Assert.Equal(
            new[] { "0A2C8841", "0A2C8842", "0A2C8843" },
            iron.Members.Select(m => m.EditorId));

        var nordic = Assert.Single(result.Sets, s => s.Id == "nordiccarved");
        Assert.Equal(
            new[] { "DLC2NordicCarvedHelmet", "DLC2NordicCarvedCuirass", "DLC2NordicCarvedGauntlets", "DLC2NordicCarvedBoots" },
            nordic.Members.Select(m => m.EditorId));
    }

    [Fact]
    public void SetsOrderedDeterministicallyById()
    {
        using var dir = new TestTempDir();
        var result = BuildGrouping(dir, out _);

        var ids = result.Sets.Select(s => s.Id).ToList();
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
    }

    [Fact]
    public void RingsAndAmulets_SkippedAsJewelry_AndNeverAppearAsSets()
    {
        using var dir = new TestTempDir();
        var result = BuildFilteringGrouping(dir);

        Assert.Equal(2, SkippedFor(result, SkipReason.Jewelry));
        Assert.DoesNotContain(result.Sets, s => s.Members.Any(m => m.EditorId is "JewelRing" or "JewelAmulet"));
    }

    [Fact]
    public void Circlet_IsNotJewelry_AndStaysInCatalog()
    {
        using var dir = new TestTempDir();
        var result = BuildFilteringGrouping(dir);

        var set = Assert.Single(result.Sets, s => s.Id == "jewel");
        Assert.Equal("Jewel", set.DisplayName);
    }

    [Fact]
    public void EnchantedNameSuffixes_SkippedAsEnchanted_AndNeverAppearAsSets()
    {
        using var dir = new TestTempDir();
        var result = BuildFilteringGrouping(dir);

        Assert.Equal(4, SkippedFor(result, SkipReason.Enchanted));
        foreach (var editorId in new[] { "EnchMuffleBoots", "EnchOneHandedCuirass", "EnchDualRegenHelmet", "EnchLowercaseFireGauntlets" })
        {
            Assert.DoesNotContain(result.Sets, s => s.Members.Any(m => m.EditorId == editorId));
        }
    }

    [Fact]
    public void PlainArmor_IsUntouched_ByJewelryAndEnchantmentFilters()
    {
        using var dir = new TestTempDir();
        var result = BuildFilteringGrouping(dir);

        var set = Assert.Single(result.Sets, s => s.Id == "plainheavy");
        Assert.Equal("Plain Heavy", set.DisplayName);
        Assert.Equal(2, SkippedFor(result, SkipReason.Jewelry));
        Assert.Equal(4, SkippedFor(result, SkipReason.Enchanted));
        Assert.Equal(0, SkippedFor(result, SkipReason.NoSlot));
    }

    private static GroupingResult BuildFilteringGrouping(TestTempDir dir, out List<ScanWarning> warnings)
    {
        SyntheticFilteringUniverse.Write(dir.Root);
        warnings = new List<ScanWarning>();

        var loader = new ModLoader();
        using var loaded = loader.TryLoad(dir.File(SyntheticFilteringUniverse.FileName), warnings);
        Assert.NotNull(loaded);

        var index = RecordIndex.Build(new[] { loaded }, warnings);
        var correlated = new ArmorCorrelator().Correlate(index, warnings);
        return new ArmorSetGrouper().Group(correlated, index, warnings);
    }

    private static GroupingResult BuildFilteringGrouping(TestTempDir dir)
        => BuildFilteringGrouping(dir, out _);
}