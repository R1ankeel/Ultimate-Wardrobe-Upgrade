using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class PieceTypeDetectorTests
{
    [Theory]
    [InlineData("IronGauntlets", "Gauntlets")]
    [InlineData("Iron-Boots", "Boots")]
    [InlineData("DLC2NordicCarvedCuirass", "Cuirass")]
    [InlineData("DraugrAmulet", "Amulet")]
    [InlineData("ArchmageCirclet", "Circlet")]
    [InlineData("ClothesCollegeRobesNoHood", "Hood")]
    [InlineData("ClothesVampireRobes", "Robes")]
    public void FromEditorId_SuffixTokens(string editorId, string expected)
    {
        Assert.Equal(expected, PieceTypeDetector.FromEditorId(editorId));
    }

    [Fact]
    public void FromEditorId_NoKnownSuffix_ReturnsNull()
    {
        Assert.Null(PieceTypeDetector.FromEditorId("MysteryThing"));
        Assert.Null(PieceTypeDetector.FromEditorId("DLC2NordicCarved"));
    }

    [Theory]
    [InlineData(BipedObjectFlag.Body, "Cuirass")]
    [InlineData(BipedObjectFlag.Hands, "Gauntlets")]
    [InlineData(BipedObjectFlag.Forearms, "Bracers")]
    [InlineData(BipedObjectFlag.Feet, "Boots")]
    [InlineData(BipedObjectFlag.Head, "Helmet")]
    [InlineData(BipedObjectFlag.Head | BipedObjectFlag.Circlet, "Circlet")]
    [InlineData(BipedObjectFlag.Shield, "Shield")]
    [InlineData(BipedObjectFlag.Hair, "Hood")]
    public void FromFlags_PrimarySlotMapsToPieceType(BipedObjectFlag flags, string expected)
    {
        Assert.Equal(expected, PieceTypeDetector.FromFlags(flags));
    }

    [Fact]
    public void FromFlags_NoSlot_ReturnsNull()
    {
        Assert.Null(PieceTypeDetector.FromFlags((BipedObjectFlag)0));
    }

    [Fact]
    public void Detect_GauntletsSuffixedEditorId_WithHandsSlot_StaysGauntlets_NoWarning()
    {
        var warnings = new List<ScanWarning>();
        var type = PieceTypeDetector.Detect("Iron-Gauntlets", BipedObjectFlag.Hands, warnings);

        Assert.Equal("Gauntlets", type);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Detect_ConflictingEvidence_WarnsButStillReturnsEditorIdSignal()
    {
        var warnings = new List<ScanWarning>();
        var type = PieceTypeDetector.Detect("IronGauntlets", BipedObjectFlag.Feet, warnings);

        Assert.Equal("Gauntlets", type);
        var warning = Assert.Single(warnings);
        Assert.Contains("IronGauntlets", warning.Message);
        Assert.Contains("slot flags indicate 'Boots'", warning.Message);
    }

    [Fact]
    public void Detect_MatchingSignals_ReturnEditorIdSignal_NoWarning()
    {
        var warnings = new List<ScanWarning>();

        Assert.Equal("Cuirass", PieceTypeDetector.Detect("IronCuirass", BipedObjectFlag.Body, warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Detect_NoEditorIdSignal_UsesSlotFlags()
    {
        var type = PieceTypeDetector.Detect("MysteryPart", BipedObjectFlag.Feet, new List<ScanWarning>());

        Assert.Equal("Boots", type);
    }
}