using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class LoadOrderBuilderTests
{
    private static VanillaCatalogSource EsmDiscovery(TestTempDir dir)
    {
        return new VanillaCatalogSource(
            dir.Root,
            Directory.GetFiles(dir.Root, "*.esp").Select(Path.GetFileName).Cast<string>().ToArray());
    }

    [Fact]
    public void OrdersMastersBeforeDependents_Recursively()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "FakeA.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "FakeB.esp", "FakeA.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "FakeC.esp", "FakeB.esp");
        var discovery = new PluginDiscovery().Discover(EsmDiscovery(dir), new List<ScanWarning>());

        var order = new LoadOrderBuilder(new ModLoader()).Build(discovery, new List<ScanWarning>());

        Assert.Equal(new[] { "FakeA.esp", "FakeB.esp", "FakeC.esp" }, order.Select(p => p.ModKey.FileName.ToString()).ToArray());
    }

    [Fact]
    public void DeduplicatesSharedMasters()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "Shared.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "One.esp", "Shared.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "Two.esp", "Shared.esp");
        var discovery = new PluginDiscovery().Discover(EsmDiscovery(dir), new List<ScanWarning>());

        var order = new LoadOrderBuilder(new ModLoader()).Build(discovery, new List<ScanWarning>());

        Assert.Equal(3, order.Count);
        Assert.Single(order, p => p.ModKey.FileName.ToString() == "Shared.esp");
        Assert.Equal("Shared.esp", order[0].ModKey.FileName.ToString());
    }

    [Fact]
    public void BreaksMasterCycles_AndKeepsEveryPluginOnce()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "FakeX.esp", "FakeY.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "FakeY.esp", "FakeX.esp", "Base.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "Base.esp");
        var discovery = new PluginDiscovery().Discover(EsmDiscovery(dir), new List<ScanWarning>());

        var order = new LoadOrderBuilder(new ModLoader()).Build(discovery, new List<ScanWarning>());

        Assert.Equal(
            new[] { "Base.esp", "FakeX.esp", "FakeY.esp" },
            order.Select(p => p.ModKey.FileName.ToString()).OrderBy(n => n).ToArray());
        Assert.Equal(3, order.Count);
    }

    [Fact]
    public void MissingMaster_WarnsOnce_AndIsSkipped()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "Stray.esp", "Ghost.esp", "Ghost.esp");
        var discovery = new PluginDiscovery().Discover(EsmDiscovery(dir), new List<ScanWarning>());
        var warnings = new List<ScanWarning>();

        var order = new LoadOrderBuilder(new ModLoader()).Build(discovery, warnings);

        var plugin = Assert.Single(order);
        Assert.Equal("Stray.esp", plugin.ModKey.FileName.ToString());
        var warning = Assert.Single(warnings);
        Assert.Contains("Ghost.esp", warning.Message);
    }

    [Fact]
    public void ResolutionOnlyPlugin_IsLinkedBeforeDependents_WithoutMissingMasterWarnings()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "Update.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "Skyrim.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "Dawnguard.esp", "Skyrim.esp", "Update.esp");
        var loader = new ModLoader();
        var discovery = new DiscoveryResult
        {
            DataPath = dir.Root,
            Plugins = new[]
            {
                new DiscoveredPlugin { AbsolutePath = dir.File("Skyrim.esp"), ModKey = ModKey.FromFileName("Skyrim.esp") },
                new DiscoveredPlugin { AbsolutePath = dir.File("Dawnguard.esp"), ModKey = ModKey.FromFileName("Dawnguard.esp") },
                new DiscoveredPlugin { AbsolutePath = dir.File("Update.esp"), ModKey = ModKey.FromFileName("Update.esp"), IsResolutionOnly = true },
            },
            MissingExplicitMasters = Array.Empty<ModKey>(),
        };
        var warnings = new List<ScanWarning>();

        var order = new LoadOrderBuilder(loader).Build(discovery, warnings);

        Assert.Empty(warnings);
        Assert.Equal(
            new[] { "Skyrim.esp", "Update.esp", "Dawnguard.esp" },
            order.Select(p => p.ModKey.FileName.ToString()).ToArray());
        Assert.True(order.Single(p => p.ModKey.FileName.ToString() == "Update.esp").IsResolutionOnly);
    }

    [Fact]
    public void StoryMain_EndsUpLast_AfterItsMasters()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "SlotA.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "SlotB.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "SlotC.esp");
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, "SlotMain.esp", "SlotA.esp", "SlotB.esp", "SlotC.esp");
        var discovery = new PluginDiscovery().Discover(EsmDiscovery(dir), new List<ScanWarning>());

        var order = new LoadOrderBuilder(new ModLoader()).Build(discovery, new List<ScanWarning>());

        Assert.Equal("SlotMain.esp", order[^1].ModKey.FileName.ToString());
        Assert.Equal(
            new[] { "SlotA.esp", "SlotB.esp", "SlotC.esp" },
            order.Take(3).Select(p => p.ModKey.FileName.ToString()).ToArray());
    }
}