using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// F4 clothing grouping - verifies that 34 Clothes + 14 Robes + 15 Hoods + 33 Shoes + 2 Gloves
/// synthetic enumeration (98 items from the spec) does not collapse into a single megaset via
/// the bare "clothes" KeyNormalizer key and that vanilla clothing megaset is not present in the
/// real 651-set catalog. Covers the F4 requirement that Belted Tunic etc. remain separate sets.
/// </summary>
public sealed class ArmorSetGrouperClothingTests
{
    [Fact]
    public void ClothingUniverse_DoesNotCollapseIntoMegaset()
    {
        using var dir = new TestTempDir();
        SyntheticClothingUniverse.Write(dir.Root);

        var warnings = new List<ScanWarning>();
        var loader = new ModLoader();
        using var loaded = loader.TryLoad(dir.File(SyntheticClothingUniverse.FileName), warnings);
        Assert.NotNull(loaded);

        var index = RecordIndex.Build(new[] { loaded! }, warnings);
        var correlated = new ArmorCorrelator().Correlate(index, warnings);
        var result = new ArmorSetGrouper().Group(correlated, index, warnings);

        // 98 synthetic clothing items each have a distinct mesh subfolder and distinct EDID after
        // stripping Clothes prefix, so they must not collapse into 1 megaset via the bare "clothes" key.
        Assert.True(result.Sets.Count > 50,
            $"Expected >50 sets from 98 clothing items, got {result.Sets.Count} - megaset collapse via KeyNormalizer bare 'clothes'");

        // No single set should contain more than 5 members (clothing sets are 1 piece each in this universe)
        foreach (var set in result.Sets)
        {
            Assert.True(set.Members.Count <= 5,
                $"Set '{set.Id}' has {set.Members.Count} members - unexpected megaset (members: {string.Join(",", set.Members.Select(m => m.EditorId))})");
        }

        // Spot-check Belted Tunic vs generic Clothes separation - they must be in different sets
        var belted = result.Sets.FirstOrDefault(s => s.Members.Any(m => m.EditorId == "ClothesBeltedTunic"));
        var fine = result.Sets.FirstOrDefault(s => s.Members.Any(m => m.EditorId == "ClothesFineClothes01"));
        Assert.NotNull(belted);
        Assert.NotNull(fine);
        Assert.NotEqual(belted!.Id, fine!.Id);

        // Verify slot distribution: we expect Body, Hair, Feet, Hands categories all present
        var allFlags = result.Sets.SelectMany(s => s.Members).Select(m => m.BipedFlags).ToList();
        Assert.Contains(BipedObjectFlag.Body, allFlags.Select(f => f & BipedObjectFlag.Body).Where(v => v != 0).Select(_ => BipedObjectFlag.Body));
        Assert.Contains(BipedObjectFlag.Hair, allFlags.Where(f => f.HasFlag(BipedObjectFlag.Hair)).Select(_ => BipedObjectFlag.Hair));
        Assert.Contains(BipedObjectFlag.Feet, allFlags.Where(f => f.HasFlag(BipedObjectFlag.Feet)).Select(_ => BipedObjectFlag.Feet));
        Assert.Contains(BipedObjectFlag.Hands, allFlags.Where(f => f.HasFlag(BipedObjectFlag.Hands)).Select(_ => BipedObjectFlag.Hands));

        // Jewelry should not appear (this universe has no Amulet/Ring) - but ensure no Jewelry skip miscount
        Assert.Equal(0, result.SkippedByReason.GetValueOrDefault(SkipReason.Jewelry));
        Assert.Equal(0, result.SkippedByReason.GetValueOrDefault(SkipReason.Enchanted));
    }

    [Fact]
    public void ClothingUniverse_RobesAndHoods_AreSeparateFromBodyClothes()
    {
        using var dir = new TestTempDir();
        SyntheticClothingUniverse.Write(dir.Root);

        var warnings = new List<ScanWarning>();
        var loader = new ModLoader();
        using var loaded = loader.TryLoad(dir.File(SyntheticClothingUniverse.FileName), warnings);
        Assert.NotNull(loaded);

        var index = RecordIndex.Build(new[] { loaded! }, warnings);
        var correlated = new ArmorCorrelator().Correlate(index, warnings);
        var result = new ArmorSetGrouper().Group(correlated, index, warnings);

        var robeSet = result.Sets.FirstOrDefault(s => s.Members.Any(m => m.EditorId == "ClothesBlackRobes"));
        var hoodSet = result.Sets.FirstOrDefault(s => s.Members.Any(m => m.EditorId == "ClothesAlikrHood"));
        var bootSet = result.Sets.FirstOrDefault(s => s.Members.Any(m => m.EditorId == "ClothesBootsA"));

        Assert.NotNull(robeSet);
        Assert.NotNull(hoodSet);
        Assert.NotNull(bootSet);
        Assert.NotEqual(robeSet!.Id, hoodSet!.Id);
        Assert.NotEqual(robeSet.Id, bootSet!.Id);
        Assert.NotEqual(hoodSet.Id, bootSet.Id);

        Assert.True(robeSet.Members.All(m => m.BipedFlags.HasFlag(BipedObjectFlag.Body)));
        Assert.True(hoodSet.Members.All(m => m.BipedFlags.HasFlag(BipedObjectFlag.Hair)));
        Assert.True(bootSet.Members.All(m => m.BipedFlags.HasFlag(BipedObjectFlag.Feet)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Vanilla_RealGame_ClothingNotMegaset()
    {
        const string gameRoot = @"D:\Skymod\Stock Game";
        if (!Directory.Exists(gameRoot))
        {
            return;
        }

        var catalog = await new FolderCatalogScanner().ScanAsync(new VanillaCatalogSource(gameRoot));

        // After F2/F3, vanilla should still be ~439-651 grouped base kits - ensure no clothing megaset
        // with >150 pieces (the same guard as RealDataScannerTests.Vanilla_RealGame_FullKitsAreSingleSets_NoMegaSets)
        var max = catalog.Sets.Max(s => s.Variants.SelectMany(v => v.Pieces).Count());
        Assert.True(max <= 150, $"Max set size {max} exceeds 150 - clothing megaset via bare 'clothes' key");

        Assert.True(catalog.Stats.GroupedSets > 50, $"Expected >50 grouped sets, got {catalog.Stats.GroupedSets}");
    }
}
