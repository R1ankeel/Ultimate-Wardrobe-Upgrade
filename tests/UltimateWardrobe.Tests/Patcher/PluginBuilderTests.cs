using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Patcher;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.Patcher;

/// <summary>
/// Sprint 5.1.5 - <see cref="PluginBuilder"/> unit tests over the synthetic mini universe. Builds the
/// output esp against the resolved targets, re-opens it with Mutagen and asserts: the override
/// <see cref="IPatcher"/> FormKey equals the target ARMA key, the gender-specific
/// <see cref="WorldModel"/> File equals the donor mesh path, null slots are created, the
/// <c>UW_</c> EditorID prefix, auto-collected masters contain the source key, the ESL flag is present
/// for a Vanilla source and absent for a StoryMod source, amendment #8 patch-shadowing selects the
/// patch mesh, and the amendment #6 loose-path skip writes no record - including the case-mixing +
/// backslash-vs-forward-slash normalization variant.
/// </summary>
public sealed class PluginBuilderTests
{
    // ---------------------------------------------------------------------------------------------
    // Core override semantics (5.1.1 / 5.1.3 / 5.1.4)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Build_OverridesTargetArma_WritesGenderSlot_PrefixesUw_AndEslForVanilla()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateIronCatalog(dir.Root));

        var donorId = Guid.NewGuid();
        var bodyPatchId = Guid.NewGuid();
        var library = CreateLibrary(
            CreateDonor(donorId),
            CreateDonor(bodyPatchId, new[] { "Data/Meshes/Donor/Iron/Cuirass.nif" }, DonorAssetKind.BodyConversionPatch));

        overhaul.Mappings.Add(CreateMapping(
            overhaul,
            donorId,
            "meshes/donor/iron/cuirass.nif",
            bodyConversionPatchId: bodyPatchId));

        var resolution = new TargetResolver().Resolve(overhaul);
        var plate = new PluginBuilder().Build(overhaul, resolution.Targets, library, Path.Combine(dir.Root, "Export"));

        Assert.Equal(Path.Combine(dir.Root, "Export", "UltimateWardrobe - TestOverhaul.esp"), plate.PluginPath);
        Assert.True(File.Exists(plate.PluginPath));
        Assert.Equal(1, plate.Report!.OverriddenRecords);
        Assert.Empty(plate.Report.Warnings);

        using var reopened = SkyrimMod.CreateFromBinaryOverlay(plate.PluginPath, SkyrimRelease.SkyrimSE);

        var arma = reopened.ArmorAddons[SyntheticSkyrimMods.MiniIronCuirassAddonKey];
        Assert.NotNull(arma);
        Assert.Equal(SyntheticSkyrimMods.MiniIronCuirassAddonKey, arma.FormKey);
        Assert.Equal("UW_IronCuirassAA", arma.EditorID);
        Assert.Equal("meshes/donor/iron/cuirass.nif", arma.WorldModel!.Male!.File!.GivenPath);
        Assert.Equal("meshes/armor/mini/IronCuirass/female.nif", arma.WorldModel.Female!.File!.GivenPath);
        Assert.Contains(SyntheticSkyrimMods.MiniUniverseKey, reopened.MasterReferences.Select(m => m.Master));
        Assert.True(reopened.IsSmallMaster);
    }

    [Fact]
    public void Build_StoryModSource_NoEslFlag_ButOverrideAndMasters()
    {
        using var dir = new TestTempDir();
        WriteStoryTarget(dir.Root);
        var overhaul = CreateOverhaul(CreateStoryCatalog(dir.Root));

        var donorId = Guid.NewGuid();
        var library = CreateLibrary(CreateDonor(donorId));
        overhaul.Mappings.Add(CreateMapping(overhaul, donorId, "meshes/donor/story/male.nif", story: true));

        var resolution = new TargetResolver().Resolve(overhaul);
        var plate = new PluginBuilder().Build(overhaul, resolution.Targets, library, Path.Combine(dir.Root, "Export"));

        using var reopened = SkyrimMod.CreateFromBinaryOverlay(plate.PluginPath, SkyrimRelease.SkyrimSE);

        Assert.False(reopened.IsSmallMaster);
        var arma = reopened.ArmorAddons[StoryTargetAddonKey];
        Assert.NotNull(arma);
        Assert.Equal("UW_StoryArmorAA", arma.EditorID);
        Assert.Equal("meshes/donor/story/male.nif", arma.WorldModel!.Male!.File!.GivenPath);
        Assert.Contains(StoryTargetKey, reopened.MasterReferences.Select(m => m.Master));
    }

    [Fact]
    public void Build_WorldModelNonExistingGenderSlot_IsCreatedNotDereferenced()
    {
        using var dir = new TestTempDir();
        WriteConverterTarget(dir.Root);
        var overhaul = CreateOverhaul(CreateConverterCatalog(dir.Root));

        var donorId = Guid.NewGuid();
        var library = CreateLibrary(CreateDonor(donorId));
        overhaul.Mappings.Add(CreateMapping(overhaul, donorId, "meshes/donor/shadow/male.nif", setId: "ShadowSet", pieceEditorId: "ShadowPlate"));

        var resolution = new TargetResolver().Resolve(overhaul);
        var plate = new PluginBuilder().Build(overhaul, resolution.Targets, library, Path.Combine(dir.Root, "Export"));

        Assert.Equal(1, plate.Report!.OverriddenRecords);

        using var reopened = SkyrimMod.CreateFromBinaryOverlay(plate.PluginPath, SkyrimRelease.SkyrimSE);

        var arma = reopened.ArmorAddons[ConverterTargetAddonKey];
        Assert.NotNull(arma);
        Assert.Equal("UW_ShadowPlateAA", arma.EditorID);
        Assert.Equal("meshes/donor/shadow/male.nif", arma.WorldModel!.Male!.File!.GivenPath);
        Assert.Equal("meshes/armor/shadow/female.nif", arma.WorldModel.Female!.File!.GivenPath);
        Assert.Contains(ConverterTargetKey, reopened.MasterReferences.Select(m => m.Master));
    }

    [Fact]
    public void Build_TwoMappingsSharingOneArma_WriteBothSlots_PrefixOnce_CountOnce()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);

        var male = new Variant(Gender.Male, WeightClass.Heavy, new[]
        {
            new Piece("IronCuirass", SyntheticSkyrimMods.MiniIronCuirassKey.ID, "32 Body", "IronCuirassAA", "meshes/armor/mini/IronCuirass/male.nif"),
        });
        var female = new Variant(Gender.Female, WeightClass.Heavy, new[]
        {
            new Piece("IronCuirass", SyntheticSkyrimMods.MiniIronCuirassKey.ID, "32 Body", "IronCuirassAA", "meshes/armor/mini/IronCuirass/female.nif"),
        });
        var catalog = new Catalog(
            new VanillaCatalogSource(dir.Root, new[] { SyntheticSkyrimMods.MiniUniverseFileName }),
            new[] { new ArmorSet("IronArmor", "Iron", new[] { male, female }) });
        var overhaul = CreateOverhaul(catalog);

        var donorId = Guid.NewGuid();
        var library = CreateLibrary(CreateDonor(donorId));
        overhaul.Mappings.Add(CreateMapping(overhaul, donorId, "meshes/donor/iron/m.nif", gender: Gender.Male));
        overhaul.Mappings.Add(CreateMapping(overhaul, donorId, "meshes/donor/iron/f.nif", gender: Gender.Female));

        var resolution = new TargetResolver().Resolve(overhaul);
        var plate = new PluginBuilder().Build(overhaul, resolution.Targets, library, Path.Combine(dir.Root, "Export"));

        Assert.Equal(1, plate.Report!.OverriddenRecords);

        using var reopened = SkyrimMod.CreateFromBinaryOverlay(plate.PluginPath, SkyrimRelease.SkyrimSE);

        var arma = reopened.ArmorAddons[SyntheticSkyrimMods.MiniIronCuirassAddonKey];
        Assert.NotNull(arma);
        Assert.Equal("UW_IronCuirassAA", arma.EditorID);
        Assert.Equal("meshes/donor/iron/m.nif", arma.WorldModel!.Male!.File!.GivenPath);
        Assert.Equal("meshes/donor/iron/f.nif", arma.WorldModel.Female!.File!.GivenPath);
    }

    // ---------------------------------------------------------------------------------------------
    // Amendment #8 patch-layer mesh shadowing (5.1.2)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolveEffectiveMesh_NoPatchAsset_UsesDonorMesh()
    {
        var donorId = Guid.NewGuid();
        var library = CreateLibrary(CreateDonor(donorId));
        var mapping = CreateMapping(CreateOverhaul(CreateIronCatalog("C:/DoesNotMatter")), donorId, "Meshes\\donor\\iron\\cuirass.nif");

        var effective = new PluginBuilder().ResolveEffectiveMesh(mapping, library);

        Assert.Equal("Meshes/donor/iron/cuirass.nif", effective.MeshPath);
        Assert.Equal(donorId, effective.MeshProviderAssetId);
        Assert.False(effective.ShadowedByBodyPatch);
        Assert.False(effective.ShadowedByPhysicsPatch);
    }

    [Fact]
    public void ResolveEffectiveMesh_BodyPatchShadows_NormalizedDataPath()
    {
        var donorId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();
        var library = CreateLibrary(
            CreateDonor(donorId),
            CreateDonor(bodyId, new[] { "Data\\meshes\\donor\\iron\\cuirass.nif" }, DonorAssetKind.BodyConversionPatch));
        var mapping = CreateMapping(CreateOverhaul(CreateIronCatalog("C:/DoesNotMatter")), donorId, "meshes/donor/iron/cuirass.nif", bodyConversionPatchId: bodyId);

        var effective = new PluginBuilder().ResolveEffectiveMesh(mapping, library);

        Assert.Equal("meshes/donor/iron/cuirass.nif", effective.MeshPath);
        Assert.Equal(bodyId, effective.MeshProviderAssetId);
        Assert.True(effective.ShadowedByBodyPatch);
        Assert.False(effective.ShadowedByPhysicsPatch);
    }

    [Fact]
    public void ResolveEffectiveMesh_PhysicsPatchShadowed_LastPayloadWins()
    {
        var donorId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();
        var physicsId = Guid.NewGuid();
        var library = CreateLibrary(
            CreateDonor(donorId),
            CreateDonor(bodyId, new[] { "meshes/donor/iron/cuirass.nif" }, DonorAssetKind.BodyConversionPatch),
            CreateDonor(physicsId, new[] { "data/meshes/donor/iron/cuirass.nif" }, DonorAssetKind.PhysicsPatch));
        var mapping = CreateMapping(
            CreateOverhaul(CreateIronCatalog("C:/DoesNotMatter")),
            donorId,
            "meshes/donor/iron/cuirass.nif",
            bodyConversionPatchId: bodyId,
            physicsPatchId: physicsId);

        var effective = new PluginBuilder().ResolveEffectiveMesh(mapping, library);

        Assert.Equal(physicsId, effective.MeshProviderAssetId);
        Assert.True(effective.ShadowedByBodyPatch);
        Assert.True(effective.ShadowedByPhysicsPatch);
    }

    [Fact]
    public void ResolveEffectiveMesh_PatchWithoutMatchingFile_StaysDonor()
    {
        var donorId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();
        var library = CreateLibrary(
            CreateDonor(donorId),
            CreateDonor(bodyId, new[] { "meshes/donor/iron/other.nif" }, DonorAssetKind.BodyConversionPatch));
        var mapping = CreateMapping(CreateOverhaul(CreateIronCatalog("C:/DoesNotMatter")), donorId, "meshes/donor/iron/cuirass.nif", bodyConversionPatchId: bodyId);

        var effective = new PluginBuilder().ResolveEffectiveMesh(mapping, library);

        Assert.Equal(donorId, effective.MeshProviderAssetId);
        Assert.False(effective.ShadowedByBodyPatch);
        Assert.False(effective.ShadowedByPhysicsPatch);
    }

    // ---------------------------------------------------------------------------------------------
    // Amendment #6 loose-path skip (5.1.1)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Build_LoosePathEquality_WritesNoOverrideRecord()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateIronCatalog(dir.Root));

        var donorId = Guid.NewGuid();
        var library = CreateLibrary(CreateDonor(donorId));
        overhaul.Mappings.Add(CreateMapping(overhaul, donorId, SyntheticSkyrimMods.MiniIronCuirassMalePath));

        var resolution = new TargetResolver().Resolve(overhaul);
        var plate = new PluginBuilder().Build(overhaul, resolution.Targets, library, Path.Combine(dir.Root, "Export"));

        Assert.Equal(0, plate.Report!.OverriddenRecords);
        Assert.Contains(plate.Report.Warnings, w => w.Message.Contains("wrote no ARMA override", StringComparison.Ordinal));

        using var reopened = SkyrimMod.CreateFromBinaryOverlay(plate.PluginPath, SkyrimRelease.SkyrimSE);
        Assert.Empty(reopened.ArmorAddons.Records);
    }

    [Fact]
    public void Build_LoosePathEquality_NormalizedMixedCaseBackslashes_Skips()
    {
        using var dir = new TestTempDir();
        SyntheticSkyrimMods.WriteMiniUniverse(dir.Root);
        var overhaul = CreateOverhaul(CreateIronCatalog(dir.Root));

        var donorId = Guid.NewGuid();
        var library = CreateLibrary(CreateDonor(donorId));
        overhaul.Mappings.Add(CreateMapping(overhaul, donorId, "Meshes\\Armor\\Mini\\IronCuirass\\Male.nif"));

        var resolution = new TargetResolver().Resolve(overhaul);
        var plate = new PluginBuilder().Build(overhaul, resolution.Targets, library, Path.Combine(dir.Root, "Export"));

        Assert.Equal(0, plate.Report!.OverriddenRecords);
        Assert.Contains(plate.Report.Warnings, w => w.Message.Contains("wrote no ARMA override", StringComparison.Ordinal));

        using var reopened = SkyrimMod.CreateFromBinaryOverlay(plate.PluginPath, SkyrimRelease.SkyrimSE);
        Assert.Empty(reopened.ArmorAddons.Records);
    }

    [Fact]
    public void Build_LoosePathEquality_CurrentPathNull_NeverSkips()
    {
        using var dir = new TestTempDir();
        WriteConverterTarget(dir.Root);
        var overhaul = CreateOverhaul(CreateConverterCatalog(dir.Root));

        var donorId = Guid.NewGuid();
        var library = CreateLibrary(CreateDonor(donorId));
        overhaul.Mappings.Add(CreateMapping(overhaul, donorId, "meshes/armor/shadow/female.nif", setId: "ShadowSet", pieceEditorId: "ShadowPlate"));

        // The ConverterTarget addon has no male slot (CurrentModelMalePath is null): a null current
        // path never equals the donor path, so the male override MUST be written even though the
        // donor equals the female side.
        var resolution = new TargetResolver().Resolve(overhaul);
        var plate = new PluginBuilder().Build(overhaul, resolution.Targets, library, Path.Combine(dir.Root, "Export"));

        Assert.Equal(1, plate.Report!.OverriddenRecords);

        using var reopened = SkyrimMod.CreateFromBinaryOverlay(plate.PluginPath, SkyrimRelease.SkyrimSE);
        Assert.Equal("meshes/armor/shadow/female.nif", reopened.ArmorAddons[ConverterTargetAddonKey].WorldModel!.Male!.File!.GivenPath);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private const string StoryTargetFileName = "StoryTarget.esp";
    private const string ConverterTargetFileName = "ConverterTarget.esp";

    private static ModKey StoryTargetKey => ModKey.FromName("StoryTarget", ModType.Plugin);
    private static FormKey StoryTargetAddonKey => new(StoryTargetKey, 0x800);
    private static ModKey ConverterTargetKey => ModKey.FromName("ConverterTarget", ModType.Plugin);
    private static FormKey ConverterTargetAddonKey => new(ConverterTargetKey, 0x900);

    private static Overhaul CreateOverhaul(Catalog catalog, string name = "TestOverhaul")
    {
        var project = new Project(Guid.NewGuid(), "PluginBuilderProject", "C:/Projects/Test");
        return new Overhaul(Guid.NewGuid(), name, project.Id, catalog.Source) { Catalog = catalog };
    }

    private static PieceMapping CreateMapping(
        Overhaul overhaul,
        Guid donorId,
        string donorMesh,
        Guid? bodyConversionPatchId = null,
        Guid? physicsPatchId = null,
        Gender gender = Gender.Male,
        bool story = false,
        string? setId = null,
        string? pieceEditorId = null)
    {
        if (story)
        {
            setId = "StorySet";
            pieceEditorId = "StoryArmor";
        }

        return new PieceMapping(
            Guid.NewGuid(),
            overhaul.Id,
            setId ?? "IronArmor",
            pieceEditorId ?? "IronCuirass",
            gender,
            donorId,
            "DonorPiece",
            donorMesh,
            bodyConversionPatchId,
            physicsPatchId);
    }

    private static UltimateWardrobe.Core.Domain.DonorLibrary CreateLibrary(params DonorAsset[] assets)
    {
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
        library.Assets.AddRange(assets);
        return library;
    }

    private static DonorAsset CreateDonor(
        Guid id,
        IReadOnlyList<string>? manifestPaths = null,
        DonorAssetKind kind = DonorAssetKind.FullReplacer)
    {
        var manifest = manifestPaths?.Select(p => new DonorFileEntry(p, 42)).ToList();
        return new DonorAsset(
            id,
            "donor-" + id.ToString("N") + ".7z",
            "C:/Donor/" + id,
            DateTime.UtcNow,
            "hash-" + id,
            kind,
            fileManifest: manifest);
    }

    private static Catalog CreateIronCatalog(string root)
    {
        var male = new Variant(Gender.Male, WeightClass.Heavy, new[]
        {
            new Piece("IronCuirass", SyntheticSkyrimMods.MiniIronCuirassKey.ID, "32 Body", "IronCuirassAA", SyntheticSkyrimMods.MiniIronCuirassMalePath),
        });
        return new Catalog(
            new VanillaCatalogSource(root, new[] { SyntheticSkyrimMods.MiniUniverseFileName }),
            new[] { new ArmorSet("IronArmor", "Iron", new[] { male }) });
    }

    private static Catalog CreateStoryCatalog(string root)
    {
        var male = new Variant(Gender.Male, WeightClass.Heavy, new[]
        {
            new Piece("StoryArmor", 0x801, "32 Body", "StoryArmorAA", "meshes/story/male.nif"),
        });
        return new Catalog(
            new StoryModCatalogSource(root, StoryTargetFileName),
            new[] { new ArmorSet("StorySet", "Story", new[] { male }) });
    }

    private static Catalog CreateConverterCatalog(string root)
    {
        var male = new Variant(Gender.Male, WeightClass.Heavy, new[]
        {
            new Piece("ShadowPlate", ConverterTargetAddonKey.ID, "32 Body", "ShadowPlateAA", "meshes/armor/shadow/female.nif"),
        });
        return new Catalog(
            new VanillaCatalogSource(root, new[] { ConverterTargetFileName }),
            new[] { new ArmorSet("ShadowSet", "Shadow", new[] { male }) });
    }

    private static void WriteStoryTarget(string directory)
    {
        var mod = new SkyrimMod(StoryTargetKey, SkyrimRelease.SkyrimSE);
        var addon = new ArmorAddon(StoryTargetAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "StoryArmorAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(MakeModel("meshes/story/male.nif"), MakeModel("meshes/story/female.nif")),
        };
        mod.ArmorAddons.Add(addon);
        mod.Armors.Add(new Armor(new FormKey(StoryTargetKey, 0x801), SkyrimRelease.SkyrimSE)
        {
            EditorID = "StoryArmor",
            Name = "Story Armor",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(StoryTargetAddonKey) },
        });
        mod.WriteToBinary(Path.Combine(directory, StoryTargetFileName));
    }

    private static void WriteConverterTarget(string directory)
    {
        var mod = new SkyrimMod(ConverterTargetKey, SkyrimRelease.SkyrimSE);
        var addon = new ArmorAddon(ConverterTargetAddonKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "ShadowPlateAA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            WorldModel = new GenderedItem<Model?>(null, MakeModel("meshes/armor/shadow/female.nif")),
        };
        mod.ArmorAddons.Add(addon);
        mod.Armors.Add(new Armor(new FormKey(ConverterTargetKey, 0x901), SkyrimRelease.SkyrimSE)
        {
            EditorID = "ShadowPlate",
            Name = "Shadow Plate",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
            Armature = new ExtendedList<IFormLinkGetter<IArmorAddonGetter>> { new FormLink<IArmorAddonGetter>(ConverterTargetAddonKey) },
        });
        mod.WriteToBinary(Path.Combine(directory, ConverterTargetFileName));
    }

    private static Model MakeModel(string path)
    {
        var model = new Model();
        var file = new AssetLink<SkyrimModelAssetType>();
        file.TrySetPath(path);
        model.File = file;
        return model;
    }
}