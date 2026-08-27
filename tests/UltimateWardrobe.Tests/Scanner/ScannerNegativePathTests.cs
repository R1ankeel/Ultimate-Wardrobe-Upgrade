using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Sprint 1.6.4 negative paths: corrupt/empty plugins must not abort a scan, a story-mod main
/// plugin may proceed when an explicit master is missing (with a warning).
/// </summary>
public sealed class ScannerNegativePathTests
{
    [Fact]
    public async Task ScanAsync_EmptyPlugin_Warns_AndReturnsEmptyCatalog()
    {
        using var dir = new TestTempDir();
        var pluginPath = dir.File("Empty.esp");
        await File.WriteAllBytesAsync(pluginPath, Array.Empty<byte>());

        var catalog = await new FolderCatalogScanner().ScanAsync(
            new VanillaCatalogSource(dir.Root, new[] { "Empty.esp" }));

        Assert.Contains(catalog.Warnings, w => w.Message.Contains("Empty.esp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, catalog.Stats.TotalArmo);
        Assert.Empty(catalog.Sets);
    }

    [Fact]
    public async Task ScanAsync_CorruptPlugin_Warns_ButDoesNotAbort()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteCorruptPlugin(dir.Root);

        var catalog = await new FolderCatalogScanner().ScanAsync(
            new VanillaCatalogSource(dir.Root, new[] { "Broken.esp" }));

        Assert.Contains(catalog.Warnings, w => w.Message.Contains("Broken.esp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, catalog.Stats.TotalArmo);
    }

    [Fact]
    public async Task ScanAsync_StoryMod_MissingMaster_Warns_AndStillScansMain()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMain(dir.Root);

        var catalog = await new FolderCatalogScanner().ScanAsync(
            new StoryModCatalogSource(dir.Root, SyntheticSkyrimMods.MainFileName, new[] { SyntheticSkyrimMods.MasterFileName }));

        Assert.Contains(catalog.Warnings, w => w.Message.Contains(SyntheticSkyrimMods.MasterFileName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, catalog.Stats.TotalArmo);
        Assert.True(catalog.Stats.GroupedSets >= 1);
    }

    [Fact]
    public async Task ScanAsync_StoryMod_WithOnlyMainPlugin_ScansWithoutMastersList()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);

        var catalog = await new FolderCatalogScanner().ScanAsync(
            new StoryModCatalogSource(dir.Root, SyntheticSkyrimMods.MiniUniverseFileName));

        Assert.DoesNotContain(catalog.Warnings, w => w.Message.Contains("Master", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(12, catalog.Sets.Count);
    }
}