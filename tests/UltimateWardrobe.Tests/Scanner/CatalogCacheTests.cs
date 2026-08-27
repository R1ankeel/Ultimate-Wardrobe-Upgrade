using System.Text.Json;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class CatalogCacheTests
{
    private static VanillaCatalogSource VanillaUniverseSource(TestTempDir dir)
    {
        return new VanillaCatalogSource(dir.Root, new[] { SyntheticGroupingUniverse.FileName });
    }

    private static async Task<Catalog> ScanUniverse(TestTempDir dir)
    {
        SyntheticGroupingUniverse.Write(dir.Root);
        return await new FolderCatalogScanner().ScanAsync(VanillaUniverseSource(dir));
    }

    [Fact]
    public async Task SaveLoad_RoundTrip_IsValueIdentical()
    {
        using var dir = new TestTempDir();
        var catalog = await ScanUniverse(dir);
        var store = new CatalogCacheStore();
        var probe = store.BuildProbe(VanillaUniverseSource(dir));
        var path = dir.File("cache.json");

        store.Save(path, catalog, probe);
        var loaded = store.TryLoad(path);

        Assert.NotNull(loaded);
        var options = CatalogCacheStore.JsonOptions;
        Assert.Equal(JsonSerializer.Serialize(catalog, options), JsonSerializer.Serialize(loaded, options));
    }

    [Fact]
    public async Task SaveLoad_VanillaSource_RoundTripsPluginNames()
    {
        using var dir = new TestTempDir();
        var catalog = await ScanUniverse(dir);
        var store = new CatalogCacheStore();
        var path = dir.File("cache.json");

        store.Save(path, catalog, store.BuildProbe(VanillaUniverseSource(dir)));
        var loaded = store.TryLoad(path);

        Assert.NotNull(loaded);
        Assert.Equal(CatalogSourceKind.VanillaPlusDlc, loaded!.Source.Kind);
        var source = Assert.IsType<VanillaCatalogSource>(loaded.Source);
        Assert.Equal(SyntheticGroupingUniverse.FileName, Assert.Single(source.PluginNames));
        Assert.Equal(6, loaded.Sets.Count);
        Assert.Equal(17, loaded.Stats.TotalArmo);
    }

    [Fact]
    public async Task SaveLoad_StorySource_RoundTripsMainPluginAndMasters()
    {
        using var dir = new TestTempDir();
        SyntheticGroupingUniverse.Write(dir.Root);
        var scanner = new FolderCatalogScanner();
        var source = new StoryModCatalogSource(dir.Root, SyntheticGroupingUniverse.FileName, new[] { "Skyrim.esm" });
        var catalog = await scanner.ScanAsync(source);
        var store = new CatalogCacheStore();
        var path = dir.File("cache.json");

        store.Save(path, catalog, store.BuildProbe(source));
        var loaded = store.TryLoad(path);

        Assert.NotNull(loaded);
        var roundTripped = Assert.IsType<StoryModCatalogSource>(loaded!.Source);
        Assert.Equal(SyntheticGroupingUniverse.FileName, roundTripped.MainPlugin);
        Assert.Equal("Skyrim.esm", Assert.Single(roundTripped.Masters));
    }

    [Fact]
    public async Task Save_Canonical_IdenticalBytesForSameInput()
    {
        using var dir = new TestTempDir();
        var catalog = await ScanUniverse(dir);
        var store = new CatalogCacheStore();
        var probe = store.BuildProbe(VanillaUniverseSource(dir));

        var pathA = dir.File("a.json");
        var pathB = dir.File("b.json");
        store.Save(pathA, catalog, probe);
        store.Save(pathB, catalog, probe);

        Assert.Equal(File.ReadAllBytes(pathA), File.ReadAllBytes(pathB));
    }

    [Fact]
    public async Task IsFresh_True_WhenSourceUnchanged()
    {
        using var dir = new TestTempDir();
        var catalog = await ScanUniverse(dir);
        var store = new CatalogCacheStore();
        var source = VanillaUniverseSource(dir);
        var path = dir.File("cache.json");

        store.Save(path, catalog, store.BuildProbe(source));

        Assert.True(store.IsFresh(path, source));
    }

    [Fact]
    public async Task IsFresh_False_WhenPluginModified()
    {
        using var dir = new TestTempDir();
        var catalog = await ScanUniverse(dir);
        var store = new CatalogCacheStore();
        var source = VanillaUniverseSource(dir);
        var path = dir.File("cache.json");

        store.Save(path, catalog, store.BuildProbe(source));

        await File.AppendAllTextAsync(dir.File(SyntheticGroupingUniverse.FileName), "x");

        Assert.False(store.IsFresh(path, source));
    }

    [Fact]
    public async Task IsFresh_False_WhenPluginDeleted()
    {
        using var dir = new TestTempDir();
        var catalog = await ScanUniverse(dir);
        var store = new CatalogCacheStore();
        var source = VanillaUniverseSource(dir);
        var path = dir.File("cache.json");

        store.Save(path, catalog, store.BuildProbe(source));

        File.Delete(dir.File(SyntheticGroupingUniverse.FileName));

        Assert.False(store.IsFresh(path, source));
    }

    [Fact]
    public void IsFresh_False_WhenCacheFileMissing()
    {
        using var dir = new TestTempDir();
        var store = new CatalogCacheStore();
        var source = new VanillaCatalogSource(dir.Root, new[] { SyntheticGroupingUniverse.FileName });

        Assert.False(store.IsFresh(dir.File("no-such-cache.json"), source));
    }

    [Fact]
    public void TryLoad_Null_WhenFileMissing()
    {
        using var dir = new TestTempDir();
        var store = new CatalogCacheStore();

        Assert.Null(store.TryLoad(dir.File("no-such-cache.json")));
    }

    [Fact]
    public async Task TryLoad_Null_WhenFileCorrupt()
    {
        using var dir = new TestTempDir();
        var store = new CatalogCacheStore();
        var path = dir.File("cache.json");
        await File.WriteAllTextAsync(path, "{ not json !!");

        Assert.Null(store.TryLoad(path));
        Assert.False(store.IsFresh(path, VanillaUniverseSource(dir)));
    }
}