using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class RecordIndexTests
{
    private static RecordIndex BuildPair(TestTempDir dir, out List<ScanWarning> warnings)
    {
        SyntheticSkyrimMods.WriteMaster(dir.Root);
        SyntheticSkyrimMods.WriteMain(dir.Root);

        var filePaths = new[] { dir.File(SyntheticSkyrimMods.MasterFileName), dir.File(SyntheticSkyrimMods.MainFileName) };
        warnings = new List<ScanWarning>();
        return Build(dir, filePaths, warnings);
    }

    private static RecordIndex Build(TestTempDir dir, IReadOnlyList<string> absolutePaths, List<ScanWarning> warnings)
    {
        var loader = new ModLoader();
        using var mods = new DisposableList<LoadedMod>();
        foreach (var absolutePath in absolutePaths)
        {
            mods.Add(loader.TryLoad(absolutePath, warnings));
        }

        return RecordIndex.Build(mods.Items, warnings);
    }

    [Fact]
    public void ResolutionOnlyMod_ContributesNoArmorContent_ButItsResolutionRecordsStay()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMaster(dir.Root);
        SyntheticSkyrimMods.WriteMain(dir.Root);
        var warnings = new List<ScanWarning>();
        var loader = new ModLoader();
        using var mods = new DisposableList<LoadedMod>();
        mods.Add(loader.TryLoad(dir.File(SyntheticSkyrimMods.MasterFileName), warnings));
        mods.Add(loader.TryLoad(dir.File(SyntheticSkyrimMods.MainFileName), isResolutionOnly: true, warnings));

        var index = RecordIndex.Build(mods.Items, warnings);

        Assert.Equal(1, index.ArmorCount);
        Assert.Equal(0, index.ArmorAddonCount);
        Assert.False(index.TryResolveArmor(SyntheticSkyrimMods.NewArmorKey, out _));
        Assert.True(index.TryResolveArmor(SyntheticSkyrimMods.OverrideArmorKey, out var armor));
        Assert.Equal("Base Armor", armor.Name?.String);
        Assert.True(index.TryResolveKeyword(SyntheticSkyrimMods.MainHeavyKeywordKey, out _));
        Assert.Equal(2, index.KeywordCount);
    }

    [Fact]
    public void LaterPlugin_OverridesEarlierRecord_ForSameFormId()
    {
        using var dir = new TestTempDir();
        var index = BuildPair(dir, out _);

        Assert.True(index.TryResolveArmor(SyntheticSkyrimMods.OverrideArmorKey, out var armor));
        Assert.Equal("Patched Armor", armor.Name?.String);
    }

    [Fact]
    public void ResolvesRecordsAcrossAllPlugins()
    {
        using var dir = new TestTempDir();
        var index = BuildPair(dir, out _);

        Assert.True(index.TryResolveArmor(SyntheticSkyrimMods.NewArmorKey, out var helm));
        Assert.Equal("NewHelm", helm.EditorID);

        Assert.True(index.TryResolveArmorAddon(SyntheticSkyrimMods.MainArmorAddonKey, out var addon));
        Assert.Equal("BaseArmorAA", addon.EditorID);

        Assert.Equal(4, index.ArmorCount);
        Assert.Equal(2, index.ArmorAddonCount);
    }

    [Fact]
    public void KeywordCache_OnlyKeepsWeightKeywords()
    {
        using var dir = new TestTempDir();
        var index = BuildPair(dir, out _);

        Assert.True(index.TryResolveKeyword(SyntheticSkyrimMods.WeightKeywordKey, out var keyword));
        Assert.Equal("ArmorHeavy", keyword.EditorID);
        Assert.True(index.TryResolveKeyword(SyntheticSkyrimMods.MainHeavyKeywordKey, out _));
        Assert.Equal(2, index.KeywordCount);

        Assert.False(index.TryResolveKeyword(SyntheticSkyrimMods.NonWeightKeywordKey, out _));
    }

    [Fact]
    public void TextureSetCache_Resolves_OnlyReferencedSets()
    {
        using var dir = new TestTempDir();
        var index = BuildPair(dir, out _);

        Assert.True(index.TryResolveTextureSet(SyntheticSkyrimMods.MasterTextureSetKey, out var textureSet));
        Assert.Equal("txSetMaster", textureSet.EditorID);

        Assert.True(index.TryResolveTextureSet(SyntheticSkyrimMods.MeshTextureSetKey, out var meshSet));
        Assert.Equal("txSetIron", meshSet.EditorID);

        Assert.Equal(2, index.TextureSetCount);
    }

    [Fact]
    public void CorruptPluginInTheMiddle_DoesNotAbortIndex_AndOverridesStillWin()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMaster(dir.Root);
        SyntheticSkyrimMods.WriteCorruptPlugin(dir.Root);
        SyntheticSkyrimMods.WriteMain(dir.Root);

        var filePaths = new[]
        {
            dir.File(SyntheticSkyrimMods.MasterFileName),
            dir.File("Broken.esp"),
            dir.File(SyntheticSkyrimMods.MainFileName),
        };
        var warnings = new List<ScanWarning>();
        var index = Build(dir, filePaths, warnings);

        Assert.True(index.TryResolveArmor(SyntheticSkyrimMods.OverrideArmorKey, out var armor));
        Assert.Equal("Patched Armor", armor.Name?.String);
        Assert.Contains(warnings, w => w.Message.Contains("Broken.esp"));
    }

    [Fact]
    public void UnlinkedTextureSet_IsNotCached()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMaster(dir.Root);
        SyntheticSkyrimMods.WriteChainPlugin(dir.Root, SyntheticSkyrimMods.MainFileName);
        var warnings = new List<ScanWarning>();
        var index = Build(dir, new[] { dir.File(SyntheticSkyrimMods.MasterFileName), dir.File(SyntheticSkyrimMods.MainFileName) }, warnings);

        var unlinked = SyntheticSkyrimMods.MasterTextureSetKey;
        Assert.False(index.TryResolveTextureSet(unlinked, out _));
        Assert.Equal(0, index.TextureSetCount);
    }

    private sealed class DisposableList<T> : IDisposable where T : class, IDisposable
    {
        public IReadOnlyList<T> Items => _items;

        private readonly List<T> _items = new();

        public void Add(T? item)
        {
            if (item is not null)
            {
                _items.Add(item);
            }
        }

        public void Dispose()
        {
            foreach (var item in _items)
            {
                item.Dispose();
            }
        }
    }
}