using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class KeyNormalizerTests
{
    [Theory]
    [InlineData("ArmorIronCuirass", "iron", "Iron")]
    [InlineData("ClothesCollegeRobesNoHood", "collegerobes", "College Robes")]
    [InlineData("DLC2NordicCarvedPlate", "nordiccarved", "Nordic Carved")]
    [InlineData("DLC2NordicCarvedGauntlets", "nordiccarved", "Nordic Carved")]
    [InlineData("ccBGSSSE063-ba_elvencuirass", "elven", "Elven")]
    [InlineData("ccBGSSSE063-ba_elven_armor", "elven", "Elven")]
    [InlineData("zzzMysteryBoots", "mystery", "Mystery")]
    [InlineData("AANordCuirassReplacer", "cuirassreplacer", "Cuirass Replacer")]
    [InlineData("Iron_Armor", "iron", "Iron")]
    [InlineData("ArmorSteelBootsA", "steel", "Steel")]
    [InlineData("ArmorSteelHelmetB", "steel", "Steel")]
    [InlineData("ArmorLeatherGauntletsC", "leather", "Leather")]
    public void NormalizeEditorId_CorpusOfRealLookingIds(string editorId, string expectedId, string expectedName)
    {
        var key = KeyNormalizer.NormalizeEditorId(editorId);

        Assert.NotNull(key);
        Assert.Equal(expectedId, key!.Id);
        Assert.Equal(expectedName, key.DisplayName);
    }

    [Fact]
    public void NormalizeEditorId_PieceWordOnly_FallsThrough()
    {
        Assert.Null(KeyNormalizer.NormalizeEditorId("Gauntlets"));
        Assert.Null(KeyNormalizer.NormalizeEditorId("Clothes"));
        Assert.Null(KeyNormalizer.NormalizeEditorId("CuirassAA"));
    }

    [Fact]
    public void NormalizeEditorId_NullOrWhitespace_ReturnsNull()
    {
        Assert.Null(KeyNormalizer.NormalizeEditorId(null));
        Assert.Null(KeyNormalizer.NormalizeEditorId("  "));
    }

    [Fact]
    public void NormalizeEditorId_StripsNonAlphanumerics_AndLowercasesInvariant()
    {
        var key = KeyNormalizer.NormalizeEditorId("Some_Weird.Id-1!");

        Assert.NotNull(key);
        Assert.Equal("someweirdid1", key!.Id);
        Assert.Equal("Some Weird Id1", key.DisplayName);
    }

    [Theory]
    [InlineData("DLC2NordicCarved", "nordiccarved", "Nordic Carved")]
    [InlineData("IronArmor", "ironarmor", "Iron Armor")]
    [InlineData("aaSharedSet", "aasharedset", "Aa Shared Set")]
    public void NormalizeOutfitEditorId_SamePipelineWithoutPieceStripping(string editorId, string expectedId, string expectedName)
    {
        var key = KeyNormalizer.NormalizeOutfitEditorId(editorId);

        Assert.NotNull(key);
        Assert.Equal(expectedId, key!.Id);
        Assert.Equal(expectedName, key.DisplayName);
    }

    [Fact]
    public void NormalizeOutfitEditorId_KeepsPieceSuffixes_BecauseSplitSetJoinsViaSuffix()
    {
        var outfitKey = KeyNormalizer.NormalizeOutfitEditorId("DLC2NordicCarved");
        var fallbackPieceKey = KeyNormalizer.NormalizeEditorId("DLC2NordicCarvedGauntlets");

        Assert.NotNull(outfitKey);
        Assert.NotNull(fallbackPieceKey);
        Assert.Equal(outfitKey!.Id, fallbackPieceKey!.Id);
    }

    [Theory]
    [InlineData("meshes/armor/iron/cuirass_1.nif", "iron", "Iron")]
    [InlineData("meshes/armor/vigilant/cuirass.nif", "vigilant", "Vigilant")]
    [InlineData("meshes/clothes/college/robes.nif", "college", "College")]
    [InlineData("meshes/armor/iron_male/cuirass.nif", "iron", "Iron")]
    [InlineData("armor/steel/steel_boots_1.nif", "steel", "Steel")]
    public void NormalizeMeshFolder_FolderAfterArmorOrClothesMarker(string meshPath, string expectedId, string expectedName)
    {
        var key = KeyNormalizer.NormalizeMeshFolder(meshPath);

        Assert.NotNull(key);
        Assert.Equal(expectedId, key!.Id);
        Assert.Equal(expectedName, key.DisplayName);
    }

    [Fact]
    public void NormalizeMeshFolder_UsesLastSegment_WhenNoMarker()
    {
        var key = KeyNormalizer.NormalizeMeshFolder(@"meshes\somepack\cuirass_1.nif");

        Assert.NotNull(key);
        Assert.Equal("somepack", key!.Id);
    }

    [Fact]
    public void NormalizeMeshFolder_NullOrWhitespace_ReturnsNull()
    {
        Assert.Null(KeyNormalizer.NormalizeMeshFolder(null));
        Assert.Null(KeyNormalizer.NormalizeMeshFolder("   "));
    }

    [Fact]
    public void NormalizedIdCoincides_ForVariantStopWordAndBase()
    {
        var baseKey = KeyNormalizer.NormalizeEditorId("ClothesCollegeRobesHood");
        var variantKey = KeyNormalizer.NormalizeEditorId("ClothesCollegeRobesNoHood");

        Assert.NotNull(baseKey);
        Assert.NotNull(variantKey);
        Assert.Equal(baseKey!.Id, variantKey!.Id);
    }
}