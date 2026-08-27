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

public sealed class GenderWeightDetectorTests
{
    private static readonly ModKey TestMod = ModKey.FromName("GenderTest", ModType.Plugin);

    private static FormKey Key(uint id) => new(TestMod, id);

    private static RecordIndex BuildIndex(TestTempDir dir, out List<ScanWarning> warnings)
    {
        return GroupingTestHarness.BuildIndex(dir, out warnings);
    }

    [Fact]
    public void DetectWeight_HeavyBeatsLightAndClothing()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeWeightArmor(Key(0x100), [SyntheticGroupingUniverse.HeavyKeywordKey, SyntheticGroupingUniverse.LightKeywordKey, SyntheticGroupingUniverse.ClothingKeywordKey]);

        Assert.Equal(WeightClass.Heavy, GenderWeightDetector.DetectWeight(MakeCorrelated(armor), index));
    }

    [Fact]
    public void DetectWeight_LightBeatsClothing()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeWeightArmor(Key(0x101), [SyntheticGroupingUniverse.LightKeywordKey, SyntheticGroupingUniverse.ClothingKeywordKey]);

        Assert.Equal(WeightClass.Light, GenderWeightDetector.DetectWeight(MakeCorrelated(armor), index));
    }

    [Fact]
    public void DetectWeight_ClothingOnly()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeWeightArmor(Key(0x102), [SyntheticGroupingUniverse.ClothingKeywordKey]);

        Assert.Equal(WeightClass.Clothing, GenderWeightDetector.DetectWeight(MakeCorrelated(armor), index));
    }

    [Fact]
    public void DetectWeight_NoKeyword_FallsBackToArmorType()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);

        Assert.Equal(WeightClass.Heavy, GenderWeightDetector.DetectWeight(MakeCorrelated(MakeWeightArmor(Key(0x103), [], ArmorType.HeavyArmor)), index));
        Assert.Equal(WeightClass.Light, GenderWeightDetector.DetectWeight(MakeCorrelated(MakeWeightArmor(Key(0x104), [], ArmorType.LightArmor)), index));
        Assert.Equal(WeightClass.Clothing, GenderWeightDetector.DetectWeight(MakeCorrelated(MakeWeightArmor(Key(0x105), [], ArmorType.Clothing)), index));
    }

    [Fact]
    public void DetectWeight_NoKeywordAndNoArmorType_ReturnsAny()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeWeightArmor(Key(0x106), [], armorType: null);

        Assert.Equal(WeightClass.Any, GenderWeightDetector.DetectWeight(MakeCorrelated(armor), index));
    }

    [Fact]
    public void DetectWeight_UnresolvableKeywordLink_SkippedNotThrown()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeWeightArmor(Key(0x107), [Key(0x7FF0)], ArmorType.LightArmor);

        Assert.Equal(WeightClass.Light, GenderWeightDetector.DetectWeight(MakeCorrelated(armor), index));
    }

    [Fact]
    public void DetectGenders_BothModels_ReturnsMaleAndFemale()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(MakeSignalArmor("IronCuirass", Key(0x200), maleModel: true, femaleModel: true));

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Male, Gender.Female }, genders);
    }

    [Fact]
    public void DetectGenders_MaleModelOnly_ReturnsMale()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(MakeSignalArmor("MaleOnlyCuirass", Key(0x201), maleModel: true, femaleModel: false));

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Male }, genders);
    }

    [Fact]
    public void DetectGenders_FemaleModelOnly_ReturnsFemale()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(MakeSignalArmor("FemaleOnlyCuirass", Key(0x202), maleModel: false, femaleModel: true));

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Female }, genders);
    }

    [Fact]
    public void DetectGenders_WeightSliders_CountAsSignals()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);

        var maleOnly = MakeCorrelated(MakeSignalArmor("MaleSlider", Key(0x203), maleModel: false, femaleModel: false, maleSlider: true, femaleSlider: false));
        Assert.Equal(new[] { Gender.Male }, GenderWeightDetector.DetectGenders(maleOnly, index, new List<ScanWarning>()));

        var both = MakeCorrelated(MakeSignalArmor("BothSlider", Key(0x204), maleModel: false, femaleModel: false, maleSlider: true, femaleSlider: true));
        Assert.Equal(new[] { Gender.Male, Gender.Female }, GenderWeightDetector.DetectGenders(both, index, new List<ScanWarning>()));
    }

    [Fact]
    public void DetectGenders_NoSignals_ReturnsUnisex_AndWarns()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var warnings = new List<ScanWarning>();
        var armor = MakeCorrelated(MakeSignalArmor("NoSignalRobes", Key(0x205), maleModel: false, femaleModel: false), meshPath: "meshes/clothes/x/y.nif");

        var genders = GenderWeightDetector.DetectGenders(armor, index, warnings);

        Assert.Equal(new[] { Gender.Unisex }, genders);
        var warning = Assert.Single(warnings);
        Assert.Contains("gender signal", warning.Message);
        Assert.Equal("NoSignalRobes", warning.EditorId);
    }

    [Fact]
    public void DetectGenders_ExplicitEditorId_OOverridesBothModels()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(MakeSignalArmor("IronCuirass_female", Key(0x206), maleModel: true, femaleModel: true));

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Female }, genders);
    }

    [Fact]
    public void DetectGenders_ExplicitEditorId_DashedMarker()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(MakeSignalArmor("IronCuirass-male", Key(0x207), maleModel: true, femaleModel: true));

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Male }, genders);
    }

    [Fact]
    public void DetectGenders_ExplicitMeshFolder_OverridesSignals()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(
            MakeSignalArmor("IronCuirass", Key(0x208), maleModel: true, femaleModel: true),
            meshPath: "meshes/armor/iron/female/cuirass.nif");

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Female }, genders);
    }

    [Fact]
    public void DetectGenders_ExplicitEditorId_WinsOverMeshFolder()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(
            MakeSignalArmor("IronCuirass_male", Key(0x209), maleModel: true, femaleModel: true),
            meshPath: "meshes/armor/iron/female/cuirass.nif");

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Male }, genders);
    }

    [Fact]
    public void DetectGenders_AmbiguousMesh_WithNoSignals_FallsBackToUnisex()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var armor = MakeCorrelated(
            MakeSignalArmor("AmbiguousBoots", Key(0x20A), maleModel: false, femaleModel: false),
            meshPath: "meshes/armor/x/male/feet/female/boots.nif");

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());
        Assert.Equal(new[] { Gender.Unisex }, genders);
    }

    [Fact]
    public void DetectGenders_UnresolvableRace_WithNoSignals_FallsBackToUnisex()
    {
        using var dir = new TestTempDir();
        var index = BuildIndex(dir, out _);
        var addon = MakeSignalArmor("RaceSignedRobes", Key(0x20B), maleModel: false, femaleModel: false);
        var armor = MakeCorrelated(addon, raceLink: new FormLinkNullable<IRaceGetter>(Key(0x7FF1)));

        var genders = GenderWeightDetector.DetectGenders(armor, index, new List<ScanWarning>());

        Assert.Equal(new[] { Gender.Unisex }, genders);
    }

    [Fact]
    public void RaceGenderHint_MapsEditorIdMarkers()
    {
        Assert.Equal(Gender.Female, GenderWeightDetector.RaceGenderHint(MakeRace("FemaleHumanoid")));
        Assert.Equal(Gender.Male, GenderWeightDetector.RaceGenderHint(MakeRace("OrsimerMale")));
        Assert.Null(GenderWeightDetector.RaceGenderHint(MakeRace("Nord")));
        Assert.Null(GenderWeightDetector.RaceGenderHint(null));
    }

    [Fact]
    public void ExplicitFromEditorId_RecognizesMarkers()
    {
        Assert.Equal(Gender.Female, GenderWeightDetector.ExplicitFromEditorId("IronCuirass_female"));
        Assert.Equal(Gender.Female, GenderWeightDetector.ExplicitFromEditorId("IronCuirass_F"));
        Assert.Equal(Gender.Male, GenderWeightDetector.ExplicitFromEditorId("IronCuirass-male"));
        Assert.Null(GenderWeightDetector.ExplicitFromEditorId("IronCuirass"));
        Assert.Null(GenderWeightDetector.ExplicitFromEditorId(null));
    }

    [Fact]
    public void ExplicitFromMeshPath_RecognizesGenderFolders()
    {
        Assert.Equal(Gender.Female, GenderWeightDetector.ExplicitFromMeshPath("meshes/armor/iron/female/cuirass.nif"));
        Assert.Equal(Gender.Male, GenderWeightDetector.ExplicitFromMeshPath(@"meshes/armor/iron\male\cuirass.nif"));
        Assert.Null(GenderWeightDetector.ExplicitFromMeshPath("meshes/armor/iron/cuirass.nif"));
        Assert.Null(GenderWeightDetector.ExplicitFromMeshPath(null));
    }

    private static Armor MakeWeightArmor(FormKey key, IReadOnlyList<FormKey> keywordKeys, ArmorType? armorType = ArmorType.HeavyArmor)
    {
        var armor = new Armor(key, SkyrimRelease.SkyrimSE)
        {
            EditorID = "WeightTestArmor",
            BodyTemplate = armorType is null ? null : new BodyTemplate { FirstPersonFlags = BipedObjectFlag.Body, ArmorType = armorType.Value },
        };

        if (keywordKeys.Count > 0)
        {
            var keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
            foreach (var k in keywordKeys)
            {
                keywords.Add(new FormLink<IKeywordGetter>(k));
            }

            armor.Keywords = keywords;
        }

        return armor;
    }

    private static ArmorAddon MakeSignalArmor(
        string editorId,
        FormKey key,
        bool maleModel,
        bool femaleModel,
        bool maleSlider = false,
        bool femaleSlider = false)
    {
        return new ArmorAddon(key, SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId + "AA",
            WorldModel = new GenderedItem<Model?>(maleModel ? Model("meshes/armor/test/male.nif") : null, femaleModel ? Model("meshes/armor/test/female.nif") : null),
            WeightSliderEnabled = new GenderedItem<bool>(maleSlider, femaleSlider),
        };
    }

    private static CorrelatedArmor MakeCorrelated(
        Armor armor,
        string? meshPath = null,
        FormLinkNullable<IRaceGetter>? raceLink = null)
    {
        return new CorrelatedArmor
        {
            EditorId = armor.EditorID ?? $"FormId:{armor.FormKey.IDString()}",
            FormId = armor.FormKey.ID,
            Armor = armor,
            FirstAddon = null,
            MeshPath = meshPath,
            RaceLink = raceLink ?? new FormLinkNullable<IRaceGetter>(),
        };
    }

    private static CorrelatedArmor MakeCorrelated(
        ArmorAddon addon,
        string? meshPath = null,
        FormLinkNullable<IRaceGetter>? raceLink = null)
    {
        var armor = new Armor(Key(addon.FormKey.ID), SkyrimRelease.SkyrimSE)
        {
            EditorID = addon.EditorID?.Replace("AA", ""),
        };

        return new CorrelatedArmor
        {
            EditorId = armor.EditorID ?? $"FormId:{armor.FormKey.IDString()}",
            FormId = armor.FormKey.ID,
            Armor = armor,
            FirstAddon = addon,
            MeshPath = meshPath,
            RaceLink = raceLink ?? new FormLinkNullable<IRaceGetter>(),
        };
    }

    private static Model? Model(string path)
    {
        var model = new Model();
        var file = new AssetLink<SkyrimModelAssetType>();
        Assert.True(file.TrySetPath(path), $"Model path '{path}' was rejected (must be a full path).");
        model.File = file;
        return model;
    }

    private static Race MakeRace(string editorId)
    {
        return new Race(Key(0x300), SkyrimRelease.SkyrimSE) { EditorID = editorId };
    }
}