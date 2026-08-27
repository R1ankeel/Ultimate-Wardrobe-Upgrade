using System.Linq;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class ArmorCorrelatorTests
{
    private static RecordIndex BuildIndex(TestTempDir dir, out List<ScanWarning> warnings)
    {
        SyntheticSkyrimMods.WriteMaster(dir.Root);
        SyntheticSkyrimMods.WriteMain(dir.Root);
        warnings = new List<ScanWarning>();

        var loader = new ModLoader();
        var mods = new List<LoadedMod>();
        foreach (var path in new[] { dir.File(SyntheticSkyrimMods.MasterFileName), dir.File(SyntheticSkyrimMods.MainFileName) })
        {
            var loaded = loader.TryLoad(path, warnings);
            if (loaded is not null) mods.Add(loaded);
        }

        try
        {
            return RecordIndex.Build(mods, warnings);
        }
        finally
        {
            foreach (var m in mods) m.Dispose();
        }
    }

    private static CorrelatedArmor CorrelateForm(RecordIndex index, Mutagen.Bethesda.Plugins.FormKey key, List<ScanWarning> warnings)
    {
        Assert.True(index.TryResolveArmor(key, out var armor));
        return new ArmorCorrelator().CorrelateOne(armor, index, warnings);
    }

    [Fact]
    public void MeshArmor_ResolvesMeshPath_AndArmaEditorId()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out var warnings);

        var correlated = CorrelateForm(index, SyntheticSkyrimMods.MeshArmorKey, warnings);

        Assert.Equal("IronCuirass", correlated.EditorId);
        Assert.Equal(SyntheticSkyrimMods.MeshArmorKey.ID, correlated.FormId);
        Assert.Equal("IronCuirassAA", correlated.ArmaEditorId);
        Assert.Equal(SyntheticSkyrimMods.MeshPath, correlated.MeshPath);
        Assert.Empty(warnings);
    }

    [Fact]
    public void MeshArmor_ResolvesTexturePaths_FromTxst_DedupedAndOrdinalSorted()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);

        var correlated = CorrelateForm(index, SyntheticSkyrimMods.MeshArmorKey, new List<ScanWarning>());

        Assert.Equal(
            new[]
            {
                SyntheticSkyrimMods.MeshDiffusePath,
                SyntheticSkyrimMods.MeshNormalPath,
                SyntheticSkyrimMods.MeshGlowPath,
            },
            correlated.TexturePaths);
    }

    [Fact]
    public void UnresolvableArmature_WarnsForAffectedArmor_AndKeepsScanning()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out var warnings);

        var correlated = CorrelateForm(index, SyntheticSkyrimMods.DanglingArmorKey, warnings);

        Assert.Equal("DanglingGauntlets", correlated.EditorId);
        Assert.Null(correlated.ArmaEditorId);
        Assert.Null(correlated.MeshPath);
        Assert.Empty(correlated.TexturePaths);

        var warning = Assert.Single(warnings);
        Assert.Contains("DanglingGauntlets", warning.Message);
        Assert.Contains("could not be resolved", warning.Message);
        Assert.Equal("DanglingGauntlets", warning.EditorId);
    }

    [Fact]
    public void BulkCorrelate_ProcessesEveryArmor_WithoutThrowing()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out var warnings);

        var results = new ArmorCorrelator().Correlate(index, warnings);

        Assert.Equal(index.ArmorCount, results.Count);
        Assert.Contains(results, r => r.EditorId == "IronCuirass");
    }
}
