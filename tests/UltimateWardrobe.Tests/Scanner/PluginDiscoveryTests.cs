using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class PluginDiscoveryTests
{
    [Fact]
    public void Vanilla_ResolvesDataFolder_WhenItExists()
    {
        using var dir = new TestTempDir();
        Directory.CreateDirectory(dir.File("Data"));
        File.WriteAllText(dir.File("Data\\FakeA.esm"), "x");
        File.WriteAllText(dir.File("Data\\FakeB.esl"), "x");

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), new List<ScanWarning>());

        Assert.Equal(dir.File("Data"), discovery.DataPath);
        Assert.Equal(new[] { "FakeA.esm", "FakeB.esl" }, discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
        Assert.All(discovery.Plugins, p => Assert.False(p.IsMainPlugin));
    }

    [Fact]
    public void Vanilla_GlobsEsmAndEsl_AndIgnoresOtherFiles()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("Skyrim.esm"), "x");
        File.WriteAllText(dir.File("Update.esl"), "x");
        File.WriteAllText(dir.File("notes.txt"), "x");

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), new List<ScanWarning>());

        Assert.Equal(new[] { "Skyrim.esm", "Update.esl" }, discovery.Plugins.Select(p => p.ModKey.FileName.ToString()).ToArray());
    }

    [Fact]
    public void Vanilla_FallsBackToRoot_WhenNoDataFolderButEsmsPresent()
    {
        using var dir = new TestTempDir();
        File.WriteAllText(dir.File("OnlyRomance.esm"), "x");

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), new List<ScanWarning>());

        Assert.Equal(dir.Root, discovery.DataPath);
        Assert.Single(discovery.Plugins);
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
    public void Vanilla_EmptyDataFolder_YieldsNoPlugins_WithoutThrowing()
    {
        using var dir = new TestTempDir();
        Directory.CreateDirectory(dir.File("Data"));

        var discovery = new PluginDiscovery().Discover(new VanillaCatalogSource(dir.Root), new List<ScanWarning>());

        Assert.Empty(discovery.Plugins);
    }
}