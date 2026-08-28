using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Patcher;
using UltimateWardrobe.Scanner;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.Patcher;

/// <summary>
/// Sprint 5.0.4 - <see cref="TargetResolver"/> unit tests over the Phase 1 pipeline. The synthetic
/// mini universe (<see cref="SyntheticSkyrimMods.WriteMiniUniverse"/>) already carries gender-split
/// <see cref="Model"/> fixtures (Both/Female/Male), so no fixture extension was needed. Covers:
/// EditorId-primary resolution, FormId fallback, ARMA by <c>ArmaEditorId</c> then first armature
/// addon, gendered model extraction, resolution over a real scanned catalog, and the negative paths
/// (missing catalog -> typed <see cref="PatchException"/>; unknown target, unresolvable ARMA, corrupt
/// plugin, missing game folder -> skip/warning or typed failure, no crash).
/// </summary>
public sealed class TargetResolverTests
{
    [Fact]
    public void Resolve_ByEditorId_ResolvesArmorAddonAndGenderModels()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateMiniCatalog(dir.Root));
        overhaul.Mappings.Add(CreateMapping(overhaul, "IronArmor", "IronCuirass", Gender.Male));

        var result = new TargetResolver().Resolve(overhaul);

        var target = Assert.Single(result.Targets);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassKey, target.ArmorKey);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassAddonKey, target.ArmorAddonKey);
        Assert.Equal(Gender.Male, target.Gender);
        Assert.Equal("meshes/armor/mini/IronCuirass/male.nif", target.CurrentModelMalePath);
        Assert.Equal("meshes/armor/mini/IronCuirass/female.nif", target.CurrentModelFemalePath);
        Assert.Empty(result.Warnings);

        var second = new TargetResolver().Resolve(overhaul);
        Assert.Equal(
            second.Targets.Select(t => t.ArmorAddonKey),
            result.Targets.Select(t => t.ArmorAddonKey));
    }

    [Fact]
    public void Resolve_ByEditorIdFallback_ResolvesByFormId()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateMiniCatalog(dir.Root, ironEditorId: "RenamedCuirass"));
        overhaul.Mappings.Add(CreateMapping(overhaul, "IronArmor", "RenamedCuirass", Gender.Male));

        var result = new TargetResolver().Resolve(overhaul);

        var target = Assert.Single(result.Targets);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassKey, target.ArmorKey);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassAddonKey, target.ArmorAddonKey);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Resolve_ArmaEditorIdMissing_FallsBackToFirstArmatureAddon()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateMiniCatalog(dir.Root, ironArmaEditorId: "GhostAddonAA"));
        overhaul.Mappings.Add(CreateMapping(overhaul, "IronArmor", "IronCuirass", Gender.Male));

        var result = new TargetResolver().Resolve(overhaul);

        var target = Assert.Single(result.Targets);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassAddonKey, target.ArmorAddonKey);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Resolve_GenderSpecificModels_ExtractOnlyThePresentSide()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateMiniCatalog(dir.Root));
        overhaul.Mappings.Add(CreateMapping(overhaul, "Clothes", "FemaleCorset", Gender.Female));
        overhaul.Mappings.Add(CreateMapping(overhaul, "Clothes", "MaleBulwark", Gender.Male));

        var result = new TargetResolver().Resolve(overhaul);

        Assert.Equal(2, result.Targets.Count);
        var corset = Assert.Single(result.Targets, t => t.Mapping.TargetPieceEditorId == "FemaleCorset");
        Assert.Equal("meshes/armor/mini/FemaleCorset/female.nif", corset.CurrentModelFemalePath);
        Assert.Null(corset.CurrentModelMalePath);

        var bulwark = Assert.Single(result.Targets, t => t.Mapping.TargetPieceEditorId == "MaleBulwark");
        Assert.Equal("meshes/armor/mini/MaleBulwark/male.nif", bulwark.CurrentModelMalePath);
        Assert.Null(bulwark.CurrentModelFemalePath);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Resolve_OverScannedCatalog_MatchesTheScanPipeline()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var source = new VanillaCatalogSource(dir.Root, new[] { SyntheticSkyrimMods.MiniUniverseFileName });
        var catalog = await new FolderCatalogScanner().ScanAsync(source);

        var (setId, gender, piece) = catalog.Sets
            .SelectMany(s => s.Variants.Select(v => (SetId: s.Id, Gender: v.Gender, Piece: v.Pieces.FirstOrDefault(p => p.EditorId == "IronCuirass"))))
            .First(t => t.Piece is not null);

        var overhaul = CreateOverhaul(catalog);
        overhaul.Mappings.Add(CreateMapping(overhaul, setId, piece!.EditorId, gender));

        var result = new TargetResolver().Resolve(overhaul);

        var target = Assert.Single(result.Targets);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassKey, target.ArmorKey);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassAddonKey, target.ArmorAddonKey);
    }

    [Fact]
    public void Resolve_UnknownTarget_SkipsWithWarningAndResolvesOthers()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateMiniCatalog(dir.Root));
        overhaul.Mappings.Add(CreateMapping(overhaul, "IronArmor", "GhostArmor", Gender.Male));
        overhaul.Mappings.Add(CreateMapping(overhaul, "IronArmor", "IronCuirass", Gender.Male));

        var result = new TargetResolver().Resolve(overhaul);

        var target = Assert.Single(result.Targets);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassAddonKey, target.ArmorAddonKey);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("GhostArmor", warning.Context);
        Assert.Contains("skipped", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UnresolvableArma_SkipsWithWarning()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var catalog = CreateMiniCatalog(dir.Root, dummyArmaEditorId: "GhostAddonAA");
        var overhaul = CreateOverhaul(catalog);
        overhaul.Mappings.Add(CreateMapping(overhaul, "Clothes", "DummyMannequin", Gender.Male));

        var result = new TargetResolver().Resolve(overhaul);

        Assert.Empty(result.Targets);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("DummyMannequin", warning.Context);
        Assert.Contains("skipped", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_CorruptSourcePlugin_SkipsWithoutCrash()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteCorruptPlugin(dir.Root, "CorruptTarget.esp");
        var source = new VanillaCatalogSource(dir.Root, new[] { "CorruptTarget.esp" });
        var catalog = new Catalog(source, new[]
        {
            new ArmorSet("IronArmor", "Iron", new[]
            {
                new Variant(Gender.Male, WeightClass.Heavy, new[]
                {
                    new Piece("IronCuirass", SyntheticSkyrimMods.MiniIronCuirassKey.ID, "32 Body", "IronCuirassAA"),
                }),
            }),
        });
        var overhaul = CreateOverhaul(catalog);
        overhaul.Mappings.Add(CreateMapping(overhaul, "IronArmor", "IronCuirass", Gender.Male));

        var result = new TargetResolver().Resolve(overhaul);

        Assert.Empty(result.Targets);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Resolve_CatalogMissing_ThrowsPatchException()
    {
        var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
        var overhaul = new Overhaul(Guid.NewGuid(), "NoCatalog", project.Id, new VanillaCatalogSource("D:/Skymod/Stock Game"));

        var ex = Assert.Throws<PatchException>(() => new TargetResolver().Resolve(overhaul));

        Assert.Contains("catalog", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_MissingGameFolder_ThrowsPatchException()
    {
        var source = new VanillaCatalogSource("C:/DoesNotExist_UW_Sprint50");
        var catalog = new Catalog(source, Array.Empty<ArmorSet>());
        var overhaul = CreateOverhaul(catalog);
        overhaul.Mappings.Add(CreateMapping(overhaul, "IronArmor", "IronCuirass", Gender.Male));

        var ex = Assert.Throws<PatchException>(() => new TargetResolver().Resolve(overhaul));

        Assert.Contains("Could not load the source for patching", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Overhaul CreateOverhaul(Catalog catalog)
    {
        var project = new Project(Guid.NewGuid(), "ResolverProject", "C:/Projects/Test");
        return new Overhaul(Guid.NewGuid(), "MiniOverhaul", project.Id, catalog.Source) { Catalog = catalog };
    }

    private static PieceMapping CreateMapping(Overhaul overhaul, string setId, string pieceEditorId, Gender gender)
    {
        return new PieceMapping(
            Guid.NewGuid(),
            overhaul.Id,
            setId,
            pieceEditorId,
            gender,
            Guid.NewGuid(),
            "DonorPiece",
            "armor/donor.nif");
    }

    private static Catalog CreateMiniCatalog(
        string root,
        string ironArmaEditorId = "IronCuirassAA",
        string ironEditorId = "IronCuirass",
        string dummyArmaEditorId = "DummyAA")
    {
        var maleIron = new Variant(Gender.Male, WeightClass.Heavy, new[]
        {
            new Piece(ironEditorId, SyntheticSkyrimMods.MiniIronCuirassKey.ID, "32 Body", ironArmaEditorId, "meshes/armor/mini/IronCuirass/male.nif"),
        });
        var femaleIron = new Variant(Gender.Female, WeightClass.Heavy, new[]
        {
            new Piece("IronCuirass", SyntheticSkyrimMods.MiniIronCuirassKey.ID, "32 Body", "IronCuirassAA", "meshes/armor/mini/IronCuirass/female.nif"),
        });
        var femaleClothes = new Variant(Gender.Female, WeightClass.Clothing, new[]
        {
            new Piece("FemaleCorset", SyntheticSkyrimMods.MiniFemaleCorsetKey.ID, "32 Body", "FemaleCorsetAA", "meshes/armor/mini/FemaleCorset/female.nif"),
        });
        var maleClothes = new Variant(Gender.Male, WeightClass.Clothing, new[]
        {
            new Piece("MaleBulwark", SyntheticSkyrimMods.MiniMaleBulwarkKey.ID, "32 Body", "MaleBulwarkAA", "meshes/armor/mini/MaleBulwark/male.nif"),
            new Piece("DummyMannequin", SyntheticSkyrimMods.MiniDummyKey.ID, "32 Body", dummyArmaEditorId),
        });

        return new Catalog(
            new VanillaCatalogSource(root, new[] { SyntheticSkyrimMods.MiniUniverseFileName }),
            new[]
            {
                new ArmorSet("IronArmor", "Iron", new[] { maleIron, femaleIron }),
                new ArmorSet("Clothes", "Clothes", new[] { femaleClothes, maleClothes }),
            });
    }
}