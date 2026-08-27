using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class ModLoaderTests
{
    [Fact]
    public void TryLoad_ReturnsLoadedMod_ForValidPlugin()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMaster(dir.Root);

        using var loaded = new ModLoader().TryLoad(dir.File(SyntheticSkyrimMods.MasterFileName), new List<ScanWarning>());

        Assert.NotNull(loaded);
        Assert.Equal(SyntheticSkyrimMods.MasterKey, loaded.ModKey);
        Assert.Equal(dir.File(SyntheticSkyrimMods.MasterFileName), loaded.AbsolutePath);
    }

    [Fact]
    public void ReadMasters_ReturnsHeaderMasters_OfPlugin()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMaster(dir.Root);
        SyntheticSkyrimMods.WriteMain(dir.Root);

        var masters = new ModLoader().ReadMasters(dir.File(SyntheticSkyrimMods.MainFileName));

        var master = Assert.Single(masters);
        Assert.Equal(SyntheticSkyrimMods.MasterKey, master);
    }

    [Fact]
    public void TryLoad_CorruptPlugin_ReturnsNull_AndWarns()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteCorruptPlugin(dir.Root);
        var warnings = new List<ScanWarning>();

        var loaded = new ModLoader().TryLoad(dir.File("Broken.esp"), warnings);

        Assert.Null(loaded);
        var warning = Assert.Single(warnings);
        Assert.Contains("Broken.esp", warning.Message);
        Assert.Contains("skipped", warning.Message);
    }
}