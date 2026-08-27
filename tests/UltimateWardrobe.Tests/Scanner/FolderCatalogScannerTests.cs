using System.Text.Json;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class FolderCatalogScannerTests
{
    private static VanillaCatalogSource VanillaUniverseSource(TestTempDir dir)
    {
        return new VanillaCatalogSource(dir.Root, new[] { SyntheticGroupingUniverse.FileName });
    }

    private static StoryModCatalogSource StoryUniverseSource(TestTempDir dir)
    {
        return new StoryModCatalogSource(dir.Root, SyntheticGroupingUniverse.FileName);
    }

    [Fact]
    public async Task ScanAsync_OnSyntheticUniverse_ProducesDeterministicCatalog()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();

        var first = await scanner.ScanAsync(VanillaUniverseSource(dir));
        var second = await scanner.ScanAsync(VanillaUniverseSource(dir));

        var options = CatalogCacheStore.JsonOptions;
        var jsonA = JsonSerializer.Serialize(first, options);
        var jsonB = JsonSerializer.Serialize(second, options);
        Assert.Equal(jsonA, jsonB);
    }

    [Fact]
    public async Task ScanAsync_OnSyntheticUniverse_FillsExpectedStats()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();

        var catalog = await scanner.ScanAsync(VanillaUniverseSource(dir));

        Assert.Equal(17, catalog.Stats.TotalArmo);
        Assert.Equal(16, catalog.Stats.TotalArma);
        Assert.Equal(6, catalog.Stats.GroupedSets);
        Assert.Equal(5, catalog.Stats.Skipped);
        Assert.Equal(
            new Dictionary<SkipReason, int>
            {
                [SkipReason.CreatureRace] = 1,
                [SkipReason.NoArmature] = 1,
                [SkipReason.EmptyModel] = 1,
                [SkipReason.NoSlot] = 1,
                [SkipReason.NoKeyword] = 1,
            },
            catalog.Stats.SkippedByReason);
        Assert.Equal(24, catalog.Stats.MissingFiles);
    }

    [Fact]
    public async Task ScanAsync_OnSyntheticUniverse_FillsReport()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();

        await scanner.ScanAsync(VanillaUniverseSource(dir));

        Assert.NotNull(scanner.LastReport);
        Assert.Equal(3, scanner.LastReport!.OutfitGroupedSetCount);
        Assert.Equal(6, scanner.LastReport.Stats.GroupedSets);

        Assert.Equal(2, scanner.LastReport.Warnings.Count);
        var raceWarning = Assert.Single(scanner.LastReport.Warnings, w => w.EditorId == "MysteryGauntlets");
        Assert.Contains("race", raceWarning.Message, StringComparison.OrdinalIgnoreCase);
        var armaWarning = Assert.Single(scanner.LastReport.Warnings, w => w.EditorId == "DanglingOnly");
        Assert.Contains("armature", armaWarning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_OnSyntheticUniverse_MissingFilesMatchPieceCount()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();

        var catalog = await scanner.ScanAsync(VanillaUniverseSource(dir));

        var pieceCount = catalog.Sets.Sum(s => s.Variants.Sum(v => v.Pieces.Count));
        Assert.Equal(pieceCount, catalog.Stats.MissingFiles);
    }

    [Fact]
    public async Task ScanAsync_StoryModSource_LoadsMainPluginAndGroups()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();

        var catalog = await scanner.ScanAsync(StoryUniverseSource(dir));

        Assert.Equal(CatalogSourceKind.StoryMod, catalog.Source.Kind);
        Assert.Equal(6, catalog.Sets.Count);
        Assert.Contains(catalog.Sets, s => s.Id == "ironarmor" && s.DisplayName == "Iron Armor");
    }

    [Fact]
    public async Task ScanAsync_BuildsAllExpectedSets()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();

        var catalog = await scanner.ScanAsync(VanillaUniverseSource(dir));

        Assert.Equal(
            new[] { "aasharedset", "ironarmor", "leather", "mystery", "nordiccarved", "vampirerobes" },
            catalog.Sets.Select(s => s.Id));
    }

    [Fact]
    public async Task ScanAsync_MissingRoot_ThrowsCatalogScanException()
    {
        using var dir = new TestTempDir();
        var scanner = new FolderCatalogScanner();
        var source = new VanillaCatalogSource(dir.File("DoesNotExist"));

        var ex = await Assert.ThrowsAsync<CatalogScanException>(() => scanner.ScanAsync(source));
        Assert.Contains("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_MissingMainPlugin_ThrowsCatalogScanException()
    {
        using var dir = new TestTempDir();
        var scanner = new FolderCatalogScanner();
        var source = new StoryModCatalogSource(dir.Root, "Missing.esp");

        var ex = await Assert.ThrowsAsync<CatalogScanException>(() => scanner.ScanAsync(source));
        Assert.Contains("was not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();
        var source = VanillaUniverseSource(dir);

        var token = new CancellationToken(canceled: true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(source, token));
    }

    [Fact]
    public async Task ScanAsync_MissingExplicitPlugin_WarnsAndScansRest()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();
        var source = new VanillaCatalogSource(dir.Root, new[] { "Ghost.esp", SyntheticGroupingUniverse.FileName });

        var catalog = await scanner.ScanAsync(source);

        Assert.Single(catalog.Warnings, w => w.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(17, catalog.Stats.TotalArmo);
    }
}