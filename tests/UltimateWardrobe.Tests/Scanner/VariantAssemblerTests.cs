using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Noggog;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class VariantAssemblerTests
{
    private static readonly ModKey TestMod = ModKey.FromName("AssemblerTest", ModType.Plugin);

    private static FormKey Key(uint id) => new(TestMod, id);

    [Fact]
    public void IronSet_AssemblesMaleHeavy_AndFemaleHeavyVariants_WithCorrectPieces()
    {
        using var dir = new TestTempDir();
        var sets = GroupingTestHarness.Assemble(dir, out _, out _);

        var iron = Assert.Single(sets, s => s.Id == "ironarmor");
        Assert.Equal("Iron Armor", iron.DisplayName);
        Assert.Equal(2, iron.Variants.Count);

        var male = iron.Variants[0];
        var female = iron.Variants[1];
        Assert.Equal(Gender.Male, male.Gender);
        Assert.Equal(WeightClass.Heavy, male.Weight);
        Assert.Equal(Gender.Female, female.Gender);
        Assert.Equal(WeightClass.Heavy, female.Weight);

        var expectedSlots = new[] { "32 Body", "33 Hands", "37 Feet" };
        var expectedEditorIds = new[] { "0A2C8841", "0A2C8842", "0A2C8843" };

        Assert.Equal(expectedSlots, male.Pieces.Select(p => p.Slot));
        Assert.Equal(expectedSlots, female.Pieces.Select(p => p.Slot));
        Assert.Equal(expectedEditorIds, male.Pieces.Select(p => p.EditorId));
        Assert.Equal(expectedEditorIds, female.Pieces.Select(p => p.EditorId));
    }

    [Fact]
    public void IronSet_SameArmoYieldsTwoPieces_SameEditorIdDifferentGender()
    {
        using var dir = new TestTempDir();
        var sets = GroupingTestHarness.Assemble(dir, out _, out _);

        var iron = Assert.Single(sets, s => s.Id == "ironarmor");
        var male = iron.Variants.Single(v => v.Gender == Gender.Male);
        var female = iron.Variants.Single(v => v.Gender == Gender.Female);

        for (var i = 0; i < male.Pieces.Count; i++)
        {
            Assert.Equal(male.Pieces[i].EditorId, female.Pieces[i].EditorId);
            Assert.Equal(male.Pieces[i].FormId, female.Pieces[i].FormId);

            var uniqueKeys = new HashSet<string>
            {
                $"{male.Pieces[i].EditorId}:{male.Gender}",
                $"{female.Pieces[i].EditorId}:{female.Gender}",
            };
            Assert.Equal(2, uniqueKeys.Count);
        }
    }

    [Fact]
    public void VariantsDeterministicAcrossRuns()
    {
        List<string> Capture()
        {
            using var dir = new TestTempDir();
            var sets = GroupingTestHarness.Assemble(dir, out _, out _);
            return sets
                .Select(s => $"{s.Id}:{string.Join("|", s.Variants.Select(v => $"{v.Gender}+{v.Weight}:{string.Join(",", v.Pieces.Select(p => p.EditorId))}"))}")
                .ToList();
        }

        var first = Capture();
        var second = Capture();
        Assert.Equal(first, second);
    }

    [Fact]
    public void SingleGenderSignal_AssemblesOneVariant()
    {
        using var dir = new TestTempDir();
        var index = GroupingTestHarness.BuildIndex(dir, out _);
        var member = MakeMember(
            "FemaleOnlyCuirass",
            Key(0x200),
            maleModel: false,
            femaleModel: true,
            BipedObjectFlag.Body,
            ArmorType.HeavyArmor);

        var result = SingleSetGrouping("singlegender", "Single Gender", member);
        var warnings = new List<ScanWarning>();

        var sets = VariantAssembler.Assemble(result, index, warnings);

        var set = Assert.Single(sets);
        var variant = Assert.Single(set.Variants);
        Assert.Equal(Gender.Female, variant.Gender);
        Assert.Equal(WeightClass.Heavy, variant.Weight);
        var piece = Assert.Single(variant.Pieces);
        Assert.Equal("FemaleOnlyCuirass", piece.EditorId);
        Assert.Equal("32 Body", piece.Slot);
        Assert.Empty(warnings);
    }

    [Fact]
    public void NoSignals_AssemblesUnisexVariant_AndWarns()
    {
        using var dir = new TestTempDir();
        var index = GroupingTestHarness.BuildIndex(dir, out _);
        var member = MakeMember(
            "NoSignalRobes",
            Key(0x201),
            maleModel: false,
            femaleModel: false,
            BipedObjectFlag.Body,
            ArmorType.HeavyArmor,
            meshPath: "meshes/clothes/x/y.nif");

        var result = SingleSetGrouping("nosignal", "No Signal", member);
        var warnings = new List<ScanWarning>();

        var sets = VariantAssembler.Assemble(result, index, warnings);

        var variant = Assert.Single(Assert.Single(sets).Variants);
        Assert.Equal(Gender.Unisex, variant.Gender);
        Assert.Equal(WeightClass.Heavy, variant.Weight);
        Assert.Single(variant.Pieces);
        Assert.Contains(warnings, w => w.EditorId == "NoSignalRobes" && w.Message.Contains("gender signal"));
    }

    [Fact]
    public void UnrecognizedSlot_FallsBackToRawBoldDescriptor()
    {
        using var dir = new TestTempDir();
        var index = GroupingTestHarness.BuildIndex(dir, out _);
        var member = MakeMember(
            "StrangeRing",
            Key(0x202),
            maleModel: true,
            femaleModel: false,
            (BipedObjectFlag)0,
            ArmorType.Clothing);

        var result = SingleSetGrouping("strange", "Strange", member);
        var warnings = new List<ScanWarning>();

        var sets = VariantAssembler.Assemble(result, index, warnings);

        var variant = Assert.Single(Assert.Single(sets).Variants);
        var piece = Assert.Single(variant.Pieces);
        Assert.Equal("BODT 0", piece.Slot);
    }

    [Fact]
    public void IronSet_FemaleVariantUsesFemaleMesh_PerGenderMesh()
    {
        using var dir = new TestTempDir();
        var index = GroupingTestHarness.BuildIndex(dir, out _);
        // Simulate Iron-like per-gender meshes: male = Armor/Iron/Male/... female = Armor/Iron/F/...
        var member = MakeMemberWithPerGenderMesh(
            "IronCuirassPerGender",
            Key(0x210),
            "meshes/armor/iron/male/cuirass_1.nif",
            "meshes/armor/iron/f/cuirass_1.nif",
            BipedObjectFlag.Body,
            ArmorType.HeavyArmor);

        var result = SingleSetGrouping("pergender", "Per Gender", member);
        var warnings = new List<ScanWarning>();

        var sets = VariantAssembler.Assemble(result, index, warnings);

        var set = Assert.Single(sets);
        Assert.Equal(2, set.Variants.Count);
        var male = set.Variants.Single(v => v.Gender == Gender.Male);
        var female = set.Variants.Single(v => v.Gender == Gender.Female);
        var malePiece = Assert.Single(male.Pieces);
        var femalePiece = Assert.Single(female.Pieces);
        Assert.Equal("meshes/armor/iron/male/cuirass_1.nif", malePiece.MeshPath);
        Assert.Equal("meshes/armor/iron/f/cuirass_1.nif", femalePiece.MeshPath);
        Assert.NotEqual(malePiece.MeshPath, femalePiece.MeshPath);
        Assert.Equal("IronCuirassPerGenderAA_M", malePiece.ArmaEditorId);
        Assert.Equal("IronCuirassPerGenderAA_F", femalePiece.ArmaEditorId);
    }

    [Fact]
    public void DualArmaSplit_GenderAggregation_ReturnsBothGenders_AndPerGenderMesh()
    {
        using var dir = new TestTempDir();
        var index = GroupingTestHarness.BuildIndex(dir, out _);
        // One ARMA male-only, another female-only - aggregation should yield both genders and correct meshes
        var maleAddon = new ArmorAddon(Key(0x211), SkyrimRelease.SkyrimSE)
        {
            EditorID = "SplitArmorAA_M",
            WorldModel = new GenderedItem<Model?>(Model("meshes/armor/split/male.nif"), null),
            WeightSliderEnabled = new GenderedItem<bool>(true, false),
        };
        var femaleAddon = new ArmorAddon(Key(0x212), SkyrimRelease.SkyrimSE)
        {
            EditorID = "SplitArmorAA_F",
            WorldModel = new GenderedItem<Model?>(null, Model("meshes/armor/split/female.nif")),
            WeightSliderEnabled = new GenderedItem<bool>(false, true),
        };
        var armor = new Armor(Key(0x213), SkyrimRelease.SkyrimSE)
        {
            EditorID = "SplitArmor",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = ArmorType.HeavyArmor },
        };
        var correlated = new CorrelatedArmor
        {
            EditorId = "SplitArmor",
            FormId = Key(0x213).ID,
            Armor = armor,
            FirstAddon = maleAddon,
            AllAddons = new[] { maleAddon, femaleAddon },
            MeshPathMale = "meshes/armor/split/male.nif",
            MeshPathFemale = "meshes/armor/split/female.nif",
            ArmaEditorIdMale = maleAddon.EditorID,
            ArmaEditorIdFemale = femaleAddon.EditorID,
            MeshPath = "meshes/armor/split/male.nif",
            BipedFlags = BipedObjectFlag.Body,
        };

        var genders = GenderWeightDetector.DetectGenders(correlated, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Male, Gender.Female }, genders);

        var result = SingleSetGrouping("split", "Split", correlated);
        var warnings = new List<ScanWarning>();
        var sets = VariantAssembler.Assemble(result, index, warnings);
        var set = Assert.Single(sets);
        Assert.Equal(2, set.Variants.Count);
        var male = set.Variants.Single(v => v.Gender == Gender.Male);
        var female = set.Variants.Single(v => v.Gender == Gender.Female);
        Assert.Equal("meshes/armor/split/male.nif", Assert.Single(male.Pieces).MeshPath);
        Assert.Equal("meshes/armor/split/female.nif", Assert.Single(female.Pieces).MeshPath);
    }

    private static GroupingResult SingleSetGrouping(string id, string displayName, CorrelatedArmor member)
    {
        return new GroupingResult
        {
            Sets = new[]
            {
                new GroupedSet { Id = id, DisplayName = displayName, Members = new[] { member } },
            },
            SkippedByReason = new Dictionary<SkipReason, int>(),
        };
    }

    private static CorrelatedArmor MakeMember(
        string editorId,
        FormKey key,
        bool maleModel,
        bool femaleModel,
        BipedObjectFlag flags,
        ArmorType armorType,
        string? meshPath = null)
    {
        var addon = new ArmorAddon(key, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            BodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = armorType },
            WorldModel = new GenderedItem<Model?>(maleModel ? Model() : null, femaleModel ? Model() : null),
        };

        var armor = new Armor(key, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            BodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = armorType },
        };

        return new CorrelatedArmor
        {
            EditorId = editorId,
            FormId = key.ID,
            Armor = armor,
            FirstAddon = addon,
            MeshPath = meshPath,
            BipedFlags = flags,
        };
    }

    private static CorrelatedArmor MakeMemberWithPerGenderMesh(
        string editorId,
        FormKey key,
        string maleMesh,
        string femaleMesh,
        BipedObjectFlag flags,
        ArmorType armorType)
    {
        var maleAddon = new ArmorAddon(new FormKey(key.ModKey, key.ID + 100), SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA_M",
            WorldModel = new GenderedItem<Model?>(Model(maleMesh), null),
            WeightSliderEnabled = new GenderedItem<bool>(true, false),
        };
        var femaleAddon = new ArmorAddon(new FormKey(key.ModKey, key.ID + 200), SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA_F",
            WorldModel = new GenderedItem<Model?>(null, Model(femaleMesh)),
            WeightSliderEnabled = new GenderedItem<bool>(false, true),
        };
        var armor = new Armor(key, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            BodyTemplate = new BodyTemplate { FirstPersonFlags = flags, ArmorType = armorType },
        };
        return new CorrelatedArmor
        {
            EditorId = editorId,
            FormId = key.ID,
            Armor = armor,
            FirstAddon = maleAddon,
            AllAddons = new[] { maleAddon, femaleAddon },
            MeshPathMale = maleMesh,
            MeshPathFemale = femaleMesh,
            ArmaEditorIdMale = maleAddon.EditorID,
            ArmaEditorIdFemale = femaleAddon.EditorID,
            MeshPath = maleMesh,
            ArmaEditorId = maleAddon.EditorID,
            BipedFlags = flags,
        };
    }

    private static Model? Model()
    {
        var model = new Model();
        var file = new AssetLink<SkyrimModelAssetType>();
        Assert.True(file.TrySetPath("meshes/armor/test/mesh.nif"), "Model path was rejected (must be a full path).");
        model.File = file;
        return model;
    }

    private static Model? Model(string path)
    {
        var model = new Model();
        var file = new AssetLink<SkyrimModelAssetType>();
        Assert.True(file.TrySetPath(path), $"Model path '{path}' was rejected (must be a full path).");
        model.File = file;
        return model;
    }
}