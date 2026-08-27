using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class OutfitSetKeyResolverTests
{
    private static RecordIndex BuildIndex(TestTempDir dir, out List<ScanWarning> warnings)
    {
        return GroupingTestHarness.BuildIndex(dir, out warnings);
    }

    private static OutfitKeyResult Resolve(TestTempDir dir, FormKey armorKey)
    {
        var index = BuildIndex(dir, out _);
        Assert.True(index.TryResolveArmor(armorKey, out var armor));
        return OutfitSetKeyResolver.Resolve(armor, index);
    }

    [Fact]
    public void ArmorInOutfit_ResolvesNormalizedOutfitKey()
    {
        using var dir = new TestTempDir();

        var result = Resolve(dir, SyntheticGroupingUniverse.IronCuirassKey);

        Assert.NotNull(result.Key);
        Assert.Equal("ironarmor", result.Key!.Id);
        Assert.Equal("Iron Armor", result.Key.DisplayName);
        Assert.Equal(1, result.OutfitCount);
    }

    [Fact]
    public void ArmorInNoOutfit_FallsThroughToNullKey()
    {
        using var dir = new TestTempDir();

        var result = Resolve(dir, SyntheticGroupingUniverse.LeatherGauntletsKey);

        Assert.Null(result.Key);
        Assert.Equal(0, result.OutfitCount);
    }

    [Fact]
    public void MultiOutfitArmor_TieBreakIsDeterministicAlphabetical()
    {
        using var dir = new TestTempDir();

        var result = Resolve(dir, SyntheticGroupingUniverse.MultiBootsKey);

        Assert.NotNull(result.Key);
        Assert.Equal("aasharedset", result.Key!.Id);
        Assert.Equal(2, result.OutfitCount);
    }

    [Fact]
    public void SplitMembership_OutfitHalfAndFallbackHalf_ResolveToSameKey()
    {
        using var dir = new TestTempDir();

        var cuirass = Resolve(dir, SyntheticGroupingUniverse.NcCuirassKey);
        var helmet = Resolve(dir, SyntheticGroupingUniverse.NcHelmetKey);

        Assert.NotNull(cuirass.Key);
        Assert.NotNull(helmet.Key);
        Assert.Equal("nordiccarved", cuirass.Key!.Id);
        Assert.Equal(cuirass.Key.Id, helmet.Key.Id);
    }
}