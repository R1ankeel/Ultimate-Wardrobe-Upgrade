using System.Text.Json;
using System.Text.Json.Nodes;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Sprint 1.6 golden tests. The catalog snapshot lives under <c>tests/TestData/CatalogGolden/</c>
/// and the static reader-guard plugin under <c>tests/TestData/Plugins/</c>. Regenerate both
/// intentionally by running with <c>UW_WRITE_GOLDENS=1</c> and review the diff before committing.
/// </summary>
public sealed class CatalogGoldenTests : IDisposable
{
    private readonly TestTempDir _dir = new();
    private readonly ITestOutputHelper _output;

    public CatalogGoldenTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => _dir.Dispose();

    private const string NormalizedRootPath = "<root>";

    /// <summary>
    /// Serializes a catalog with the source <c>rootPath</c> replaced by a fixed placeholder so
    /// golden output is reproducible across machines, temp directories, and CI checkouts. The
    /// derived post-scan report is stripped - goldens capture the catalog data contract
    /// (source/sets/stats/warnings), not reporting derived from it.
    /// </summary>
    private static string Serialize(Catalog catalog)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(catalog, CatalogCacheStore.JsonOptions))!.AsObject();
        if (node["source"] is JsonObject source)
        {
            source["rootPath"] = NormalizedRootPath;
        }

        node.Remove("report");
        return node.ToJsonString();
    }

    private async Task<Catalog> ScanWrittenUniverse()
    {
        SyntheticSkyrimMods.WriteMiniUniverse(_dir.Root);
        var source = new VanillaCatalogSource(_dir.Root, new[] { SyntheticSkyrimMods.MiniUniverseFileName });
        return await new FolderCatalogScanner().ScanAsync(source);
    }

    [Fact]
    public async Task MiniUniverse_Scan_MatchesCommittedGolden()
    {
        var catalog = await ScanWrittenUniverse();
        var json = Serialize(catalog);

        if (CatalogGoldenData.ShouldWriteGoldens)
        {
            Directory.CreateDirectory(CatalogGoldenData.CatalogGoldenDirectory);
            Directory.CreateDirectory(CatalogGoldenData.PluginsDirectory);
            File.WriteAllText(CatalogGoldenData.MiniUniverseCatalog, json);
            SyntheticSkyrimMods.WriteMiniUniverse(CatalogGoldenData.PluginsDirectory);
            _output.WriteLine($"Goldens written. Rerun the suite WITHOUT UW_WRITE_GOLDENS to verify.");
            return;
        }

        var golden = File.ReadAllText(CatalogGoldenData.MiniUniverseCatalog);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(golden)),
            "Scan output diverged from the committed golden. To refresh intentionally, rerun with UW_WRITE_GOLDENS=1 and review the diff.");
    }

    [Fact]
    public async Task MiniUniverse_Scan_ProducesExpectedStats()
    {
        var catalog = await ScanWrittenUniverse();

        Assert.Equal(23, catalog.Stats.TotalArmo);
        Assert.Equal(22, catalog.Stats.TotalArma);
        Assert.Equal(12, catalog.Stats.GroupedSets);
        Assert.Equal(2, catalog.Stats.Skipped);
        Assert.Equal(
            new Dictionary<SkipReason, int> { [SkipReason.NoArmature] = 1, [SkipReason.CreatureRace] = 1 },
            catalog.Stats.SkippedByReason);
        Assert.Equal(40, catalog.Stats.MissingFiles);
    }

    [Fact]
    public async Task MiniUniverse_Scan_GroupsAllExpectedSets()
    {
        var catalog = await ScanWrittenUniverse();

        Assert.Equal(
            new[] { "aaminipauldron", "champion", "daedric", "elven", "femalecorset", "ironarmor", "leather", "malebulwark", "nightmaresuit", "nordiccarved", "orcish", "vampirerobes" },
            catalog.Sets.Select(s => s.Id));
    }

    [Fact]
    public async Task MiniUniverse_IronSet_IsOneFullKit()
    {
        var catalog = await ScanWrittenUniverse();

        var iron = Assert.Single(catalog.Sets, s => s.Id == "ironarmor");
        var male = Assert.Single(iron.Variants, v => v.Gender == Gender.Male);
        var female = Assert.Single(iron.Variants, v => v.Gender == Gender.Female);
        Assert.Equal(4, male.Pieces.Count);
        Assert.Equal(4, female.Pieces.Count);
        Assert.Equal(new[] { "IronBoots", "IronCuirass", "IronGauntlets", "IronHelmet" },
            female.Pieces.Select(p => p.EditorId).OrderBy(e => e));
    }

    [Fact]
    public async Task MiniUniverse_SplitMembership_JoinsIntoOneSet()
    {
        var catalog = await ScanWrittenUniverse();

        var nordic = Assert.Single(catalog.Sets, s => s.Id == "nordiccarved");
        Assert.Equal(4, nordic.Variants.Single(v => v.Gender == Gender.Male).Pieces.Count);
        Assert.Equal(new[] { "DLC2NordicCarvedBoots", "DLC2NordicCarvedCuirass", "DLC2NordicCarvedGauntlets", "DLC2NordicCarvedHelmet" },
            nordic.Variants.Single(v => v.Gender == Gender.Female).Pieces.Select(p => p.EditorId).OrderBy(e => e));
    }

    [Fact]
    public async Task MiniUniverse_GenderCovers_ProduceSingleGenderVariants()
    {
        var catalog = await ScanWrittenUniverse();

        var corset = Assert.Single(catalog.Sets, s => s.Id == "femalecorset");
        var bulwark = Assert.Single(catalog.Sets, s => s.Id == "malebulwark");

        var corsetVariant = Assert.Single(corset.Variants);
        Assert.Equal(Gender.Female, corsetVariant.Gender);

        var bulwarkVariant = Assert.Single(bulwark.Variants);
        Assert.Equal(Gender.Male, bulwarkVariant.Gender);
    }

    [Fact]
    public async Task MiniUniverse_WeirdEditorIds_NormalizeToCleanSets()
    {
        var catalog = await ScanWrittenUniverse();

        Assert.Contains(catalog.Sets, s => s.Id == "nightmaresuit");
        Assert.Contains(catalog.Sets, s => s.Id == "daedric");
        Assert.Contains(catalog.Sets, s => s.Id == "champion");
        Assert.Contains(catalog.Sets, s => s.Id == "elven");
        Assert.Contains(catalog.Sets, s => s.Id == "orcish");
    }

    [Fact]
    public async Task MiniUniverse_Scan_IsByteDeterministic()
    {
        var a = await ScanWrittenUniverse();
        var b = await ScanWrittenUniverse();

        Assert.Equal(Serialize(a), Serialize(b));
        Assert.Equal(Serialize(a).Length, Serialize(b).Length);
    }

    [Fact]
    public void StaticPlugin_GuardsTheReader_AcrossRefactors()
    {
        Assert.True(File.Exists(CatalogGoldenData.MiniUniversePlugin),
            $"Committed golden plugin missing at '{CatalogGoldenData.MiniUniversePlugin}'. Generate with UW_WRITE_GOLDENS=1.");

        var warnings = new List<ScanWarning>();
        using var loaded = new ModLoader().TryLoad(CatalogGoldenData.MiniUniversePlugin, warnings);
        Assert.NotNull(loaded);

        var index = RecordIndex.Build(new[] { loaded! }, warnings);
        Assert.Equal(23, index.ArmorCount);
        Assert.Equal(22, index.ArmorAddonCount);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task StaticPlugin_Scan_MatchesCommittedGolden()
    {
        Assert.True(File.Exists(CatalogGoldenData.MiniUniversePlugin), "Committed golden plugin missing; see UW_WRITE_GOLDENS=1.");

        var source = new VanillaCatalogSource(CatalogGoldenData.PluginsDirectory, new[] { SyntheticSkyrimMods.MiniUniverseFileName });
        var catalog = await new FolderCatalogScanner().ScanAsync(source);

        var golden = File.ReadAllText(CatalogGoldenData.MiniUniverseCatalog);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Serialize(catalog)), JsonNode.Parse(golden)),
            "Scan of the committed golden plugin diverged from the golden catalog.");
    }
}