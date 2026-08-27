using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class RecordIndexGroupingTests
{
    private static RecordIndex BuildIndex(TestTempDir dir, out List<ScanWarning> warnings)
    {
        return GroupingTestHarness.BuildIndex(dir, out warnings);
    }

    [Fact]
    public void OutfitCache_ContainsAllOutfits()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);

        Assert.Equal(4, index.OutfitCount);
        Assert.True(index.TryResolveOutfit(SyntheticGroupingUniverse.NordicCarvedOutfitKey, out var outfit));
        Assert.Equal("DLC2NordicCarved", outfit.EditorID);
    }

    [Fact]
    public void OutfitsForArmor_ReverseMembershipMap()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);

        Assert.Contains(SyntheticGroupingUniverse.IronOutfitKey, index.OutfitsForArmor(SyntheticGroupingUniverse.IronCuirassKey));
        Assert.Equal(2, index.OutfitsForArmor(SyntheticGroupingUniverse.MultiBootsKey).Count);
        Assert.Empty(index.OutfitsForArmor(SyntheticGroupingUniverse.NcGauntletsKey));
    }

    [Fact]
    public void RaceCache_IsSparse_KeyedByArmaRaceReferencesOnly()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);

        Assert.Equal(2, index.RaceCount);
        Assert.True(index.TryResolveRace(SyntheticGroupingUniverse.BoarRaceKey, out var boarRace));
        Assert.Equal("BoarRace", boarRace.EditorID);
        Assert.True(index.TryResolveRace(SyntheticGroupingUniverse.NordVampireRaceKey, out _));
    }

    [Fact]
    public void UnresolvableArmaRaceKey_IsNotCached_AndResolveFails()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);

        Assert.False(index.TryResolveRace(SyntheticGroupingUniverse.UnresolvableRaceKey, out _));
    }
}