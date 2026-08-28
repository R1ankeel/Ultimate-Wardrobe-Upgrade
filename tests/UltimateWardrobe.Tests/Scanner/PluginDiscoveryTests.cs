using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class PluginDiscoveryTests
{
    [Fact]
    public void Vanilla_ResolvesDataFolder_AndDropsNonOfficialFiles()
    {
        using var dir = new TestTempDir();
        Directory.CreateDirectory(dir.File("Data"));
        File.WriteAllText(dir.File("Data\\Skyrim.esm"), "x");
        File.WriteAllText(dir.File("Data\\Dawnguard.esm"), "x");
        File.WriteAllText(dir.File("Data\\FakeA.esm"), "x");
        File.WriteAllText(dir.File("Data\\FakeB.esl"), "x");

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), new List<ScanWarning>());

        Assert.Equal(dir.File("Data"), discovery.DataPath);
        Assert.Equal(new[] { "Dawnguard.esm", "Skyrim.esm" }, discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
        Assert.All(discovery.Plugins, p => Assert.False(p.IsMainPlugin));
    }

    [Fact]
    public void Vanilla_EmptyNames_ScansOfficialMasters_PlusUpdate_AsResolutionOnly()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("Skyrim.esm"), "x");
        File.WriteAllText(dir.File("Dawnguard.esm"), "x");
        File.WriteAllText(dir.File("HearthFires.esm"), "x");
        File.WriteAllText(dir.File("Dragonborn.esm"), "x");
        File.WriteAllText(dir.File("Update.esm"), "x");
        File.WriteAllText(dir.File("ccbgssse037-curios.esl"), "x");
        File.WriteAllText(dir.File("_ResourcePack.esl"), "x");
        File.WriteAllText(dir.File("notes.txt"), "x");
        var warnings = new List<ScanWarning>();

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), warnings);

        Assert.Equal(
            new[] { "Dawnguard.esm", "Dragonborn.esm", "HearthFires.esm", "Skyrim.esm", "Update.esm" },
            discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
        var update = Assert.Single(discovery.Plugins, p => p.IsResolutionOnly);
        Assert.Equal("Update.esm", update.ModKey.FileName.ToString());
        Assert.All(
            discovery.Plugins.Where(p => !p.IsResolutionOnly),
            p => Assert.NotEqual("Update.esm", p.ModKey.FileName.ToString()));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Vanilla_EmptyNames_MissingOfficialMaster_WarnsAndSkips()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("Skyrim.esm"), "x");
        File.WriteAllText(dir.File("Dawnguard.esm"), "x");
        var warnings = new List<ScanWarning>();

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), warnings);

        Assert.Equal(
            new[] { "Dawnguard.esm", "Skyrim.esm" },
            discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
        Assert.Contains(warnings, w => w.Message.Contains("HearthFires.esm", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Message.Contains("Dragonborn.esm", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Message.Contains("Skyrim.esm", StringComparison.Ordinal));
    }

    [Fact]
    public void Vanilla_FallsBackToRoot_WhenNoDataFolderButOfficialEsmsPresent()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("Skyrim.esm"), "x");
        File.WriteAllText(dir.File("Dragonborn.esm"), "x");

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), new List<ScanWarning>());

        Assert.Equal(dir.Root, discovery.DataPath);
        Assert.Equal(new[] { "Dragonborn.esm", "Skyrim.esm" }, discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
    }

    [Fact]
    public void Vanilla_ExplicitNames_AreHonored_AndMissingOnesWarn()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("Kept.esp"), "x");
        var warnings = new List<ScanWarning>();

        var discovery = new PluginDiscovery().Discover(
            new VanillaCatalogSource(dir.Root, new[] { "Kept.esp", "Missing.esp" }),
            warnings);

        Assert.Equal(new[] { "Kept.esp" }, discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
        var warning = Assert.Single(warnings);
        Assert.Contains("Missing.esp", warning.Message);
    }

    [Fact]
    public void Vanilla_MissingRoot_Throws()
    {
        using var dir = new TestTempDir();
        var gone = dir.File("gone");

        var exception = Assert.Throws<CatalogScanException>(
            () => new PluginDiscovery().Discover(new VanillaCatalogSource(gone), new List<ScanWarning>()));

        Assert.Contains(gone, exception.Message);
    }

    [Fact]
    public void Story_MainPlugin_IsMarked_AndMastersAreIncluded()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("Main.esp"), "x");
        File.WriteAllText(dir.File("Core.esm"), "x");
        Directory.CreateDirectory(dir.File("Data"));
        File.WriteAllText(dir.File("Data\\Other.esl"), "x");

        var discovery = new PluginDiscovery().Discover(
            new StoryModCatalogSource(dir.Root, "Main.esp", new[] { "Core.esm" }),
            new List<ScanWarning>());

        Assert.True(discovery.Plugins.Single(p => p.IsMainPlugin).IsMainPlugin);
        Assert.Equal(new[] { "Main.esp", "Core.esm" }, discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
        Assert.Equal(dir.File("Data"), discovery.DataPath);
    }

    [Fact]
    public void Story_MissingMainPlugin_Throws_WithFriendlyMessage()
    {
        using var dir = new TestTempDir();

        var exception = Assert.Throws<CatalogScanException>(
            () => new PluginDiscovery().Discover(new StoryModCatalogSource(dir.Root, "Nope.esp"), new List<ScanWarning>()));

        Assert.Contains("Nope.esp", exception.Message);
        Assert.Contains("Main plugin", exception.Message);
    }

    [Fact]
    public void Story_MissingExplicitMaster_IsRecorded_AsResultNotThrow()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("Main.esp"), "x");
        var warnings = new List<ScanWarning>();

        var discovery = new PluginDiscovery().Discover(
            new StoryModCatalogSource(dir.Root, "Main.esp", new[] { "Ghost.esm" }),
            warnings);

        Assert.DoesNotContain(discovery.Plugins, p => !p.IsMainPlugin);
        Assert.Equal(new[] { "Ghost.esm" }, discovery.MissingExplicitMasters.Select(m => m.FileName.ToString()).ToArray());
        Assert.Empty(warnings);
    }

    [Fact]
    public void Story_MissingRoot_Throws()
    {
        using var dir = new TestTempDir();
        var gone = dir.File("gone");

        var exception = Assert.Throws<CatalogScanException>(
            () => new PluginDiscovery().Discover(new StoryModCatalogSource(gone, "Main.esp"), new List<ScanWarning>()));

        Assert.Contains(gone, exception.Message);
    }

    [Fact]
    public void Vanilla_EmptyDataFolder_YieldsNoPlugins_AndWarnsForEveryOfficialMaster()
    {
        using var dir = new TestTempDir();
        Directory.CreateDirectory(dir.File("Data"));
        var warnings = new List<ScanWarning>();

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), warnings);

        Assert.Empty(discovery.Plugins);
        Assert.Equal(4, warnings.Count);
    }
}