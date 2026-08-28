using FluentAssertions;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorNameHeuristicsTests
{
    [Theory]
    [InlineData("cuirass", "cuirass")]
    [InlineData("cuirass_1", "cuirass")]
    [InlineData("cuirass_0", "cuirass")]
    [InlineData("cuirass_1st", "cuirass")]
    [InlineData("cuirass_1_0", "cuirass")]
    [InlineData("cuirass_10", "cuirass_10")]
    [InlineData("_1", "")]
    public void BaseStem_Strips_Weight_Markers(string stem, string expected)
    {
        DonorNameHeuristics.BaseStem(stem).Should().Be(expected);
    }

    [Theory]
    [InlineData("cuirass_1", 0)]
    [InlineData("cuirass_0", 1)]
    [InlineData("cuirass_1st", 2)]
    [InlineData("cuirass", 3)]
    [InlineData("cuirass_10", 3)]
    public void PrimaryRank_Prefers_Then_Zero_Then_FirstPerson_Then_Plain(string stem, int expected)
    {
        DonorNameHeuristics.PrimaryRank(stem, DonorNameHeuristics.BaseStem(stem)).Should().Be(expected);
    }

    [Theory]
    [InlineData("cuirass", "Cuirass")]
    [InlineData("cuirass_f", "Cuirass")]
    [InlineData("gauntlets_0", "Gauntlets")]
    [InlineData("helmet_m", "Helmet")]
    [InlineData("boots_1st", "Boots")]
    [InlineData("robes", "Robes")]
    [InlineData("randommesh", null)]
    public void PieceTypeFromStem_Matches_EquipmentWords_CaseInsensitively(string stem, string? expected)
    {
        DonorNameHeuristics.PieceTypeFromStem(stem).Should().Be(expected);
    }

    [Fact]
    public void GenderFrom_Stem_Markers_Win()
    {
        DonorNameHeuristics.GenderFrom("cuirass_f", "meshes/armor/iron/f/cuirass_f.nif").Should().Be(Gender.Female);
        DonorNameHeuristics.GenderFrom("cuirass_m", "meshes/armor/iron/m/cuirass_m.nif").Should().Be(Gender.Male);
    }

    [Fact]
    public void GenderFrom_Female_Male_Folder_Segments()
    {
        DonorNameHeuristics.GenderFrom("cuirass", "meshes/armor/iron/female/cuirass.nif").Should().Be(Gender.Female);
        DonorNameHeuristics.GenderFrom("cuirass", "meshes/armor/iron/male/cuirass.nif").Should().Be(Gender.Male);
    }

    [Fact]
    public void GenderFrom_Single_Char_Segments_Are_Signals()
    {
        DonorNameHeuristics.GenderFrom("cuirass", "meshes/armor/iron/f/cuirass.nif").Should().Be(Gender.Female);
        DonorNameHeuristics.GenderFrom("cuirass", "meshes/armor/iron/m/cuirass.nif").Should().Be(Gender.Male);
    }

    [Fact]
    public void GenderFrom_Mixed_Signals_Stem_Wins_Over_Folder()
    {
        DonorNameHeuristics.GenderFrom("cuirass_m", "meshes/armor/iron/f/cuirass_m.nif").Should().Be(Gender.Male);
    }

    [Fact]
    public void GenderFrom_No_Signal_Returns_Null()
    {
        DonorNameHeuristics.GenderFrom("cuirass", "meshes/armor/iron/cuirass.nif").Should().BeNull();
    }

    [Theory]
    [InlineData("meshes/armor/heavyiron/f/cuirass.nif", WeightClass.Heavy)]
    [InlineData("meshes/armor/lightleather/f/cuirass.nif", WeightClass.Light)]
    [InlineData("meshes/clothes/collegerobes/f/robes.nif", WeightClass.Clothing)]
    [InlineData("meshes/armor/iron/f/cuirass.nif", WeightClass.Any)]
    [InlineData("meshes/armor/iron/f/cuirass_clothes.nif", WeightClass.Clothing)]
    public void WeightFromPath_Segment_Tokens(string path, WeightClass expected)
    {
        DonorNameHeuristics.WeightFromPath(path).Should().Be(expected);
    }

    [Fact]
    public void WeightFromPath_Heavy_Wins_Over_Ligth_And_Clothes()
    {
        DonorNameHeuristics.WeightFromPath("meshes/armor/heavy/clothes/f/cuirass.nif").Should().Be(WeightClass.Heavy);
    }
}